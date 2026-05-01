using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface ILeaderboardApi
	{
		[Get( "/package/leaderboard/2/" )]
		Task<LeaderboardResponseEx> Query( LeaderboardQuery query );

		[Get( "/package/leaderboard/1/{package}/{leaderboard}/u/{steamid}/{mode}" )]
		Task<LeaderboardResponseLegacy> QueryLegacy( string package, string leaderboard, long steamid, string mode, [Query] int take = 20 );
	}
}


public struct LeaderboardQuery
{
	public LeaderboardQuery()
	{
	}

	[AliasAs( "ident" )]
	public string PackageIdent { get; set; }
	public string Stat { get; set; }
	public AggregationType Aggregation { get; set; }
	public SortingType SortOrder { get; set; }
	public DateType DateFilter { get; set; }
	public DateTimeOffset? Date { get; set; }
	public long CenterSteamId { get; set; }
	public long Offset { get; set; } = 0;
	public string Country { get; set; }
	public int Count { get; set; } = 50;
	public long SteamId { get; set; }

	public bool Friends { get; set; }

	public string Include { get; set; } // [steamid,steamid,steamid]

	/// <summary>
	/// For debugging, return the query
	/// </summary>
	public bool IncludeQuery { get; set; }

	public enum AggregationType : byte
	{
		Sum,
		Max,
		Min,
		Last,
		Avg
	}

	public enum SortingType : byte
	{
		Desc,
		Asc,
	}

	public enum DateType : byte
	{
		None,
		Year,
		Month,
		Week,
		Day,
	}
}


public class LeaderboardResponseEx
{
	public struct Entry
	{
		public long Rank { get; set; }
		public double Value { get; set; }
		public long SteamId { get; set; }
		public string CountryCode { get; set; }
		public string DisplayName { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public Dictionary<string, object> Data { get; set; }
	}

	public string Stat { get; set; }
	public long TotalEntries { get; set; }
	public Entry[] Entries { get; set; }
	public string Query { get; set; }
	public string DateDescription { get; set; }
}


/// <summary>
/// Internal on purpose so the structures so we can present data how we want
/// to game without api breaking.
/// </summary>
public class LeaderboardResponseLegacy
{
	public struct Entry
	{
		public bool Me { get; set; }
		public long Rank { get; set; }
		public double Value { get; set; }
		public string ValueString { get; set; }
		public long SteamId { get; set; }
		public string CountryCode { get; set; }
		public string DisplayName { get; set; }
	}

	public string Title { get; set; }
	public string DisplayName { get; set; }
	public string Description { get; set; }
	public string Unit { get; set; }
	public long TotalEntries { get; set; }
	public Entry[] Entries { get; set; }
}
