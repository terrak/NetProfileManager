namespace NetProfileManager.Core.Models;

public class AppState
{
    public Dictionary<string, string> Assignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
