using SmartX.Shared;

namespace SmartX.Api.Services;

public class CommandStreamService
{
    // Lock object — the API handles many requests at the same time.
    // Only one thread may touch the collections at once, or data gets corrupted.
    private readonly object _gate = new();

    // Key = MAC address, Value = the device. O(1) lookup when a packet arrives.
    private readonly Dictionary<string, DeviceInfo> _devices = new();

    // FIFO line for normal telemetry — first packet in is first processed.
    private readonly Queue<IncomingTelemetry> _standardQueue = new();

    // Emergency line — critical alerts skip the standard queue.
    // Lower number comes out first. Using timestamp ticks means the oldest critical alert goes first.
    private readonly PriorityQueue<IncomingTelemetry, long> _priorityQueue = new();

    public CommandStreamService()
    {
        SeedDevices();
    }

    private void SeedDevices()
    {
        AddDevice("AA:BB:CC:00:00:01", "Moisture Sensor A", SensorCategory.Environmental, "Zone 1 / Sub-Zone A");
        AddDevice("AA:BB:CC:00:00:02", "Moisture Sensor B", SensorCategory.Environmental, "Zone 1 / Sub-Zone B");
        AddDevice("AA:BB:CC:00:00:03", "Temp Sensor C", SensorCategory.Environmental, "Zone 2 / Sub-Zone A");
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

    // Public lookup — takes the lock itself.
    public DeviceInfo? GetDevice(string mac)
    {
        lock (_gate)
        {
            return GetDeviceUnlocked(mac);
        }
    }

    // Same lookup without the lock — only call this when you're ALREADY inside lock (_gate).
    private DeviceInfo? GetDeviceUnlocked(string mac)
    {
        return _devices.TryGetValue(mac, out var device) ? device : null;
    }

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
}