using System;

namespace Sandbox.MenuEngine;

[Hide]
public static partial class Account
{
	/// <summary>
	/// Return true if the user has linked their account to a streamer service like twitch
	/// </summary>
	public static bool HasLinkedStreamerServices => Sandbox.AccountInformation.Links.Count() > 0;

	/// <summary>
	/// A list of favourites packages
	/// </summary>
	public static IEnumerable<Package> Favourites => AccountInformation.Favourites;

	/// <summary>
	/// The date and time the user first created their account
	/// </summary>
	public static DateTimeOffset FirstSeen => AccountInformation.FirstSeen;
}
