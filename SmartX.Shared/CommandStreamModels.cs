using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared
{
    public enum AlertSeverity  //Displays the reading
    {
        Normal,
        Warning,
        Critical
    }

    public class DeviceInfo //Model class
    {
        public required string NodeId { get; init; }
        public required string Name { get; init; }
        public required SensorCategory Category { get; init; }
        public required string Location { get; init; }
        public double LastValue { get; set; }
        public DateTime LastSeen { get; set; }
        public TileState State { get; set; }        //This is a custom type
        public bool IsOn { get; set; }
    }

    // One packet sent by a device (ESP32, smart meter, etc.) to the API.
    public record IncomingTelemetry(string NodeId, double Value, DateTime Timestamp);

    // ProcessedEvent represents a single telemetry reading after it has
    // been processed by the server and inserted into the recent events timeline.
    public record ProcessedEvent(string NodeId, double Value, AlertSeverity Severity, string Lane, DateTime Timestamp);

    // One point on a device's timeline graph.
    public record HistoryPoint(DateTime Timestamp, double Value);

    // What the dashboard sends when an operator presses an override button.
    // Action: "ON", "OFF", "TOGGLE" or "SHUTDOWN_ALL_VALVES".
    public record CommandRequest(string NodeId, string Action);

    // A command that was carried out. PreviousStates remembers what every affected
    // device looked like BEFORE the command, so Undo can put them back exactly.
    public record CommandRecord(Guid Id, string Action, string NodeId, Dictionary<string, bool> PreviousStates, DateTime IssuedAt);

    // Result of issuing or undoing a command.
    public record CommandResult(bool Success, string Message, CommandRecord? Command);

    // A predictive recommendation shown in the "Suggested Actions" panel.
    // NodeId + Action = a command the user can run with one click.
    // SearchText = a search the user usually runs in this situation.
    public record Suggestion(string Title, string Reason, string? NodeId, string? Action, string? SearchText, int Confidence);

    // Everything the Command Stream dashboard needs in one call.
    public record StreamSnapshot(
        List<DeviceInfo> Devices,
        int StandardQueueDepth,
        int PriorityQueueDepth,
        List<ProcessedEvent> RecentEvents,
        List<string> ActiveAlerts,
        int DuplicateAlertsBlocked,
        int UndoDepth,
        CommandRecord? LastCommand,
        List<Suggestion> Suggestions,
        bool SimulatorEnabled);
}
