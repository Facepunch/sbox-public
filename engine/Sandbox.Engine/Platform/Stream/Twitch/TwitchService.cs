namespace Sandbox.Twitch;

internal class TwitchService : IStreamService
{
	private readonly TwitchClient _client = new();
	private readonly TwitchAPI _api = new();

	public StreamService ServiceType => StreamService.Twitch;

	public async Task<Streamer.User> GetUser( string username )
	{
		var user = await _api.GetUser( username );
		if ( user is null )
			return default;

		return new Streamer.User
		{
			Id = user.Id,
			Login = user.Login,
			DisplayName = user.DisplayName,
			UserType = user.UserType,
			BroadcasterType = user.BroadcasterType,
			Description = user.Description,
			ProfileImageUrl = user.ProfileImageUrl,
			OfflineImageUrl = user.OfflineImageUrl,
			ViewCount = user.ViewCount,
			Email = user.Email,
			CreatedAt = user.CreatedAt,
		};
	}

	public async Task<bool> Connect()
	{
		if ( !await _client.Connect() )
		{
			return false;
		}

		timeUntilUpdateBroadcast = 30;
		UpdateBroadcast();
		return true;
	}

	public void Disconnect()
	{
		_client.Disconnect();
	}

	public void SetChannelDelay( int delay )
	{
		_api.SetChannelDelay( Engine.Streamer.UserId, delay );
	}

	public void SetChannelLanguage( string language )
	{
		_api.SetChannelLanguage( Engine.Streamer.UserId, language );
	}

	RealTimeUntil timeUntilUpdateBroadcast = 30;
	RealTimeUntil timeUntilUpdateChatters = 0;

	void IStreamService.Tick()
	{
		if ( timeUntilUpdateBroadcast <= 0 )
		{
			timeUntilUpdateBroadcast = 30; // Update again in 30 seconds
			UpdateBroadcast();
		}

		if ( timeUntilUpdateChatters <= 0 )
		{
			timeUntilUpdateChatters = 30; // Poll the chatter list every 30 seconds
			UpdateChatters();
		}
	}

	/// <summary>
	/// Poll Get Chatters for the full presence list and reconcile it into the roster. This is our
	/// reliable source of joins and (especially) leaves, since IRC PART events can't be counted on. A
	/// failed poll returns null and is skipped, so a transient error never wipes the roster.
	/// </summary>
	async void UpdateChatters()
	{
		try
		{
			var present = await _api.GetChatters( _client.UserId );
			if ( present is null )
				return;

			Engine.Streamer.ReconcileViewers( present );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, e.Message );
		}
	}

	async void UpdateBroadcast()
	{
		try
		{
			var viewers = Engine.Streamer.CurrentBroadcast.ViewerCount;

			var broadcast = await _api.GetStream( _client.UserId );

			if ( broadcast != null )
			{
				Engine.Streamer.CurrentBroadcast = new Streamer.Broadcast( broadcast );

				// Things are changing, update more often
				if ( viewers != Engine.Streamer.CurrentBroadcast.ViewerCount )
				{
					timeUntilUpdateBroadcast = 10;
				}

			}
			else
			{
				Engine.Streamer.CurrentBroadcast = default;
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, e.Message );
		}
	}
}
