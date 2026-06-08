namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// The tier of a Twitch subscription. Prime is the free Amazon Prime sub; Tier1/2/3 are the paid
	/// tiers (roughly $5 / $10 / $25).
	/// </summary>
	public enum SubscriptionTier
	{
		Prime,
		Tier1,
		Tier2,
		Tier3,
	}
}
