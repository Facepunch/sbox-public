namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// A viewer subscribed or resubscribed to the channel. New subs and resubs both arrive here -
	/// check <see cref="IsResub"/> to tell them apart.
	/// </summary>
	public struct SubscribeMessage
	{
		/// <summary>
		/// The subscriber.
		/// </summary>
		public Viewer Viewer { get; internal set; }

		/// <summary>
		/// The subscription tier.
		/// </summary>
		public SubscriptionTier Tier { get; internal set; }

		/// <summary>
		/// Total months this viewer has been subscribed, cumulatively.
		/// </summary>
		public int CumulativeMonths { get; internal set; }

		/// <summary>
		/// Consecutive months subscribed, or 0 if the viewer chose not to share their streak.
		/// </summary>
		public int StreakMonths { get; internal set; }

		/// <summary>
		/// False for a brand-new subscription, true for a resub.
		/// </summary>
		public bool IsResub { get; internal set; }

		/// <summary>
		/// The optional message the viewer attached to a resub, or null.
		/// </summary>
		public string Message { get; internal set; }

		/// <summary>
		/// When we received the event.
		/// </summary>
		public DateTimeOffset Time { get; internal set; }
	}
}
