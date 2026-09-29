namespace Sandbox.Services.Players;

/// <summary>
/// An overview of a player. Only available if their profile isn't set to private.
/// </summary>
public sealed class Overview
{
	public Profile Player { get; init; }
	public long GamesPlayed { get; init; }
	public long TotalSessions { get; init; }
	public long SecondsPlayed { get; init; }
	public long Achievements { get; init; }

	/// <summary>
	/// A json string representing how their avatar is dressed
	/// </summary>
	public string AvatarJson { get; init; }
	[Obsolete( "Comments were replaced with forum posts" )] public long TotalComments { get; init; }
	public long TotalFavourites { get; init; }
	public long TotalReviews { get; init; }
	public long NegativeReviews { get; init; }
	public long PositiveReviews { get; init; }
	public Package MostPlayed { get; init; }
	public Package LatestPlayed { get; init; }

	/// <summary>
	/// The games they've spent the most time in, most first - with how long, and how far they got.
	/// </summary>
	public IReadOnlyList<PlayedGame> TopPlayed { get; init; } = [];

	/// <summary>
	/// The games they've played lately, most recent first.
	/// </summary>
	public IReadOnlyList<PlayedGame> RecentlyPlayed { get; init; } = [];

	/// <summary>
	/// A game someone's played, and what they've done in it.
	/// </summary>
	public sealed class PlayedGame
	{
		public Package Package { get; init; }

		/// <summary>
		/// How long they've played it, in total.
		/// </summary>
		public long SecondsPlayed { get; init; }

		/// <summary>
		/// How many of its achievements they've unlocked.
		/// </summary>
		public int Achievements { get; init; }

		/// <summary>
		/// When they last played it.
		/// </summary>
		public DateTimeOffset LastPlayed { get; init; }

		internal static PlayedGame From( Sandbox.Services.PlayerPackageEntry entry )
		{
			if ( entry?.Package is null ) return null;

			return new PlayedGame
			{
				Package = RemotePackage.FromDto( entry.Package ),
				SecondsPlayed = entry.SecondsPlayed,
				Achievements = entry.AchUnlocked,
				LastPlayed = entry.LastSeen,
			};
		}
	}

	public static async Task<Overview> Get( SteamId steamid )
	{
		return Overview.From( await Sandbox.Backend.Players.GetOverview( steamid ) );
	}

	internal static Overview From( Sandbox.Services.PlayerOverview p )
	{
		if ( p is null ) return default;

		return new Overview
		{
			Player = Profile.From( p.Player ),
			GamesPlayed = p.GamesPlayed,
			TotalSessions = p.TotalSessions,
			SecondsPlayed = p.SecondsPlayed,
			Achievements = p.Achievements,
			AvatarJson = p.Avatar,
			TotalFavourites = p.TotalFavourites,
			TotalReviews = p.TotalReviews,
			NegativeReviews = p.NegativeReviews,
			PositiveReviews = p.PositiveReviews,
			MostPlayed = RemotePackage.FromDto( p.MostPlayed ),
			LatestPlayed = RemotePackage.FromDto( p.LatestPlayed ),
			TopPlayed = p.TopPlayed?.Select( PlayedGame.From ).Where( x => x is not null ).ToArray() ?? [],
			RecentlyPlayed = p.RecentlyPlayed?.Select( PlayedGame.From ).Where( x => x is not null ).ToArray() ?? [],
		};
	}
}
