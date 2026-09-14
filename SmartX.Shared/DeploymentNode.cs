namespace SmartX.Shared;

// Represents one level of the deployment hierarchy — Facility, Zone,
// Sub-Zone, or a leaf Node. Children make this a tree, which is what
// gives the validator below something to recurse over.
public class DeploymentNode
{
    public required string Name { get; init; }
    public string? MacAddress { get; init; }   // only set on leaf sensor nodes
    public List<DeploymentNode> Children { get; init; } = new();

    public bool IsLeaf => Children.Count == 0;
}