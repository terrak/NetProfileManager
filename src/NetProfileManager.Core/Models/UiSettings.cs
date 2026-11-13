namespace NetProfileManager.Core.Models;

public class UiSettings
{
    public HashSet<string> HiddenAdapters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
