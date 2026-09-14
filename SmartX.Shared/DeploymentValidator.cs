namespace SmartX.Shared;

// Recursive validator: walks a nested deployment tree (Facility -> Zone
// -> Sub-Zone -> Node) checking every node is properly placed. Base case
// is a leaf node, which stops the recursion instead of stack overflowing.
public static class DeploymentValidator
{
    public static bool Validate(DeploymentNode node, out List<string> errors)
    {
        errors = new List<string>();
        ValidateRecursive(node, errors);
        return errors.Count == 0;
    }

    private static void ValidateRecursive(DeploymentNode node, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
        {
            errors.Add("A deployment node has no name.");
        }

        // Base case — a leaf node must carry a MAC address, since it's
        // an actual sensor. No children means no further recursion.
        if (node.IsLeaf)
        {
            if (string.IsNullOrWhiteSpace(node.MacAddress))
            {
                errors.Add($"Leaf node '{node.Name}' has no MAC address.");
            }
            return;
        }

        // A non-leaf node (Facility, Zone, Sub-Zone) must not itself
        // carry a MAC address — only sensors do.
        if (!string.IsNullOrWhiteSpace(node.MacAddress))
        {
            errors.Add($"Non-leaf node '{node.Name}' should not have a MAC address.");
        }

        // Recursive case — walk every child the same way.
        foreach (DeploymentNode child in node.Children)
        {
            ValidateRecursive(child, errors);
        }
    }
}