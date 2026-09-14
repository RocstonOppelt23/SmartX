using SmartX.Shared;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    // Baseline reading per node — in a real system this might be
    // configured per sensor type; here it's a simple in-memory default.
    private static readonly Dictionary<string, SensorReading> Baselines = new();
    private static readonly Dictionary<string, DateTime> LastSeen = new();
    private static readonly Dictionary<string, TileState> States = new();

    private const double AnomalyThresholdValue = 5.0;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DeadAfter = TimeSpan.FromSeconds(90);

    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/telemetry/{nodeId}", (string nodeId, double value) =>
        {
            var incoming = new SensorReading { Value = value };
            LastSeen[nodeId] = DateTime.UtcNow;

            if (!Baselines.TryGetValue(nodeId, out var baseline))
            {
                // First reading from this node becomes its baseline.
                Baselines[nodeId] = incoming;
                States[nodeId] = TileState.Normal;
                return Results.Ok(new { nodeId, state = TileState.Normal });
            }

            bool anomalous = AnomalyThreshold.IsAnomalous(incoming, baseline, AnomalyThresholdValue);
            States[nodeId] = anomalous ? TileState.Anomalous : TileState.Normal;

            return Results.Ok(new { nodeId, state = States[nodeId] });
        });

        app.MapGet("/api/telemetry/states", () =>
        {
            // Re-evaluate staleness/dead state for every known node
            // based on how long it's been since we last heard from it.
            var now = DateTime.UtcNow;

            foreach (var (nodeId, lastSeen) in LastSeen)
            {
                var gap = now - lastSeen;

                if (States.TryGetValue(nodeId, out var current) && current == TileState.Anomalous)
                    continue; // an anomalous reading takes priority over staleness

                States[nodeId] = gap switch
                {
                    _ when gap >= DeadAfter => TileState.Dead,
                    _ when gap >= StaleAfter => TileState.Stale,
                    _ => TileState.Normal
                };
            }

            return Results.Ok(States);
        });
    }
}