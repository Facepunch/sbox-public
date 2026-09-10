using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IAccountApi
	{
		[Post( "/account/login/2" )]
		Task<LoginResult> Login( object logindata );

		[Post( "/account/activity/" )]
		Task Activity( [Body] object activity );

		[Post( "/event/batch/1" )]
		Task SubmitEvents( [Body] object activity );

		[Post( "/account/services/" )]
		Task<ServiceToken> GetService( [Query] string service );

		[Post( "/account/getauthtoken/" )]
		Task<string> GetAuthToken( [Query] string session, [Query] string package, [Query] string service );

		/// <summary>
		/// Begin linking a third-party service. Returns a URL to open in a browser; the player
		/// authorizes the service there. The game is notified via Web PubSub (ClientMsg.ServiceLinked)
		/// when it completes — there's no need to poll.
		/// </summary>
		[Post( "/account/services/link" )]
		Task<ServiceLinkResult> BeginServiceLink( [Query] string service );

		/// <summary>
		/// List the player's linked services with their public info (name, avatar). No tokens.
		/// </summary>
		[Post( "/account/services/list" )]
		Task<List<ServiceLinkInfo>> ListServices();

		/// <summary>
		/// Get the player's reward state — their windows (requirements + progress) and any
		/// pending unclaimed offer to resume. Poll after a session and after claiming.
		/// </summary>
		[Get( "/account/rewards/1" )]
		Task<RewardState> GetRewards();

		/// <summary>
		/// Open a reward claim: returns the items on offer to choose from (or the existing
		/// unclaimed offer to resume), or null if there's nothing to claim right now.
		/// Note: claiming creates the drop, which resets the player's "since last reward" progress.
		/// </summary>
		[Post( "/account/rewards/claim/1" )]
		Task<RewardOffer> ClaimReward();

		/// <summary>
		/// Commit the player's pick(s) from an open offer. Grants the chosen item(s) to
		/// their inventory and closes the offer.
		/// </summary>
		[Post( "/account/rewards/choose/1" )]
		Task<RewardResult> ChooseReward( [Body] RewardChoice choice );
	}
}


public struct ServiceToken
{
	/// <summary>
	/// The UserId returned by the service
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// The Username returned by the service
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The Token returned by the service
	/// </summary>
	public string Token { get; set; }

	/// <summary>
	/// The type (ie "Twitch")
	/// </summary>
	public string Type { get; set; }
}


/// <summary>
/// The URL to open in a browser to begin/continue a service link.
/// </summary>
public struct ServiceLinkResult
{
	public string Url { get; set; }
}

/// <summary>
/// Public, token-free info about a linked service.
/// </summary>
public struct ServiceLinkInfo
{
	/// <summary>The service type name (ie "Twitch").</summary>
	public string Type { get; set; }

	/// <summary>The user's id on that service.</summary>
	public string Id { get; set; }

	/// <summary>The user's display name on that service.</summary>
	public string Name { get; set; }

	/// <summary>The user's avatar URL on that service.</summary>
	public string Avatar { get; set; }
}
