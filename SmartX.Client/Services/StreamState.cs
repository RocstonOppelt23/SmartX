namespace SmartX.Client.Services;

// Holds the Command Stream page's UI state for the whole session.
// Registered as a singleton, so when the user navigates to another module
// and comes back, their search, filters and selected device are still there.
public class StreamState
{
    public string SearchText { get; set; } = "";
    public string Category { get; set; } = "";      // "" = all categories
    public bool AlertOnly { get; set; }
    public string? SelectedNodeId { get; set; }
}
