using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.Services;

namespace MenuProject.MenuUI.Front;

/// <summary>
/// Front page helpers that read across the activity feed.
/// </summary>
public static class FrontPage
{
	/// <summary>Entries by other people about this package, newest first.</summary>
	static IEnumerable<Sandbox.Services.Feed> OthersIn( Sandbox.Services.Feed[] activity, Package package, int days )
	{
		if ( activity is null || package is null )
			yield break;

		var cutoff = DateTimeOffset.UtcNow.AddDays( -days );

		foreach ( var entry in activity )
		{
			if ( entry?.Package is null || entry.Player is null ) continue;
			if ( entry.Timestamp < cutoff ) continue;
			if ( entry.Player.Id.Value == Game.SteamId.Value ) continue;
			if ( !string.Equals( entry.Package.FullIdent, package.FullIdent, StringComparison.OrdinalIgnoreCase ) ) continue;

			yield return entry;
		}
	}

	/// <summary>Distinct friends seen in this package over the last week.</summary>
	public static int FriendsThisWeek( Sandbox.Services.Feed[] activity, Package package )
	{
		return OthersIn( activity, package, 7 )
			.Select( x => x.Player.Id.Value )
			.Distinct()
			.Count();
	}

	/// <summary>The friend most recently seen in this package, or null.</summary>
	public static string LatestFriendIn( Sandbox.Services.Feed[] activity, Package package )
	{
		return OthersIn( activity, package, 7 ).FirstOrDefault()?.Player?.Name;
	}

	/// <summary>Widest available art for a package - the Continue banner wants a landscape crop.</summary>
	public static string KeyArt( Package package )
	{
		if ( package is null ) return null;

		if ( !string.IsNullOrEmpty( package.ThumbWide ) ) return package.ThumbWide;
		if ( !string.IsNullOrEmpty( package.Thumb ) ) return package.Thumb;

		return package.VideoThumb;
	}
}
