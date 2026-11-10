namespace Sandbox.Services;

public struct LoginResult
{
	/// <summary>
	/// Our steamid, redundant but here for verification
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// Current client hash (the login session cookie)
	/// </summary>
	public string Session { get; set; }

	/// <summary>
	/// If we borrowed this game, this is the real owner id
	/// </summary>
	public string OwnerId { get; set; }

	/// <summary>
	/// The azure pubsub endoint to connect to the messaging service.
	/// </summary>
	public string MessagingEndpoint { get; set; }

	/// <summary>
	/// A list of services that we have linked
	/// </summary>
	public ServiceType[] Links { get; set; }

	/// <summary>
	/// A list of organizations of which we're a member
	/// </summary>
	public OrganizationMinimal[] Memberships { get; set; }

	/// <summary>
	/// A list of our favourited games
	/// </summary>
	public PackageWrapMinimal[] Favourites { get; set; }

	/// <summary>
	/// The last time we were seen
	/// </summary>
	public DateTimeOffset LastSeen { get; set; }

	/// <summary>
	/// The first time we were seen
	/// </summary>
	public DateTimeOffset FirstSeen { get; set; }

	/// <summary>
	/// Json Clothing for their avatar that was stored on the backend
	/// </summary>
	public string AvatarJson { get; set; }

	/// <summary>
	/// Gives us a quick way to get the player's score immediately, without having to re-query
	/// </summary>
	public Player Player { get; set; }

	/// <summary>
	/// If true then this user will send us analytic data, like errors and performance metrics.
	/// This should generally always be on - we only disable it if our backend can't handle the load.
	/// </summary>
	public bool UseAnalytics { get; set; }

}
