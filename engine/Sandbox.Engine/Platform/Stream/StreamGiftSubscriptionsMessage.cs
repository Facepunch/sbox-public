namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// A viewer gifted a batch of subscriptions to the community at once (a "mystery" / community gift).
	/// The individual recipients arrive separately as <see cref="GiftSubscribeMessage"/> events.
	/// </summary>
	public struct GiftSubscriptionsMessage
	{
		/// <summary>
		/// The viewer who gifted the subs, or null if it was gifted anonymously (<see cref="IsAnonymous"/>).
		/// </summary>
		public Viewer Gifter { get; internal set; }

		/// <summary>
		/// Whether the gift was anonymous - in which case <see cref="Gifter"/> is null.
		/// </summary>
		public bool IsAnonymous { get; internal set; }

		/// <summary>
		/// How many subscriptions were gifted in this batch.
		/// </summary>
		public int Count { get; internal set; }

		/// <summary>
		/// The gifted subscriptions' tier.
		/// </summary>
		public SubscriptionTier Tier { get; internal set; }

		/// <summary>
		/// The gifter's lifetime total of subs gifted to this channel, or 0 if unknown.
		/// </summary>
		public int TotalGifts { get; internal set; }

		/// <summary>
		/// When we received the event.
		/// </summary>
		public DateTimeOffset Time { get; internal set; }
	}
}
