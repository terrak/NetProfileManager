using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetProfileManager.Core.Models;

namespace NetProfileManager.Service;

public static class AdapterEnumerator
{
    public static List<AdapterInfo> GetAdapters()
    {
        var adapters = new List<AdapterInfo>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = ni.GetIPProperties();
            adapters.Add(new AdapterInfo
            {
                InterfaceAlias = ni.Name,
                Description = ni.Description,
                OperationalStatus = ni.OperationalStatus.ToString(),
                CurrentAddresses = properties.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => u.Address.ToString())
                    .ToList(),
                CurrentGateway = properties.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(g => g.Address.ToString())
                    .FirstOrDefault(),
                CurrentDns = properties.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToList(),
                DhcpEnabled = properties.GetIPv4Properties()?.IsDhcpEnabled ?? false
            });
        }

        return adapters.OrderBy(a => a.InterfaceAlias, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
