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
