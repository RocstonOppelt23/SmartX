using SmartX.Shared;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    // In-memory store for now — good enough to prove the endpoint works.
    // Swap for real persistence later if you get to it.
    private static readonly List<SensorRegistration> Registry = new();

    public static void MapSensorEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sensors/register", (SensorRegistration registration) =>
        {
            if (string.IsNullOrWhiteSpace(registration.MacAddress))
                return Results.BadRequest("MAC address is required.");

            if (string.IsNullOrWhiteSpace(registration.DeploymentLocation))
                return Results.BadRequest("Deployment location is required.");

            if (Registry.Any(r => r.MacAddress == registration.MacAddress))
                return Results.Conflict("A sensor with this MAC address is already registered.");

            Registry.Add(registration);
            return Results.Created($"/api/sensors/{registration.MacAddress}", registration);
        });

        app.MapGet("/api/sensors", () => Results.Ok(Registry));
    }
}

