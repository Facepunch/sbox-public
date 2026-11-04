using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Every IP address this Uri's host resolves to. IP literals resolve to themselves without hitting DNS.
	/// </summary>
	internal static IPAddress[] ResolveAddresses( this Uri uri )
	{
		if ( TryGetLiteralAddress( uri, out var literal ) ) return literal;

		return Dns.GetHostAddresses( uri.DnsSafeHost );
	}

	/// <inheritdoc cref="ResolveAddresses(Uri)"/>
	internal static Task<IPAddress[]> ResolveAddressesAsync( this Uri uri )
	{
		if ( TryGetLiteralAddress( uri, out var literal ) ) return Task.FromResult( literal );

		return Dns.GetHostAddressesAsync( uri.DnsSafeHost );
	}

	// Dns rejects "0.0.0.0" and "::" with ArgumentException, so literals never go through it.
	private static bool TryGetLiteralAddress( Uri uri, out IPAddress[] addresses )
	{
		if ( IPAddress.TryParse( uri.DnsSafeHost, out var address ) )
		{
			addresses = [address];
			return true;
		}

		addresses = null;
		return false;
	}

	/// <summary>
	/// Does this Uri resolve to a private range IP address?
	/// </summary>
	internal static bool IsPrivate( this Uri uri )
	{
		return uri.ResolveAddresses().Any( x => x.IsPrivate() );
	}

	/// <summary>
	/// Does this Uri resolve to a private range IP address?
	/// </summary>
	internal static async Task<bool> IsPrivateAsync( this Uri uri )
	{
		var addresses = await uri.ResolveAddressesAsync();
		return addresses.Any( x => x.IsPrivate() );
	}

	/// <summary>
	/// Returns true if the IP address is in a private or otherwise non-public range.<br/>
	/// IPv4: Loopback, this network ("0.x.x.x"), link local ("169.254.x.x"), class A ("10.x.x.x"), class B ("172.16.x.x" to "172.31.x.x"), class C ("192.168.x.x") and carrier grade NAT ("100.64.x.x" to "100.127.x.x").<br/>
	/// IPv6: Loopback, unspecified ("::"), link local, site local, unique local and private IPv4 mapped to IPv6.<br/>
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

			// This network, which includes the unspecified address: 0.0.0.0 - 0.255.255.255 (0.0.0.0/8)
			// Connecting to 0.0.0.0 reaches loopback on Linux, so it must never count as public.
			bool IsThisNetwork() => ipv4Bytes[0] == 0;

			// Link local (no IP assigned by DHCP): 169.254.0.0 to 169.254.255.255 (169.254.0.0/16)
			bool IsLinkLocal() => ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254;

			// Carrier grade NAT: 100.64.0.0 - 100.127.255.255 (100.64.0.0/10)
			bool IsCarrierGradeNat() => ipv4Bytes[0] == 100 && ipv4Bytes[1] >= 64 && ipv4Bytes[1] <= 127;

			// Class A private range: 10.0.0.0 – 10.255.255.255 (10.0.0.0/8)
			bool IsClassA() => ipv4Bytes[0] == 10;

			// Class B private range: 172.16.0.0 – 172.31.255.255 (172.16.0.0/12)
			bool IsClassB() => ipv4Bytes[0] == 172 && ipv4Bytes[1] >= 16 && ipv4Bytes[1] <= 31;

			// Class C private range: 192.168.0.0 – 192.168.255.255 (192.168.0.0/16)
			bool IsClassC() => ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168;

			return IsThisNetwork() || IsLinkLocal() || IsClassA() || IsClassC() || IsClassB() || IsCarrierGradeNat();
		}

		// IPv6
		if ( ip.AddressFamily == AddressFamily.InterNetworkV6 )
		{
			// "::" reaches ::1 on Linux, same as 0.0.0.0 above.
			if ( ip.Equals( IPAddress.IPv6Any ) ) return true;

			return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
		}

		throw new NotSupportedException( $"IP address family {ip.AddressFamily} is not supported, expected only IPv4 (InterNetwork) or IPv6 (InterNetworkV6)" );
	}
}
