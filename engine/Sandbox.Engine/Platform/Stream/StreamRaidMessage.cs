namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// Another channel raided yours, sending their viewers over. The raider is the incoming broadcaster
	/// - they aren't a chatter in your channel, so they're identified by name rather than a
	/// <see cref="Viewer"/>.
	/// </summary>
	public struct RaidMessage
	{
		/// <summary>
		/// The raiding channel's login name.
		/// </summary>
		public string RaiderUsername { get; internal set; }

		/// <summary>
		/// The raiding channel's display name.
		/// </summary>
		public string RaiderDisplayName { get; internal set; }

		/// <summary>
		/// How many viewers were brought over by the raid.
		/// </summary>
		public int ViewerCount { get; internal set; }

		/// <summary>
		/// When we received the event.
		/// </summary>
		public DateTimeOffset Time { get; internal set; }
	}
}
