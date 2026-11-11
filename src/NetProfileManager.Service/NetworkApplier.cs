using System.Diagnostics;
using NetProfileManager.Core.Models;

namespace NetProfileManager.Service;

public static class NetworkApplier
{
    public static void ApplyStatic(string interfaceAlias, NetworkProfile profile)
    {
        var addressArgs = string.IsNullOrWhiteSpace(profile.Gateway)
            ? $"interface ipv4 set address name=\"{interfaceAlias}\" static {profile.IpAddress} {profile.SubnetMask}"
            : $"interface ipv4 set address name=\"{interfaceAlias}\" static {profile.IpAddress} {profile.SubnetMask} {profile.Gateway} 1";
        RunNetsh(addressArgs);

        if (profile.DnsServers.Count > 0)
        {
            RunNetsh($"interface ipv4 set dnsservers name=\"{interfaceAlias}\" static {profile.DnsServers[0]} primary");
            for (var i = 1; i < profile.DnsServers.Count; i++)
            {
                RunNetsh($"interface ipv4 add dnsservers name=\"{interfaceAlias}\" {profile.DnsServers[i]} index={i + 1}");
            }
        }
    }

    public static void ApplyDhcp(string interfaceAlias)
    {
        RunNetsh($"interface ipv4 set address name=\"{interfaceAlias}\" dhcp");
        RunNetsh($"interface ipv4 set dnsservers name=\"{interfaceAlias}\" dhcp");
    }

    private static void RunNetsh(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Impossible de lancer netsh.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh a échoué (code {process.ExitCode}) : {arguments}. {stderr} {stdout}");
        }
    }
}
