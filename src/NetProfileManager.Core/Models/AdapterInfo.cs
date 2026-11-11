namespace NetProfileManager.Core.Models;

public class AdapterInfo
{
    public string InterfaceAlias { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OperationalStatus { get; set; } = string.Empty;
    public List<string> CurrentAddresses { get; set; } = new();
    public string? CurrentGateway { get; set; }
    public List<string> CurrentDns { get; set; } = new();
    public bool DhcpEnabled { get; set; }
    public string? ActiveProfileId { get; set; }
    public bool AssignedDhcp { get; set; }
}
