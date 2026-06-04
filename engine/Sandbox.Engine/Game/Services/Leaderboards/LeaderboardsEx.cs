using Refit;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sandbox.Services;

public static partial class Leaderboards
{
	public static Board2 GetFromStat( string packageIdent, string statName )
	{
		Assert.NotNull( packageIdent, "No package specified" );
		Assert.NotNull( statName, "No stat specified" );

		return new Board2( packageIdent, statName );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	public static Board2 GetFromStat( string statName ) => GetFromStat( Application.GameIdent, statName );

	public class Board2
	{
		LeaderboardQuery query;

		public string Stat
		{
			get => query.Stat;
			set => query.Stat = value;
		}

		public Board2( string package, string name )
		{
			query = new LeaderboardQuery();
			query.PackageIdent = package;
			query.Stat = name;
			query.Count = 30;

			Entries = Array.Empty<Entry>();
		}

		public void SetAggregationSum() => query.Aggregation = LeaderboardQuery.AggregationType.Sum;
		public void SetAggregationAvg() => query.Aggregation = LeaderboardQuery.AggregationType.Avg;
		public void SetAggregationMin() => query.Aggregation = LeaderboardQuery.AggregationType.Min;
		public void SetAggregationMax() => query.Aggregation = LeaderboardQuery.AggregationType.Max;
		public void SetAggregationLast() => query.Aggregation = LeaderboardQuery.AggregationType.Last;

		public void SetSortAscending() => query.SortOrder = LeaderboardQuery.SortingType.Asc;
		public void SetSortDescending() => query.SortOrder = LeaderboardQuery.SortingType.Desc;

		public void SetFriendsOnly( bool friendsOnly ) => query.Friends = friendsOnly;

		public void SetCountryCode( string countryCode ) => query.Country = countryCode;
		public void SetCountryAuto() => SetCountryCode( "auto" );

		public void FilterByYear() => query.DateFilter = LeaderboardQuery.DateType.Year;
		public void FilterByMonth() => query.DateFilter = LeaderboardQuery.DateType.Month;
		public void FilterByWeek() => query.DateFilter = LeaderboardQuery.DateType.Week;
		public void FilterByDay() => query.DateFilter = LeaderboardQuery.DateType.Day;
		public void FilterByNone() => query.DateFilter = LeaderboardQuery.DateType.None;
		public void SetDatePeriod( DateTime dateTime ) => query.Date = dateTime;

		/// <summary>
		/// Center the results on this steamid, show the surrounding results with this in the middle.
		/// </summary>
		public void CenterOnSteamId( long steamid ) => query.CenterSteamId = steamid;

		/// <summary>
		/// Center the results on you, show the surrounding results with you in the middle.
		/// </summary>
		public void CenterOnMe() => CenterOnSteamId( (long)Utility.Steam.SteamId );

		/// <summary>
		/// If they have any results, include these steamids in the results - regardless of their position.
		/// </summary>
		public void IncludeSteamIds( params long[] steamids ) => query.Include = steamids is not null ? string.Join( ',', steamids ) : null;


		/// <summary>
		/// The steamid to get information about. If unset then this defaults to the current player.
		/// </summary>
		public long TargetSteamId { get; set; }

		/// <summary>
		/// The maximum entries to respond with.
		/// </summary>
		public int MaxEntries
		{
			get => query.Count;
			set => query.Count = value;
		}

		/// <summary>
		/// The offset to start at. If less than 0, we will start from the bottom.
		/// </summary>
		public int Offset
		{
			get => (int)query.Offset;
			set => query.Offset = value;
		}

		/// <summary>
		/// The total number of chart entries for this board.
		/// </summary>
		public long TotalEntries { get; internal set; }

		/// <summary>
		/// If you are restructing by time period, this is the name of the period
		/// </summary>
		public string TimePeriodDescription { get; internal set; }

		/// <summary>
		/// The group of entries for this board. This is usually the entries that surround
		/// the TargetSteamId.
		/// </summary>
		public Entry[] Entries { get; set; }

		void From( LeaderboardResponseEx result )
		{
			TotalEntries = result.TotalEntries;
			TimePeriodDescription = result.DateDescription;
			Entries = result.Entries.Select( x => new Entry( x ) ).ToArray();
		}

		// this is static on purpose, we don't want them to be able to create a new Board
		// and query leaderboards at whatever rate they choose! 
		static SemaphoreSlim leaderboardMutex = new SemaphoreSlim( 1, 1 );

		public async Task Refresh( CancellationToken cancellation = default )
		{
			if ( Backend.Leaderboards is null )
				return;

			await leaderboardMutex.WaitAsync( cancellation );

			try
			{
				var targetId = TargetSteamId != 0 ? TargetSteamId : (long)Steamworks.SteamClient.SteamId.Value;
				var result = await Backend.Leaderboards.Query( query );

				if ( result is null )
					return;

				From( result );
			}
			catch ( ApiException )
			{
				// Ignore 429's and 404's
			}
			finally
			{
				leaderboardMutex.Release();
			}
		}
		public readonly struct Entry
		{
			/// <summary>
			/// The rank in the board
			/// </summary>
			public readonly long Rank;

			/// <summary>
			/// The value in the board
			/// </summary>
			public readonly double Value;

			/// <summary>
			/// The steamid of the entry
			/// </summary>
			public readonly long SteamId;

			/// <summary>
			/// The country which this entry is from
			/// </summary>
			public readonly string CountryCode;

			/// <summary>
			/// The player's display name
			/// </summary>
			public readonly string DisplayName;

			/// <summary>
			/// The time this entry was created.
			/// </summary>
			public readonly DateTimeOffset Timestamp;

			/// <summary>
			/// Data associated with this entry
			/// </summary>
			[Obsolete( "DataUrl contains the url to fetch the data from" )]
			public readonly Dictionary<string, object> Data;

			/// <summary>
			/// If set then this entry has an associated data entry. This file is 
			/// usually a json object which was submitted with the stat. You can use
			/// this for replays and stuff.
			/// </summary>
			public readonly string DataUrl;

			internal Entry( LeaderboardResponseEx.Entry entry )
			{
				Rank = entry.Rank;
				Value = entry.Value;
				SteamId = entry.SteamId;
				CountryCode = entry.CountryCode;
				DisplayName = entry.DisplayName;
				Timestamp = entry.Timestamp;
				DataUrl = entry.DataUrl;
			}
		}
	}


}
