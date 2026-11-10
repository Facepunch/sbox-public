using Sandbox.Services;
using Sandbox.Twitch;

namespace Sandbox.Engine;

internal static partial class Streamer
{
	internal static IStreamService CurrentService;
	internal static Sandbox.Streamer.Broadcast CurrentBroadcast;

	internal static ServiceToken _serviceToken;
	internal static ServiceToken ServiceToken
	{
		get
		{
			if ( _serviceToken.Token is null )
				throw new System.Exception( "No service token" );

			return _serviceToken;
		}

		private set => _serviceToken = value;
	}

	/// <summary>
	/// Your own username, or null if we're not connected
	/// </summary>
	public static string Username => _serviceToken.Name;

	/// <summary>
	/// Your own user id, or null if we're not connected
	/// </summary>
	public static string UserId => _serviceToken.Id;

	internal static string Token => ServiceToken.Token;

	/// <summary>
	/// The service type (ie "Twitch")
	/// </summary>
	public static StreamService ServiceType { get; private set; } = StreamService.None;

	/// <summary>
	/// Are we connected to a service
	/// </summary>
	public static bool IsActive => CurrentService != null;

	static async Task<bool> Init( IStreamService service, ServiceToken token )
	{
		_serviceToken = token;
		CurrentService = service;

		ServiceType = CurrentService != null ? CurrentService.ServiceType : StreamService.None;

		bool success;
		try
		{
			success = await CurrentService.Connect();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, "Connection failed." );
			success = false;
		}

		if ( !success )
		{
			try
			{
				service?.Disconnect();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, "Error disconnecting failed service." );
			}

			_serviceToken = default;
			ServiceType = StreamService.None;
			CurrentService = null;
			Log.Info( "Connection failed." );
			return false;
		}

		Log.Info( "Connected" );
		return true;
	}

	internal static async Task<bool> Init( string serviceType )
	{
		if ( CurrentService != null )
		{
			Log.Warning( "Tried to start stream but already connected" );
			return false;
		}

		Log.Info( "Getting Service Token.." );
		IStreamService service = null;
		var token = await GetLinkedService( serviceType );
		if ( token.Token is null )
		{
			Log.Warning( $"Couldn't retrieve token for {serviceType} (open https://sbox.facepunch.com/link)" );
			return false;
		}

		Log.Info( $"Creating Service {serviceType}.." );

		switch ( serviceType.ToLowerInvariant() )
		{
			case "twitch":
				service = new TwitchService();
				break;
		}

		if ( service == null )
		{
			Log.Warning( $"Unsupported service type {serviceType}" );
			return false;
		}

		return await Init( service, token );
	}

	private static async Task<ServiceToken> GetLinkedService( string serviceType )
	{
		try
		{
			return await Sandbox.Backend.Account.GetService( serviceType );
		}
		catch ( System.Exception )
		{
			return default;
		}
	}

	internal static void Shutdown( string serviceName )
	{
		// serviceName ignored for now
		CurrentService?.Disconnect();
		CurrentService = null;
		_serviceToken = default;
		ServiceType = StreamService.None;
		ClearViewers();
	}
}
