namespace Sandbox.Services;

public struct FindPackageQuery
{
	public FindPackageQuery()
	{

	}

	/// <summary>
	/// Which asset types to find - or null/empty for all
	/// </summary>
	public string Type;

	/// <summary>
	/// How to sort the list
	/// </summary>
	public SortMode Sort;

	/// <summary>
	/// SteamId of querying user (should be authenticated!)
	/// </summary>
	public long SteamId;

	/// <summary>
	/// Get the favourites of this user
	/// </summary>
	public long FavouritesSteamId;

	/// <summary>
	/// Only return packages in this collection
	/// </summary>
	public string InCollection;

	/// <summary>
	/// True if we want the total
	/// </summary>
	public bool GetTotalCount;

	/// <summary>
	/// True if we want facets
	/// </summary>
	public bool GetFacets;

	/// <summary>
	/// primary asset should match this value
	/// </summary>
	public string PrimaryAsset;

	/// <summary>
	/// Text search in description, title, summary etc
	/// </summary>
	public string SearchString;

	/// <summary>
	/// Org name
	/// </summary>
	public string Org;

	/// <summary>
	/// Must contain all tags
	/// </summary>
	public List<string> WithTag;

	/// <summary>
	/// In contest
	/// </summary>
	public string InContest;

	/// <summary>
	/// Content created for this game, specifically
	/// </summary>
	public string ForGame;

	/// <summary>
	/// Show hidden packages in this orgs
	/// </summary>
	public long[] OpenOrgs;

	/// <summary>
	/// If we want to find counter-strike maps, this would be set to the name of the counter-strike package
	/// </summary>
	public string TargetPackage;

	/// <summary>
	/// Only show packages that we haven't played
	/// </summary>
	public bool Unplayed;

	/// <summary>
	/// Facets like Category:Wall
	/// </summary>
	public Dictionary<string, string> Facets;

	public enum SortMode
	{
		Updated,
		Popular,
		Friends,
		Created,
		Random,
		Trending,

		FavouriteCount,
		ThumbsUp,
		ThumbsDown,
		InCollections_REMOVEME,

		/// <summary>
		/// Order by recently used
		/// </summary>
		Used,

		RankDay,
		RankWeek,
		RankMonth,

		Spawns,
		SpawnsDay,
		SpawnsWeek,
		SpawnsMonth,

		PlayersNow,

		/// <summary>
		/// No sorting
		/// </summary>
		None
	}

	public static FindPackageQuery Parse( string query, long steamid )
	{
		if ( string.IsNullOrWhiteSpace( query ) )
			return default;

		var find = new FindPackageQuery
		{
			SteamId = steamid
		};

		var tokens = query.ToLowerInvariant().Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var searchWords = new List<string>();
		var tags = new HashSet<string>();

		foreach ( var token in tokens )
		{
			// Skip pipe-separated tokens
			if ( token.Contains( '|' ) ) continue;

			// Handle tag prefixes
			if ( token.StartsWith( '+' ) )
			{
				tags.Add( token[1..] );
				continue;
			}

			// Skip excluded tags (not currently used in output)
			if ( token.StartsWith( '-' ) ) continue;

			// Handle key:value pairs
			var colonIndex = token.IndexOf( ':' );
			if ( colonIndex >= 0 )
			{
				// ignore malformed tokens like "org:" or ":org"
				if ( colonIndex > 0 && colonIndex < token.Length - 1 )
				{
					var key = token[..colonIndex];
					var value = token[(colonIndex + 1)..].Trim();

					if ( !ProcessToken( ref find, key, value ) )
					{
						// Unknown tokens become facets
						find.Facets ??= new();
						find.Facets[key] = value;
					}
				}
			}
			else
			{
				// Regular search word
				searchWords.Add( token );
			}
		}

		// Finalize search string and tags
		find.SearchString = string.Join( ' ', searchWords );
		ProcessTags( ref find, tags );

		return find;
	}

	private static bool ProcessToken( ref FindPackageQuery find, string key, string value )
	{
		switch ( key )
		{
			case "type":
				find.Type = value;
				return true;

			case "sort":

				// TODO: this needs to move to "is:fave"
				if ( value == "favourite" || value == "favourites" || value == "favorites" || value == "favorite" )
				{
					find.FavouritesSteamId = find.SteamId;
					find.Sort = SortMode.Used;
				}
				else
				{
					find.Sort = ParseSortMode( value );
				}
				return true;

			case "asset":
				find.PrimaryAsset = value.ToLower();
				return true;

			case "contest":
				find.InContest = value;
				return true;

			case "in":
				find.InCollection = value;
				return true;

			case "target":
				find.TargetPackage = value;
				return true;

			case "org":
				find.Org = value;
				return true;

			case "is":

				if ( value == "unplayed" )
					find.Unplayed = true;

				if ( value == "fave" )
					find.FavouritesSteamId = find.SteamId;
				// Note: "owner" case was tracked but never used in original
				return true;

			case "api":
				// API version was parsed but never used in original
				return true;

			default:
				return false; // Unknown token, will be treated as facet
		}
	}

	private static void ProcessTags( ref FindPackageQuery find, HashSet<string> tags )
	{
		if ( tags.Count == 0 ) return;

		// Extract and process game tags
		var gameTag = tags.FirstOrDefault( t => t.StartsWith( "game:" ) );
		if ( gameTag != null )
		{
			var game = gameTag[5..];
			find.ForGame = game == "any" ? null : game;
			tags.Remove( gameTag );
		}

		// Set remaining tags
		if ( tags.Count > 0 )
		{
			find.WithTag = tags.ToList();
		}
	}

	private static SortMode ParseSortMode( string sort )
	{
		return sort switch
		{
			"live" or "referenced" or "referencing" or "user" or "used" or "played" => SortMode.Used,
			"oldest" => SortMode.Created, // TODO: might need reverse order flag
			"newest" => SortMode.Created,
			"upvotes" => SortMode.ThumbsUp,
			"downvotes" => SortMode.ThumbsDown,
			"favcount" => SortMode.FavouriteCount,
			"friends" => SortMode.Friends,
			"random" => SortMode.Random,
			"popular" => SortMode.Popular,
			"updated" => SortMode.Updated,
			"trending" => SortMode.Trending,
			"rankd" or "rankday" => SortMode.RankDay,
			"rankw" or "rankweek" => SortMode.RankWeek,
			"rankm" or "rankmonth" => SortMode.RankMonth,
			"spawns" => SortMode.Spawns,
			"spawnsday" => SortMode.SpawnsDay,
			"spawnsweek" => SortMode.SpawnsWeek,
			"spawnsmonth" => SortMode.SpawnsMonth,
			"playersnow" => SortMode.PlayersNow,
			_ => SortMode.Popular // Default
		};
	}
}
