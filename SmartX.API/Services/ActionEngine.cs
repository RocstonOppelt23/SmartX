using SmartX.Shared;

namespace SmartX.Api.Services;

// Predictive Action Engine.
// Learns which actions operators take after specific alerts, then suggests
// those actions automatically the next time the same alert appears.
//
// Example: every time "Moisture Sensor B is LOW" fires, the operator turns
// Pump A ON. After a few times, the engine suggests "Turn ON Pump A" the
// moment Moisture Sensor B goes low — before the operator searches for it.
public class ActionEngine
{
    private readonly object _gate = new();

    // An action only counts as a response to an alert if it happens within this window.
    private static readonly TimeSpan PatternWindow = TimeSpan.FromMinutes(2);

    // When each alert was last raised. Key = alert ("mac|LOW"), Value = time.
    private readonly Dictionary<string, DateTime> _recentTriggers = new();

    // The learned patterns: alert → (action → how many times it followed that alert).
    // Dictionary inside a Dictionary gives O(1) lookup of an alert's history.
    private readonly Dictionary<string, Dictionary<string, int>> _patterns = new();

    // How often each action has been used overall.
    private readonly Dictionary<string, int> _actionFrequency = new();

    public ActionEngine()
    {
        SeedHistory();
    }

    // Represents behaviour learned from previous operator sessions, so the
    // dashboard shows meaningful suggestions on first launch.
    private void SeedHistory()
    {
        // Moisture Sensor B low → operators turn on Pump A (same sub-zone) and search for it.
        Learn("AA:BB:CC:00:00:02|LOW", "CMD|AA:BB:CC:00:00:06|ON", 3);
        Learn("AA:BB:CC:00:00:02|LOW", "SEARCH|pump a", 2);

        // Moisture Sensor A low → Main Valve opened.
        Learn("AA:BB:CC:00:00:01|LOW", "CMD|AA:BB:CC:00:00:09|ON", 2);

        // Smart Meter 1 spike → Relay 3 switched off to shed load.
        Learn("AA:BB:CC:00:00:04|SPIKE", "CMD|AA:BB:CC:00:00:08|OFF", 2);

        // Smart Meter 2 spike → emergency shutdown.
        Learn("AA:BB:CC:00:00:05|SPIKE", "CMD|ALL|SHUTDOWN_ALL_VALVES", 1);
    }

    private void Learn(string trigger, string action, int times)
    {
        if (!_patterns.TryGetValue(trigger, out var actions))
        {
            actions = new Dictionary<string, int>();
            _patterns[trigger] = actions;
        }

        actions[action] = actions.GetValueOrDefault(action) + times;
    }

    // Called when an alert is raised for the first time.
    public void RecordTrigger(string alertKey, DateTime when)
    {
        lock (_gate)
        {
            _recentTriggers[alertKey] = when;
        }
    }

    // Called when the operator issues a command or searches.
    // Links the action to every alert that is active now or was raised in the last 2 minutes.
    public void RecordAction(string actionKey, IEnumerable<string> activeAlerts)
    {
        lock (_gate)
        {
            _actionFrequency[actionKey] = _actionFrequency.GetValueOrDefault(actionKey) + 1;

            var now = DateTime.UtcNow;
            var triggers = new HashSet<string>(activeAlerts);

            foreach (var (trigger, raisedAt) in _recentTriggers)
            {
                if (now - raisedAt <= PatternWindow) triggers.Add(trigger);
            }

            foreach (var trigger in triggers)
            {
                Learn(trigger, actionKey, 1);
            }
        }
    }

    // Builds the "Suggested Actions" list for the dashboard.
    public List<Suggestion> GetSuggestions(IReadOnlyCollection<string> activeAlerts, IReadOnlyList<DeviceInfo> devices)
    {
        lock (_gate)
        {
            var byId = devices.ToDictionary(d => d.NodeId);
            var results = new List<Suggestion>();
            var alreadySuggested = new HashSet<string>(); // never show the same suggestion twice

            foreach (var alert in activeAlerts)
            {
                var split = alert.LastIndexOf('|');
                if (split < 0) continue;

                var mac = alert[..split];
                var kind = alert[(split + 1)..];
                var name = byId.TryGetValue(mac, out var dev) ? dev.Name : mac;
                var situation = Describe(kind, name);

                // Insight: a disconnected sensor always deserves attention.
                if (kind == "DISCONNECTED" && alreadySuggested.Add($"CHECK|{mac}"))
                {
                    results.Add(new Suggestion(
                        $"Check {name}",
                        $"{name} has stopped sending data — possible power or Wi-Fi fault.",
                        mac, null, null, 90));
                }

                if (!_patterns.TryGetValue(alert, out var actions) || actions.Count == 0) continue;

                var total = actions.Values.Sum();

                // Max-heap: PriorityQueue returns the LOWEST priority first,
                // so using -count makes the most frequent action come out first.
                var heap = new PriorityQueue<string, int>();
                foreach (var (action, count) in actions) heap.Enqueue(action, -count);

                var taken = 0;
                while (heap.Count > 0 && taken < 3)
                {
                    var actionKey = heap.Dequeue();
                    if (!alreadySuggested.Add(actionKey)) continue;

                    var suggestion = Build(actionKey, actions[actionKey], total, situation, byId);
                    if (suggestion is null) continue;

                    results.Add(suggestion);
                    taken++;
                }
            }

            // Insight: devices reading out of range that have no learned pattern yet.
            foreach (var d in devices.Where(d => d.State == TileState.Anomalous))
            {
                if (alreadySuggested.Add($"INSPECT|{d.NodeId}"))
                {
                    results.Add(new Suggestion(
                        $"Inspect {d.Name}",
                        $"Latest reading {d.LastValue:0.#} is outside the safe range.",
                        d.NodeId, null, null, 50));
                }
            }

            return results
                .OrderByDescending(s => s.Confidence)
                .Take(6)
                .ToList();
        }
    }

    private static string Describe(string kind, string name) => kind switch
    {
        "LOW" => $"{name} is reading low",
        "SPIKE" => $"{name} has a power spike",
        "DISCONNECTED" => $"{name} went offline",
        _ => $"{name} raised an alert"
    };

    // Turns a stored action key ("CMD|mac|ON" or "SEARCH|pump a") into a readable suggestion.
    private static Suggestion? Build(string actionKey, int count, int total, string situation, Dictionary<string, DeviceInfo> byId)
    {
        var parts = actionKey.Split('|');
        var confidence = total == 0 ? 0 : count * 100 / total;
        var reason = $"{situation} — operators did this {count}× after this alert.";

        if (parts[0] == "CMD" && parts.Length == 3)
        {
            var mac = parts[1];
            var action = parts[2];
            var target = mac == "ALL" ? "all actuators"
                       : byId.TryGetValue(mac, out var d) ? d.Name : mac;

            var title = action switch
            {
                "ON" => $"Turn ON {target}",
                "OFF" => $"Turn OFF {target}",
                "SHUTDOWN_ALL_VALVES" => "Shut down all valves",
                _ => $"{action} {target}"
            };

            return new Suggestion(title, reason, mac, action, null, confidence);
        }

        if (parts[0] == "SEARCH" && parts.Length == 2)
        {
            return new Suggestion($"Look up \"{parts[1]}\"", reason, null, null, parts[1], confidence);
        }

        return null;
    }
}
