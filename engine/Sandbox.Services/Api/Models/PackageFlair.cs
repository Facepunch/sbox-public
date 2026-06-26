namespace Sandbox.Services;

/// <summary>
/// A small icon-with-tooltip badge shown over a package's thumbnail. Computed when
/// the package is wrapped/served (never persisted), so we can keep adding reasons
/// over time — workshop-approved, updated-since-you-played, and so on.
///
/// Flair splits into two camps: <em>intrinsic</em> flair belongs to the package and
/// is the same for everyone (and is safe to cache), while <em>player-specific</em>
/// flair (see <see cref="IsPlayerSpecific"/>) depends on who's asking and is layered
/// on per request via <see cref="ForPlayer"/>.
/// </summary>
public class PackageFlair
{
	/// <summary>
	/// What this flair is, e.g. "workshop-approved". Drives the icon/tooltip and is
	/// used by the UI to style or de-duplicate them.
	/// </summary>
	public string Kind { get; set; }

	/// <summary>
	/// Material Symbols icon name, e.g. "verified". Rendered inside the badge.
	/// </summary>
	public string Icon { get; set; }

	/// <summary>
	/// Raw CSS applied inline to the badge, e.g.
	/// <c>"background-color: #2d8cf0; color: #fff;"</c>.
	/// </summary>
	public string Style { get; set; }

	/// <summary>
	/// Hover text explaining why the flair is shown.
	/// </summary>
	public string Tooltip { get; set; }

	/// <summary>Freshly published and not yet played — a discovery nudge for brand-new content.</summary>
	public static PackageFlair New() => new()
	{
		Kind = "new",
		Icon = "new_releases",
		Style = "background-color: #ffd008; color: #ec712c; border-radius: 100px;",
		Tooltip = "New — just published",
	};

	/// <summary>An ItemDef points at this package — it's an approved Workshop item (clothing etc).</summary>
	public static PackageFlair WorkshopApproved() => new()
	{
		Kind = "workshop-approved",
		Icon = "verified",
		Style = "background-color: #2d8cf0; color: #fff;",
		Tooltip = "Approved for the Workshop",
	};

	/// <summary>A newer version was published since the requesting user last played.</summary>
	public static PackageFlair UpdatedSincePlayed() => new()
	{
		Kind = "updated-since-played",
		Icon = "upgrade",
		Style = "background-color: #1db954; color: #fff;",
		Tooltip = "Updated since you last played",
	};

	public static PackageFlair Favourite() => new()
	{
		Kind = "favourited",
		Icon = "favorite",
		Style = "background-color: #ff4d6d; color: #fff;",
		Tooltip = "You have favourited",
	};

	/// <summary>
	/// Won a contest category. Always a trophy — winners are picked per category
	/// (there's no 2nd/3rd placement). Tooltip reads e.g. "Won Best Map in Spring Jam, March 2007".
	/// </summary>
	public static PackageFlair ContestWinner( string categoryTitle, string contestTitle, DateTimeOffset date ) => new()
	{
		Kind = "contest-winner",
		Icon = "emoji_events",
		Style = "background-color: #f5a623; color: #fff;",
		Tooltip = $"Won {categoryTitle} in {contestTitle}, {date:MMMM yyyy}",
	};

	/// <summary>
	/// Build the flair list to show a specific user: the intrinsic flair plus any
	/// player-specific flair derived from how they've interacted with the package.
	///
	/// Safe to call on a cached/shared (and possibly already player-mutated) list —
	/// it strips stale player-specific flair and returns a fresh list rather than
	/// appending, so it can't accumulate or leak between users.
	/// </summary>
	public static void ForPlayer( List<PackageFlair> flair, DateTimeOffset updated, PackageInteraction interaction, string typeName )
	{
		// "New" is an intrinsic "nobody's played this yet" nudge — but if this viewer has played it, it's
		// not new to them. Strip it here (the global TotalPlayerCount that sets it lags real plays anyway).
		if ( interaction.Used )
		{
			flair.RemoveAll( f => f.Kind == "new" );
		}

		if ( typeName == "map" || typeName == "game" )
		{
			if ( interaction.Used && interaction.LastUsed is { } lastPlayed && updated > lastPlayed )
			{
				flair.Add( UpdatedSincePlayed() );
			}
		}

		if ( interaction.Favourite )
		{
			flair.Add( Favourite() );
		}
	}
}
