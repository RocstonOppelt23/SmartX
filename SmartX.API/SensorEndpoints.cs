using SmartX.Shared;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    private sealed class SensorRegistry
    {
        private readonly List<SensorRegistration> _items = new();

        public void Add(SensorRegistration registration) => _items.Add(registration);

        public bool Exists(string macAddress) => _items.Any(s => s.MacAddress == macAddress);

        public IReadOnlyList<SensorRegistration> GetAll() => _items.AsReadOnly();
    }

    private static readonly SensorRegistry Registry = new();

    private static readonly DeploymentNode FacilityTree = new()
    {
        Name = "Facility A",
        Children = new List<DeploymentNode>
        {
            new()
            {
                Name = "Zone 1",
                Children = new List<DeploymentNode>
                {
                    new()
                    {
                        Name = "Zone 1 / Sub-Zone A",
                        Children = new List<DeploymentNode>
                        {
                            new() { Name = "Placeholder Sensor A1", MacAddress = "00:00:00:00:00:01" }
                        }
                    },
                    new()
                    {
                        Name = "Zone 1 / Sub-Zone B",
                        Children = new List<DeploymentNode>
                        {
                            new() { Name = "Placeholder Sensor B1", MacAddress = "00:00:00:00:00:02" }
                        }
                    },
                }
            },
            new()
            {
                Name = "Zone 2",
                Children = new List<DeploymentNode>
                {
                    new()
                    {
                        Name = "Zone 2 / Sub-Zone A",
                        Children = new List<DeploymentNode>
                        {
                            new() { Name = "Placeholder Sensor C1", MacAddress = "00:00:00:00:00:03" }
                        }
                    },
                }
            }
        }
    };

    private static bool IsKnownLocation(string location)
    {
        return SearchTree(FacilityTree, location);
    }

    private static bool SearchTree(DeploymentNode node, string location)
    {
        if (node.Name == location) return true;
        return node.Children.Any(child => SearchTree(child, location));
    }

    public static void MapSensorEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sensors/register", (SensorRegistration registration) =>
        {
            if (string.IsNullOrWhiteSpace(registration.MacAddress))
                return Results.BadRequest("MAC address is required.");

            if (string.IsNullOrWhiteSpace(registration.DeploymentLocation))
                return Results.BadRequest("Deployment location is required.");

            if (!IsKnownLocation(registration.DeploymentLocation))
                return Results.BadRequest(
                    $"'{registration.DeploymentLocation}' is not a recognised deployment zone.");

            if (Registry.Exists(registration.MacAddress))
                return Results.Conflict("A sensor with this MAC address is already registered.");

            Registry.Add(registration);
            return Results.Created($"/api/sensors/{registration.MacAddress}", registration);
        });

        app.MapGet("/api/sensors", () => Results.Ok(Registry.GetAll()));

        app.MapGet("/api/facility-tree/validate", () =>
        {
            bool isValid = DeploymentValidator.Validate(FacilityTree, out var errors);
            return Results.Ok(new { isValid, errors });
        });
    }
}
