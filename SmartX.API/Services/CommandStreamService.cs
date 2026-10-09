using SmartX.Shared;

namespace SmartX.Api.Services;

public class CommandStreamService
{
    // Lock object — the API handles many requests at the same time.
    // Only one thread may touch the collections at once, or data gets corrupted.
    private readonly object _gate = new();

    // The predictive engine that learns from alerts, searches and commands.
    private readonly ActionEngine _engine;

    // Key = MAC address, Value = the device. O(1) lookup when a packet arrives.
    private readonly Dictionary<string, DeviceInfo> _devices = new();

    // FIFO line for normal telemetry — first packet in is first processed.
    private readonly Queue<IncomingTelemetry> _standardQueue = new();

    // Emergency line — critical alerts skip the standard queue.
    // Lower number comes out first. Using timestamp ticks means the oldest critical alert goes first.
    private readonly PriorityQueue<IncomingTelemetry, long> _priorityQueue = new();

    // Recent processed events shown in the UI. Insert at front so newest are first
    // and trim to a reasonable limit to avoid unbounded memory growth.
    private readonly List<ProcessedEvent> _recentEvents = new();

    // Per-device timeline history for plotting. SortedDictionary keeps entries
    // ordered by timestamp automatically, so the graph never needs re-sorting,
    // and the oldest entry is always the first key (cheap to trim).
    private readonly Dictionary<string, SortedDictionary<DateTime, double>> _history = new();

    // Currently active alerts, e.g. "AA:BB:CC:00:00:02|LOW".
    // A HashSet only stores unique values, so the same alert for the same
    // sensor can never be raised twice — duplicates are rejected by Add().
    private readonly HashSet<string> _activeAlerts = new();

    // How many duplicate alerts the HashSet rejected (shown on the dashboard).
    private int _duplicatesBlocked;

    // History of manual override commands. Last command in is the first one
    // undone (LIFO) — exactly how Ctrl+Z works.
    private readonly Stack<CommandRecord> _undoStack = new();

    // Devices the simulator must keep silent until a given time (to simulate a dropout).
    private readonly Dictionary<string, DateTime> _offlineUntil = new();

    private static readonly TimeSpan DisconnectAfter = TimeSpan.FromSeconds(15);
    private const int MaxEvents = 50;
    private const int MaxHistoryPerDevice = 300;

    // Lets the dashboard pause/resume the device simulator.
    public volatile bool SimulatorEnabled = true;

    public CommandStreamService(ActionEngine engine)
    {
        _engine = engine;
        SeedDevices();
    }

    private void SeedDevices()
    {
        AddDevice("AA:BB:CC:00:00:01", "Moisture Sensor A", SensorCategory.Environmental, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:02", "Moisture Sensor B", SensorCategory.Environmental, "Zone 1 / Sub-Zone B");
        AddDevice("AA:BB:CC:00:00:03", "Moisture Sensor C", SensorCategory.Environmental, "Zone 2 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:04", "Smart Meter 1", SensorCategory.Power, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:05", "Smart Meter 2", SensorCategory.Power, "Zone 2 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:06", "Pump A", SensorCategory.Actuator, "Zone 1 / Sub-Zone B");
        AddDevice("AA:BB:CC:00:00:07", "Pump B", SensorCategory.Actuator, "Zone 2 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:08", "Relay 3", SensorCategory.Actuator, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:09", "Main Valve", SensorCategory.Actuator, "Zone 1 / Sub-Zone B");
    }

    private void AddDevice(string mac, string name, SensorCategory category, string location)
    {
        _devices[mac] = new DeviceInfo
        {
            NodeId = mac,
            Name = name,
            Category = category,
            Location = location,
            LastSeen = DateTime.UtcNow,
            State = TileState.Normal
        };
    }

    // ───────────────────────── Lookups ─────────────────────────

    // Public lookup — takes the lock itself. Returns a copy so callers can't
    // change the live device outside the lock.
    public DeviceInfo? GetDevice(string mac)
    {
        lock (_gate)
        {
            var device = GetDeviceUnlocked(mac);
            return device is null ? null : Clone(device);
        }
    }

    // Same lookup without the lock — only call this when you're ALREADY inside lock (_gate).
    private DeviceInfo? GetDeviceUnlocked(string mac)
    {
        return _devices.TryGetValue(mac, out var device) ? device : null;
    }

    // MAC addresses of every device that sends readings (everything except actuators).
    public List<string> GetSensorIds()
    {
        lock (_gate)
        {
            return _devices.Values
                .Where(d => d.Category != SensorCategory.Actuator)
                .Select(d => d.NodeId)
                .ToList();
        }
    }

    private static DeviceInfo Clone(DeviceInfo d) => new()
    {
        NodeId = d.NodeId,
        Name = d.Name,
        Category = d.Category,
        Location = d.Location,
        LastValue = d.LastValue,
        LastSeen = d.LastSeen,
        State = d.State,
        IsOn = d.IsOn
    };

    // Decides how serious a reading is, based on what kind of device sent it.
    private static AlertSeverity Classify(DeviceInfo device, double value)
    {
        return device.Category switch
        {
            // Soil moisture %: too dry is dangerous
            SensorCategory.Environmental when value < 20 => AlertSeverity.Critical,
            SensorCategory.Environmental when value < 30 => AlertSeverity.Warning,

            // Watts: a spike is dangerous
            SensorCategory.Power when value > 3000 => AlertSeverity.Critical,
            SensorCategory.Power when value > 2200 => AlertSeverity.Warning,

            _ => AlertSeverity.Normal
        };
    }

    // ───────────────────────── Queues ─────────────────────────

    // A packet arrives → critical ones go to the emergency line, everything else to the normal line.
    // Returns the lane it went into so the API can tell the device.
    public string Enqueue(IncomingTelemetry packet)
    {
        lock (_gate)
        {
            var device = GetDeviceUnlocked(packet.NodeId);
            if (device is null) return "UNKNOWN_DEVICE";

            var severity = Classify(device, packet.Value);

            if (severity == AlertSeverity.Critical)
            {
                _priorityQueue.Enqueue(packet, packet.Timestamp.Ticks);
                return "PRIORITY";
            }

            _standardQueue.Enqueue(packet);
            return "STANDARD";
        }
    }

    // How many normal packets are waiting.
    public int GetQueueDepth()
    {
        lock (_gate)
        {
            return _standardQueue.Count;
        }
    }

    // How many critical packets are waiting.
    public int GetPriorityQueueDepth()
    {
        lock (_gate)
        {
            return _priorityQueue.Count;
        }
    }

    // Called every 500ms by QueueProcessorWorker.
    // ALWAYS drains the priority queue fully first, then up to 'max' standard packets.
    // Returns the number of processed packets.
    public int ProcessBatch(int max = 20)
    {
        var processed = 0;

        lock (_gate)
        {
            // 1. Critical alerts first — they bypass the normal line completely.
            while (_priorityQueue.Count > 0)
            {
                ProcessPacketUnlocked(_priorityQueue.Dequeue(), "PRIORITY");
                processed++;
            }

            // 2. Then normal packets, first-in-first-out.
            var standardDone = 0;
            while (standardDone < max && _standardQueue.Count > 0)
            {
                ProcessPacketUnlocked(_standardQueue.Dequeue(), "STANDARD");
                standardDone++;
                processed++;
            }

            // 3. Look for sensors that have gone quiet.
            CheckDisconnectsUnlocked(DateTime.UtcNow);
        }

        return processed;
    }

    // Applies one packet to the system: updates the device, alerts, event feed and history.
    private void ProcessPacketUnlocked(IncomingTelemetry packet, string lane)
    {
        var device = GetDeviceUnlocked(packet.NodeId);
        if (device is null) return;

        var severity = Classify(device, packet.Value);

        device.LastValue = packet.Value;
        device.LastSeen = packet.Timestamp;
        device.State = severity == AlertSeverity.Normal ? TileState.Normal : TileState.Anomalous;

        // The device just reported, so it is clearly not disconnected any more.
        _activeAlerts.Remove(AlertKey(device.NodeId, "DISCONNECTED"));

        UpdateReadingAlertsUnlocked(device, severity);

        _recentEvents.Insert(0, new ProcessedEvent(packet.NodeId, packet.Value, severity, lane, packet.Timestamp));
        if (_recentEvents.Count > MaxEvents)
            _recentEvents.RemoveRange(MaxEvents, _recentEvents.Count - MaxEvents);

        AddHistoryUnlocked(packet.NodeId, packet.Timestamp, packet.Value);
    }

    // ───────────────────────── History (SortedDictionary) ─────────────────────────

    private void AddHistoryUnlocked(string mac, DateTime timestamp, double value)
    {
        if (!_history.TryGetValue(mac, out var timeline))
        {
            timeline = new SortedDictionary<DateTime, double>();
            _history[mac] = timeline;
        }

        // Keys must be unique — if two readings share a timestamp, nudge by 1 tick.
        var key = timestamp;
        while (timeline.ContainsKey(key)) key = key.AddTicks(1);
        timeline[key] = value;

        // Keep memory bounded: the first key is always the oldest reading.
        while (timeline.Count > MaxHistoryPerDevice)
            timeline.Remove(timeline.Keys.First());
    }

    // Return ordered history points for a given device. 'last' is the
    // maximum number of most-recent points to return (default 60).
    public List<HistoryPoint> GetHistory(string mac, int last = 60)
    {
        lock (_gate)
        {
            if (!_history.TryGetValue(mac, out var timeline)) return new List<HistoryPoint>();

            // Already sorted by time — just skip to the last N entries.
            return timeline
                .Skip(Math.Max(0, timeline.Count - last))
                .Select(kv => new HistoryPoint(kv.Key, kv.Value))
                .ToList();
        }
    }

    // ───────────────────────── Alerts (HashSet) ─────────────────────────

    private static string AlertKey(string mac, string kind) => $"{mac}|{kind}";

    // Raises or clears LOW / SPIKE alerts depending on the latest reading.
    private void UpdateReadingAlertsUnlocked(DeviceInfo device, AlertSeverity severity)
    {
        string? kind = device.Category switch
        {
            SensorCategory.Environmental => "LOW",
            SensorCategory.Power => "SPIKE",
            _ => null
        };
        if (kind is null) return;

        var key = AlertKey(device.NodeId, kind);

        if (severity == AlertSeverity.Normal)
            _activeAlerts.Remove(key);
        else
            RaiseAlertUnlocked(key);
    }

    // HashSet.Add returns false if the alert already exists — that is the
    // duplicate being blocked, so we count it instead of processing it again.
    private bool RaiseAlertUnlocked(string key)
    {
        if (_activeAlerts.Add(key))
        {
            _engine.RecordTrigger(key, DateTime.UtcNow);
            return true;
        }

        _duplicatesBlocked++;
        return false;
    }

    // Any sensor that hasn't reported for 15 seconds is marked as disconnected.
    private void CheckDisconnectsUnlocked(DateTime now)
    {
        foreach (var device in _devices.Values)
        {
            if (device.Category == SensorCategory.Actuator) continue;
            if (now - device.LastSeen < DisconnectAfter) continue;

            var key = AlertKey(device.NodeId, "DISCONNECTED");
            if (_activeAlerts.Contains(key)) continue; // already known — nothing new to do

            _activeAlerts.Add(key);
            device.State = TileState.Dead;
            _engine.RecordTrigger(key, now);
        }
    }

    private bool HasAlertUnlocked(string mac) =>
        _activeAlerts.Any(a => a.StartsWith(mac + "|", StringComparison.Ordinal));

    // Demo helper: takes the device offline for 20s and fires the disconnect
    // alert several times in a row. The first one gets through; the rest are
    // rejected by the HashSet, proving duplicates are blocked.
    public bool SimulateDisconnect(string mac, int attempts, out int raised, out int blocked)
    {
        raised = 0;
        blocked = 0;

        lock (_gate)
        {
            var device = GetDeviceUnlocked(mac);
            if (device is null || device.Category == SensorCategory.Actuator) return false;

            _offlineUntil[mac] = DateTime.UtcNow.AddSeconds(20);
            device.State = TileState.Dead;

            for (var i = 0; i < attempts; i++)
            {
                if (RaiseAlertUnlocked(AlertKey(mac, "DISCONNECTED"))) raised++;
                else blocked++;
            }

            return true;
        }
    }

    // ───────────────────────── Simulator support ─────────────────────────

    // Tells the simulator to stop sending for this device for a while.
    public void ForceOffline(string mac, TimeSpan duration)
    {
        lock (_gate)
        {
            _offlineUntil[mac] = DateTime.UtcNow.Add(duration);
        }
    }

    public bool IsOffline(string mac)
    {
        lock (_gate)
        {
            return _offlineUntil.TryGetValue(mac, out var until) && DateTime.UtcNow < until;
        }
    }

    // ───────────────────────── Commands + Undo (Stack) ─────────────────────────

    public CommandResult IssueCommand(CommandRequest request)
    {
        var action = (request.Action ?? "").Trim().ToUpperInvariant();

        lock (_gate)
        {
            var previous = new Dictionary<string, bool>();
            string nodeId;
            string message;
            string learnedAction;

            if (action == "SHUTDOWN_ALL_VALVES")
            {
                // Bulk command — every actuator goes off.
                foreach (var d in _devices.Values.Where(d => d.Category == SensorCategory.Actuator))
                {
                    previous[d.NodeId] = d.IsOn;
                    d.IsOn = false;
                }

                nodeId = "ALL";
                message = "All pumps, relays and valves shut down.";
                learnedAction = "SHUTDOWN_ALL_VALVES";
            }
            else
            {
                var device = GetDeviceUnlocked(request.NodeId ?? "");
                if (device is null)
                    return new CommandResult(false, "Unknown device.", null);

                if (device.Category != SensorCategory.Actuator)
                    return new CommandResult(false, $"{device.Name} is a sensor — only actuators accept commands.", null);

                previous[device.NodeId] = device.IsOn;

                switch (action)
                {
                    case "ON": device.IsOn = true; break;
                    case "OFF": device.IsOn = false; break;
                    case "TOGGLE": device.IsOn = !device.IsOn; break;
                    default: return new CommandResult(false, $"Unknown action '{request.Action}'.", null);
                }

                nodeId = device.NodeId;
                learnedAction = device.IsOn ? "ON" : "OFF"; // learn the resulting state, not "TOGGLE"
                message = $"{device.Name} turned {learnedAction}.";
            }

            var record = new CommandRecord(Guid.NewGuid(), action, nodeId, previous, DateTime.UtcNow);
            _undoStack.Push(record);

            // Teach the engine: "while these alerts were active, the operator did this".
            _engine.RecordAction($"CMD|{nodeId}|{learnedAction}", _activeAlerts.ToList());

            return new CommandResult(true, message, record);
        }
    }

    // Pops the most recent command off the stack and restores every device it touched.
    public CommandResult Undo()
    {
        lock (_gate)
        {
            if (_undoStack.Count == 0)
                return new CommandResult(false, "Nothing to undo.", null);

            var record = _undoStack.Pop();

            foreach (var (mac, wasOn) in record.PreviousStates)
            {
                var device = GetDeviceUnlocked(mac);
                if (device is not null) device.IsOn = wasOn;
            }

            return new CommandResult(true, $"Undid '{record.Action}'.", record);
        }
    }

    // ───────────────────────── Search / filter ─────────────────────────

    public List<DeviceInfo> SearchDevices(string? query, SensorCategory? category, bool alertOnly)
    {
        lock (_gate)
        {
            IEnumerable<DeviceInfo> results = _devices.Values;

            if (category.HasValue)
                results = results.Where(d => d.Category == category.Value);

            if (alertOnly)
                results = results.Where(d => HasAlertUnlocked(d.NodeId));

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                results = results.Where(d =>
                    d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    d.NodeId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    d.Location.Contains(q, StringComparison.OrdinalIgnoreCase));

                // Only learn from meaningful searches, not every keystroke.
                if (q.Length >= 3)
                    _engine.RecordAction($"SEARCH|{q.ToLowerInvariant()}", _activeAlerts.ToList());
            }

            return results.Select(Clone).ToList();
        }
    }

    // ───────────────────────── Dashboard snapshot ─────────────────────────

    public StreamSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var devices = _devices.Values.Select(Clone).ToList();
            var alerts = _activeAlerts.OrderBy(a => a).ToList();

            return new StreamSnapshot(
                devices,
                _standardQueue.Count,
                _priorityQueue.Count,
                _recentEvents.Take(20).ToList(),
                alerts,
                _duplicatesBlocked,
                _undoStack.Count,
                _undoStack.Count > 0 ? _undoStack.Peek() : null,
                _engine.GetSuggestions(alerts, devices),
                SimulatorEnabled);
        }
    }
}
