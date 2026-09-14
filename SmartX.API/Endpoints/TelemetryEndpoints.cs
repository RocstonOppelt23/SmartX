using SmartX.Shared;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
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
            var now = DateTime.UtcNow;

            foreach (var (nodeId, lastSeen) in LastSeen)
            {
                var gap = now - lastSeen;

                if (States.TryGetValue(nodeId, out var current) && current == TileState.Anomalous)
                    continue;

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

    public static void SeedMockData()
    {
        var now = DateTime.UtcNow;

        // Example jagged array of historical raw readings per node
        double[][] historicalBatches = new double[][]
        {
            new double[] { 23.5, 24.0, 23.8 },
            new double[] { 21.5, 22.0 },
            new double[] { 24.5, 25.0, 25.2, 24.8 },
        };

        // Use BatchProcessor.Flatten to convert into sensor readings and
        // demonstrate that the utility is actually executed on startup.
        var flattened = BatchProcessor.Flatten(historicalBatches);
        Console.WriteLine($"Flattened historical readings count: {flattened.Count}");

        Baselines["NODE-A1"] = new SensorReading { Value = 24 };
        LastSeen["NODE-A1"] = now;
        States["NODE-A1"] = TileState.Normal;

        Baselines["NODE-A2"] = new SensorReading { Value = 22 };
        LastSeen["NODE-A2"] = now;
        States["NODE-A2"] = TileState.Normal;

        Baselines["NODE-B1"] = new SensorReading { Value = 25 };
        LastSeen["NODE-B1"] = now;
        States["NODE-B1"] = TileState.Normal;

        Baselines["NODE-B2"] = new SensorReading { Value = 24 };
        LastSeen["NODE-B2"] = now;
        States["NODE-B2"] = TileState.Anomalous;

        Baselines["NODE-C1"] = new SensorReading { Value = 23 };
        LastSeen["NODE-C1"] = now - StaleAfter - TimeSpan.FromSeconds(5);
        States["NODE-C1"] = TileState.Normal;

        Baselines["NODE-C2"] = new SensorReading { Value = 21 };
        LastSeen["NODE-C2"] = now - DeadAfter - TimeSpan.FromSeconds(10);
        States["NODE-C2"] = TileState.Normal;
    }
}
