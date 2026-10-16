using Steamworks;

namespace Sandbox.Services;

/// <summary>
/// Why a workshop download didn't work out, in words someone can do something with
/// </summary>
internal static partial class Ugc
{
	/// <summary>
	/// A failed download - what went wrong, short, and what that means and what to do about it
	/// </summary>
	internal record struct Failure( string Message, string Help );

	static readonly Failure CouldntStart = new( "Steam wouldn't start it", "Try again in a moment" );
	static readonly Failure Stalled = new( "Download stalled", "Check your connection" );

	/// <summary>
	/// What one of Steam's results means for a workshop download
	/// </summary>
	internal static Failure Explain( Result result ) => result switch
	{
		Result.NoConnection or Result.Timeout or Result.ConnectFailed or Result.ServiceUnavailable or Result.RemoteCallFailed
			=> new( "Couldn't reach Steam", "Check your connection" ),

		Result.NotLoggedOn => new( "Steam is offline", "Log in to Steam to download" ),
		Result.FileNotFound => new( "Removed from the Workshop", "Its creator deleted it" ),
		Result.AccessDenied or Result.InsufficientPrivilege => new( "It's private", "Only its creator can get it" ),
		Result.Banned or Result.Revoked or Result.Disabled => new( "It's been taken down", "It broke the Workshop rules" ),
		Result.RegionLocked => new( "Not available here", "Steam blocks it in your region" ),
		Result.DiskFull => new( "Your disk is full", "Free up space, then try again" ),

		Result.IOFailure or Result.PersistFailed or Result.LockingFailed or Result.DataCorruption
			=> new( "Couldn't save it", "Restart the game and try again" ),

		Result.Busy or Result.LimitExceeded or Result.RateLimitExceeded or Result.Pending
			=> new( "Steam is busy", "Try again in a bit" ),

		Result.Cancelled => new( "Download cancelled", "Steam cancelled it" ),

		_ => new( "Download failed", $"Steam said {result}" ),
	};
}
