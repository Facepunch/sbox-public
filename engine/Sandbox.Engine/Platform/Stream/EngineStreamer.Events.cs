using SubscriptionTier = Sandbox.Streamer.SubscriptionTier;
using SubscribeMessage = Sandbox.Streamer.SubscribeMessage;
using GiftSubscribeMessage = Sandbox.Streamer.GiftSubscribeMessage;
using GiftSubscriptionsMessage = Sandbox.Streamer.GiftSubscriptionsMessage;
using RaidMessage = Sandbox.Streamer.RaidMessage;

namespace Sandbox.Engine;

internal static partial class Streamer
{
	/// <summary>
	/// A viewer subscribed or resubscribed. Enrolls them in the roster and notifies scene listeners.
	/// </summary>
	internal static void OnSubscribe( string username, string userId, string displayName, string color, string[] badges, SubscriptionTier tier, int cumulativeMonths, int streakMonths, bool isResub, string message )
	{
		var viewer = EnrollViewer( username, userId, displayName, color, badges );
		if ( viewer is null )
			return;

		var sub = new SubscribeMessage
		{
			Viewer = viewer,
			Tier = tier,
			CumulativeMonths = cumulativeMonths,
			StreakMonths = streakMonths,
			IsResub = isResub,
			Message = message,
			Time = DateTimeOffset.UtcNow,
		};

		Dispatch( x => x.OnStreamSubscribe( sub ) );
	}

	/// <summary>
	/// A viewer gifted a sub to a specific recipient. An anonymous gifter has no <see cref="Sandbox.Streamer.Viewer"/>.
	/// </summary>
	internal static void OnGiftSubscribe( string gifterUsername, string gifterUserId, string gifterDisplayName, string color, string[] badges, string recipientUsername, string recipientDisplayName, SubscriptionTier tier, int giftMonths, bool isAnonymous )
	{
		var gift = new GiftSubscribeMessage
		{
			Gifter = isAnonymous ? null : EnrollViewer( gifterUsername, gifterUserId, gifterDisplayName, color, badges ),
			IsAnonymous = isAnonymous,
			RecipientUsername = recipientUsername,
			RecipientDisplayName = recipientDisplayName,
			Tier = tier,
			GiftMonths = giftMonths,
			Time = DateTimeOffset.UtcNow,
		};

		Dispatch( x => x.OnStreamGiftSubscribe( gift ) );
	}

	/// <summary>
	/// A viewer gifted a batch of subs to the community. An anonymous gifter has no <see cref="Sandbox.Streamer.Viewer"/>.
	/// </summary>
	internal static void OnGiftSubscriptions( string gifterUsername, string gifterUserId, string gifterDisplayName, string color, string[] badges, SubscriptionTier tier, int count, int totalGifts, bool isAnonymous )
	{
		var gift = new GiftSubscriptionsMessage
		{
			Gifter = isAnonymous ? null : EnrollViewer( gifterUsername, gifterUserId, gifterDisplayName, color, badges ),
			IsAnonymous = isAnonymous,
			Count = count,
			Tier = tier,
			TotalGifts = totalGifts,
			Time = DateTimeOffset.UtcNow,
		};

		Dispatch( x => x.OnStreamGiftSubscriptions( gift ) );
	}

	/// <summary>
	/// Another channel raided ours. The raider isn't a chatter here, so we don't touch the roster.
	/// </summary>
	internal static void OnRaid( string raiderUsername, string raiderDisplayName, int viewerCount )
	{
		var raid = new RaidMessage
		{
			RaiderUsername = raiderUsername,
			RaiderDisplayName = raiderDisplayName,
			ViewerCount = viewerCount,
			Time = DateTimeOffset.UtcNow,
		};

		Dispatch( x => x.OnStreamRaid( raid ) );
	}
}
