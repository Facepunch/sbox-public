using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Does this Uri resolve to a private range IP address?
	/// </summary>
	internal static bool IsPrivate( this Uri uri )
	{
		return Dns.GetHostEntry( uri.DnsSafeHost ).AddressList.Any( x => x.IsPrivate() );
	}

	/// <summary>
	/// Does this Uri resolve to a private range IP address?
	/// </summary>
	internal static async Task<bool> IsPrivateAsync( this Uri uri )
	{
		var entry = await Dns.GetHostEntryAsync( uri.DnsSafeHost );
		return entry.AddressList.Any( x => x.IsPrivate() );
	}

	/// <summary>
	/// Returns true if the IP address is in a private range.<br/>
	/// IPv4: Loopback, link local ("169.254.x.x"), class A ("10.x.x.x"), class B ("172.16.x.x" to "172.31.x.x") and class C ("192.168.x.x").<br/>
	/// IPv6: Loopback, link local, site local, unique local and private IPv4 mapped to IPv6.<br/>
	/// </summary>
	internal static bool IsPrivate( this IPAddress ip )
	{
		// Map back to IPv4 if mapped to IPv6, for example "::ffff:1.2.3.4" to "1.2.3.4".
		if ( ip.IsIPv4MappedToIPv6 )
			ip = ip.MapToIPv4();

		// Checks loopback ranges for both IPv4 and IPv6.
		if ( IPAddress.IsLoopback( ip ) ) return true;

		// IPv4
		if ( ip.AddressFamily == AddressFamily.InterNetwork )
		{
			var ipv4Bytes = ip.GetAddressBytes();

			// Link local (no IP assigned by DHCP): 169.254.0.0 to 169.254.255.255 (169.254.0.0/16)
			bool IsLinkLocal() => ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254;

			// Class A private range: 10.0.0.0 – 10.255.255.255 (10.0.0.0/8)
			bool IsClassA() => ipv4Bytes[0] == 10;

			// Class B private range: 172.16.0.0 – 172.31.255.255 (172.16.0.0/12)
			bool IsClassB() => ipv4Bytes[0] == 172 && ipv4Bytes[1] >= 16 && ipv4Bytes[1] <= 31;

			// Class C private range: 192.168.0.0 – 192.168.255.255 (192.168.0.0/16)
			bool IsClassC() => ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168;

			return IsLinkLocal() || IsClassA() || IsClassC() || IsClassB();
		}

		// IPv6
		if ( ip.AddressFamily == AddressFamily.InterNetworkV6 )
		{
			return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
		}

		throw new NotSupportedException( $"IP address family {ip.AddressFamily} is not supported, expected only IPv4 (InterNetwork) or IPv6 (InterNetworkV6)" );
	}
}
