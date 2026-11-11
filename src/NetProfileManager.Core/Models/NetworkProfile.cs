namespace NetProfileManager.Core.Models;

public class NetworkProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string SubnetMask { get; set; } = "255.255.255.0";
    public string? Gateway { get; set; }
    public List<string> DnsServers { get; set; } = new();
    public string? Description { get; set; }

    public string Summary => $"{IpAddress}/{SubnetMask} | GW {Gateway ?? "-"} | DNS {string.Join(", ", DnsServers)}";
}
