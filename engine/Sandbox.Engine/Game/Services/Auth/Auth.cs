using System.Threading;
using Sandbox.SteamApi;

namespace Sandbox.Services;

public static class Auth
{
	// Native startup can deliver the prefetched ticket before account login begins.
	static TaskCompletionSource<string> webTicket = new( TaskCreationOptions.RunContinuationsAsynchronously );

	internal static async Task<string> GetWebTicketAsync()
	{
		var ticket = webTicket.Task;
		if ( !SteamNative.Auth.RequestWebTicket() ) return null;

		try
		{
			return await ticket.WaitAsync( TimeSpan.FromSeconds( 10 ) );
		}
		catch ( TimeoutException )
		{
			return null;
		}
	}

	internal static void OnWebTicketReceived( string ticket )
	{
		var completion = webTicket;
		if ( string.IsNullOrEmpty( ticket ) )
			webTicket = new( TaskCreationOptions.RunContinuationsAsynchronously );

		completion.TrySetResult( ticket );
	}

	internal static void CancelWebTicket()
	{
		SteamNative.Auth.CancelWebTicket();
		OnWebTicketReceived( null );
	}

	/// <summary>
	/// Get an auth token, which can be passed to the backend
	/// </summary>
	public static async Task<string> GetToken( string serviceName, CancellationToken token = default )
	{
		var package = Application.GameIdent;
		if ( package is null ) return default;
		if ( Backend.Account is null ) return default;

		try
		{
			var tt = await Backend.Account.GetAuthToken( Api.SessionId.ToString(), package, serviceName );
			return tt?.Trim( '"', '\'', ' ' );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error getting token for {serviceName}" );
			return default;
		}
	}

	/// <summary>
	/// Get a Steam authentication ticket intended for a target with a particular Steam Id.
	/// </summary>
	/// <param name="targetSteamId"></param>
	/// <param name="ticket"></param>
	/// <returns></returns>
	internal static uint GetAuthTicket( ulong targetSteamId, out byte[] ticket )
	{
		using var buffer = SteamNative.Auth.GetTicket( targetSteamId, out var handle );
		ticket = buffer.IsNull ? [] : buffer.ToArray();
		return handle;
	}

	/// <summary>
	/// End an authentication session with a particular user.
	/// </summary>
	/// <param name="steamId"></param>
	internal static void EndAuthSession( ulong steamId )
	{
		SteamNative.Auth.EndSession( steamId );
	}

	/// <summary>
	/// Begin an authentication session with a particular user, using the ticket they provided you.
	/// </summary>
	/// <param name="senderSteamId"></param>
	/// <param name="ticket"></param>
	internal static unsafe BeginAuthResult BeginAuthSession( ulong senderSteamId, byte[] ticket )
	{
		fixed ( byte* ptr = ticket )
		{
			return SteamNative.Auth.BeginSession( senderSteamId, (IntPtr)ptr, ticket.Length );
		}
	}

	/// <summary>
	/// Cancel an authentication ticket from a handle obtained by <see cref="GetAuthTicket"/>.
	/// </summary>
	/// <param name="handle"></param>
	internal static void CancelAuthTicket( uint handle )
	{
		SteamNative.Auth.CancelTicket( handle );
	}
}
