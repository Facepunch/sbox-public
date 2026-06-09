namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// Your own username
	/// </summary>
	public static string Username => Engine.Streamer.Username;

	/// <summary>
	/// Your own user id
	/// </summary>
	public static string UserId => Engine.Streamer.UserId;

	/// <summary>
	/// The service type (ie "Twitch")
	/// </summary>
	public static StreamService Service => Engine.Streamer.ServiceType;

	/// <summary>
	/// Are we connected to a service
	/// </summary>
	public static bool IsActive => Engine.Streamer.IsActive;

	/// <summary>
	/// Everyone we currently think is in chat, tracked from join, leave and message events.
	/// This is a best-effort list of active chatters, not silent viewers.
	/// </summary>
	public static IReadOnlyList<Viewer> Viewers => Engine.Streamer.Viewers;

	/// <summary>
	/// Get user information. If no username is specified, the user returned is ourself
	/// </summary>
	public static Task<User> GetUser( string username = null ) => Engine.Streamer.CurrentService?.GetUser( username ) ?? Task.FromResult<User>( default );

	/// <summary>
	/// The game/category the stream is currently set to. Read-only.
	/// </summary>
	public static string Game => Engine.Streamer.CurrentBroadcast.GameName;

	/// <summary>
	/// Set the language of your stream
	/// </summary>
	public static string Language
	{
		get => Engine.Streamer.CurrentBroadcast.Language;
		set => Engine.Streamer.CurrentService?.SetChannelLanguage( value );
	}

	/// <summary>
	/// The title of the stream. Read-only.
	/// </summary>
	public static string Title => Engine.Streamer.CurrentBroadcast.Title;

	/// <summary>
	/// Set the delay of your stream
	/// </summary>
	public static int Delay
	{
		set => Engine.Streamer.CurrentService?.SetChannelDelay( value );
	}

	/// <summary>
	/// Amount of concurrent viewer your stream has.
	/// </summary>
	public static int ViewerCount => Engine.Streamer.CurrentBroadcast.ViewerCount;

	/// <summary>
	/// When the current stream started. Default if you're not live.
	/// </summary>
	public static DateTimeOffset StartedAt => Engine.Streamer.CurrentBroadcast.StartedAt;

	/// <summary>
	/// Thumbnail URL of the current stream.
	/// </summary>
	public static string GetThumbnailUrl( int width, int height ) => Engine.Streamer.CurrentBroadcast.ThumbnailUrl?.Replace( "{width}", $"{width}" ).Replace( "{height}", $"{height}" );

	/// <summary>
	/// Tags on the current stream.
	/// </summary>
	public static string[] Tags => Engine.Streamer.CurrentBroadcast.TagIds ?? [];

	/// <summary>
	/// Whether the current stream is flagged as mature.
	/// </summary>
	public static bool IsMature => Engine.Streamer.CurrentBroadcast.IsMature;

	/// <summary>
	/// Implement this on a <c>Component</c> to receive stream chat events. They're dispatched to the
	/// active scene on the main thread, so it's safe to touch GameObjects from these.
	/// </summary>
	public interface IEvents
	{
		/// <summary>
		/// A viewer joined chat.
		/// </summary>
		void OnStreamJoin( Viewer viewer ) { }

		/// <summary>
		/// A viewer left chat. Note that Twitch sends leaves unreliably, so don't count on this firing.
		/// </summary>
		void OnStreamLeave( Viewer viewer ) { }

		/// <summary>
		/// A chat message was received. Cheers arrive here too - check <see cref="ChatMessage.Bits"/>.
		/// </summary>
		void OnStreamMessage( ChatMessage message ) { }

		/// <summary>
		/// A viewer subscribed or resubscribed to the channel.
		/// </summary>
		void OnStreamSubscribe( SubscribeMessage message ) { }

		/// <summary>
		/// A viewer gifted a subscription to a specific recipient.
		/// </summary>
		void OnStreamGiftSubscribe( GiftSubscribeMessage message ) { }

		/// <summary>
		/// A viewer gifted a batch of subscriptions to the community (a mystery/community gift).
		/// </summary>
		void OnStreamGiftSubscriptions( GiftSubscriptionsMessage message ) { }

		/// <summary>
		/// Another channel raided yours, bringing their viewers with them.
		/// </summary>
		void OnStreamRaid( RaidMessage message ) { }
	}
}
