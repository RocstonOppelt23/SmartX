using SmartX.Api.Services;
using SmartX.Shared;

namespace SmartX.Api.Endpoints;

// All URLs for the Real-Time Command Stream module.
// MAC addresses contain ':' so they are passed as ?mac= query values, not in the path.
public static class CommandStreamEndpoints
{
    public static void MapCommandStreamEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/stream");

        // Devices (or the simulator) push readings here.
        group.MapPost("/telemetry", (IncomingTelemetry packet, CommandStreamService svc) =>
        {
            var lane = svc.Enqueue(packet);
            if (lane == "UNKNOWN_DEVICE")
                return Results.NotFound(new { message = $"Unknown device {packet.NodeId}" });

            return Results.Ok(new
            {
                lane,
                standardQueueDepth = svc.GetQueueDepth(),
                priorityQueueDepth = svc.GetPriorityQueueDepth()
            });
        });

        // Everything the dashboard needs in one call.
        group.MapGet("/snapshot", (CommandStreamService svc) => Results.Ok(svc.GetSnapshot()));

        // Search / filter: /api/stream/devices?query=pump&category=Actuator&alertOnly=true
        group.MapGet("/devices", (string? query, string? category, bool? alertOnly, CommandStreamService svc) =>
        {
            SensorCategory? parsed = null;
            if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<SensorCategory>(category, true, out var c))
                parsed = c;

            return Results.Ok(svc.SearchDevices(query, parsed, alertOnly ?? false));
        });

        // Timeline for one device: /api/stream/history?mac=AA:BB:CC:00:00:01&last=60
        group.MapGet("/history", (string mac, int? last, CommandStreamService svc) =>
            Results.Ok(svc.GetHistory(mac, last ?? 60)));

        // Manual override: { "nodeId": "AA:BB:CC:00:00:06", "action": "ON" }
        group.MapPost("/commands", (CommandRequest request, CommandStreamService svc) =>
        {
            var result = svc.IssueCommand(request);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // Undo the most recent command (Stack pop).
        group.MapPost("/commands/undo", (CommandStreamService svc) =>
        {
            var result = svc.Undo();
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        // Demo: inject a critical reading to prove it skips the standard queue.
        group.MapPost("/simulate/critical", (string mac, CommandStreamService svc) =>
        {
            var device = svc.GetDevice(mac);
            if (device is null) return Results.NotFound(new { message = "Unknown device" });

            double? value = device.Category switch
            {
                SensorCategory.Environmental => 12.0,   // soil far too dry
                SensorCategory.Power => 3500.0,         // severe power spike
                _ => (double?)null
            };

            if (value is null)
                return Results.BadRequest(new { message = "Actuators don't send readings." });

            var lane = svc.Enqueue(new IncomingTelemetry(mac, value.Value, DateTime.UtcNow));
            return Results.Ok(new { lane, value, priorityQueueDepth = svc.GetPriorityQueueDepth() });
        });

        // Demo: fire the same disconnect alert 3 times — HashSet lets only the first through.
        group.MapPost("/simulate/disconnect", (string mac, CommandStreamService svc) =>
        {
            if (!svc.SimulateDisconnect(mac, 3, out var raised, out var blocked))
                return Results.BadRequest(new { message = "Choose a sensor (not an actuator)." });

            return Results.Ok(new { raised, blocked });
        });

        // Pause / resume the device simulator.
        group.MapPost("/simulator/start", (CommandStreamService svc) =>
        {
            svc.SimulatorEnabled = true;
            return Results.Ok(new { enabled = true });
        });

        group.MapPost("/simulator/stop", (CommandStreamService svc) =>
        {
            svc.SimulatorEnabled = false;
            return Results.Ok(new { enabled = false });
        });
    }
}
