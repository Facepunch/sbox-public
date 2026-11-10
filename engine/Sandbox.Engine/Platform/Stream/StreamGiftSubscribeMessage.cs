namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// A viewer gifted a subscription to one specific recipient.
	/// </summary>
	public struct GiftSubscribeMessage
	{
		/// <summary>
		/// The viewer who gifted the sub, or null if it was gifted anonymously (<see cref="IsAnonymous"/>).
		/// </summary>
		public Viewer Gifter { get; internal set; }

		/// <summary>
		/// Whether the gift was anonymous - in which case <see cref="Gifter"/> is null.
		/// </summary>
		public bool IsAnonymous { get; internal set; }

		/// <summary>
		/// The recipient's login name.
		/// </summary>
		public string RecipientUsername { get; internal set; }

		/// <summary>
		/// The recipient's display name.
		/// </summary>
		public string RecipientDisplayName { get; internal set; }

		/// <summary>
		/// The gifted subscription's tier.
		/// </summary>
		public SubscriptionTier Tier { get; internal set; }

		/// <summary>
		/// Number of months gifted.
		/// </summary>
		public int GiftMonths { get; internal set; }

		/// <summary>
		/// When we received the event.
		/// </summary>
		public DateTimeOffset Time { get; internal set; }
	}
}
