using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Resolves a stable identifier for this physical / VM server. Used by the
/// activation client so the licensing backend can detect attempts to reuse
/// the same serial on a different machine.
///
///   hostname  = Environment.MachineName + ',' + Dns.GetHostName fallback
///   mac_hash  = SHA-256 of every non-virtual NIC MAC, sorted, joined by '|'
///
/// MACs are hashed (not sent in clear) so the backend stores opaque
/// fingerprints rather than directly-usable hardware identifiers.
/// </summary>
public static class ServerIdentity
{
    public static ServerIdentityInfo Current { get; } = Resolve();

    private static ServerIdentityInfo Resolve()
    {
        var host = Environment.MachineName;
        try
        {
            var dnsHost = System.Net.Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(dnsHost)
                && !dnsHost.Equals(host, StringComparison.OrdinalIgnoreCase))
            {
                host = $"{host}|{dnsHost}";
            }
        }
        catch
        {
            // DNS lookup is best-effort.
        }

        var macs = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }
                // Skip common virtual adapters by description heuristic.
                var desc = nic.Description?.ToLowerInvariant() ?? string.Empty;
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v"))
                {
                    continue;
                }
                var mac = nic.GetPhysicalAddress().ToString();
                if (!string.IsNullOrWhiteSpace(mac))
                {
                    macs.Add(mac);
                }
            }
        }
        catch
        {
            // If NIC enumeration fails the hash falls back to host name only.
        }
        macs.Sort(StringComparer.Ordinal);

        var raw = macs.Count > 0 ? string.Join('|', macs) : $"no-mac:{host}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        return new ServerIdentityInfo(Hostname: host, MacHash: hash);
    }
}

public sealed record ServerIdentityInfo(string Hostname, string MacHash);
