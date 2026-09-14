namespace SmartX.Shared;

public record SensorRegistration
{
    public required string MacAddress { get; init; }
    public required string DeploymentLocation { get; init; }
    public required SensorCategory Category { get; init; }
}