using Sandbox.Services.Players;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox;

public static partial class Storage
{
	public enum SortOrder
	{
		RankedByVote = 0,
		RankedByPublicationDate = 1,
		AcceptedForGameRankedByAcceptanceDate = 2,
		RankedByTrend = 3,
		FavoritedByFriendsRankedByPublicationDate = 4,
		CreatedByFriendsRankedByPublicationDate = 5,
		RankedByNumTimesReported = 6,
		CreatedByFollowedUsersRankedByPublicationDate = 7,
		NotYetRated = 8,
		RankedByTotalVotesAsc = 9,
		RankedByVotesUp = 10,
		RankedByTextSearch = 11,
		RankedByTotalUniqueSubscriptions = 12,
		RankedByPlaytimeTrend = 13,
		RankedByTotalPlaytime = 14,
		RankedByAveragePlaytimeTrend = 15,
		RankedByLifetimeAveragePlaytime = 16,
		RankedByPlaytimeSessionsTrend = 17,
		RankedByLifetimePlaytimeSessions = 18,
		RankedByLastUpdatedDate = 19,
	};

	/// <summary>
	/// Query the Steam Workshop for items
	/// </summary>
	public class Query
	{
		/// <summary>
		/// Tags that the item must have all of to be included in results.
		/// </summary>
		public HashSet<string> TagsRequired { get; set; } = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// Tags that the item must not have any of to be included in results.
		/// </summary>
		public HashSet<string> TagsExcluded { get; set; } = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// KeyValues that the item must match to be included in results.
		/// </summary>
		public Dictionary<string, string> KeyValues { get; set; } = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// Search Text
		/// </summary>
		public string SearchText { get; set; }

		/// <summary>
		/// Max Cache Age in seconds
		/// </summary>
		public int MaxCacheAge { get; set; }

		/// <summary>
		/// Sort Order
		/// </summary>
		public SortOrder SortOrder { get; set; } = SortOrder.RankedByVote;

		/// <summary>
		/// Number of days to consider for rank trend calculations
		/// </summary>
		public int RankTrendDays { get; set; } = 30;

		/// <summary>
		/// Run the query
		/// </summary>
		public Task<QueryResult> Run( CancellationToken token = default )
		{
			// gets first page of results
			return RunEx( "*", token );
		}

		/// <summary>
		/// Run the query
		/// </summary>
		internal async Task<QueryResult> RunEx( string nextToken, CancellationToken token = default )
		{
			var json = JsonSerializer.Serialize( this );
			using var q = NativeEngine.CUgcQuery.CreateQuery( json, nextToken );

			while ( !q.m_complete )
			{
				await Task.Delay( 100, token );
			}

			var resultJson = q.GetResultJson();

			var result = Json.Deserialize<QueryResult>( resultJson );
			result.SourceQuery = this;
			return result;
		}
	}

	/// <summary>
	/// The results of a Steam Workshop query
	/// </summary>
	public class QueryResult
	{
		public int ResultCount { get; set; }
		public int TotalCount { get; set; }
		public string NextCursor { get; set; }
		public List<QueryItem> Items { get; set; }

		internal Query SourceQuery;

		/// <summary>
		/// Returns true if there are more results to be fetched
		/// </summary>
		public bool HasMoreResults() => !string.IsNullOrEmpty( NextCursor );

		/// <summary>
		/// Get the next set of results from the query. Returns null if none.
		/// </summary>
		public Task<QueryResult> GetNextResults( CancellationToken token = default )
		{
			if ( !HasMoreResults() ) return null;
			if ( SourceQuery is null ) return null;

			return SourceQuery.RunEx( NextCursor, token );
		}
	}

	/// <summary>
	/// Details about a UGC item returned from a Steam Workshop query
	/// </summary>
	public class QueryItem
	{
		public ulong Id { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public int Visibility { get; set; }
		public bool Banned { get; set; }
		public bool Accepted { get; set; }
		public ulong FileHandle { get; set; }
		public string Preview { get; set; }
		public string Filename { get; set; }
		public ulong Size { get; set; }
		public string Url { get; set; }
		public int VotesUp { get; set; }
		public int VotesDown { get; set; }
		public float Score { get; set; }
		public string Metadata { get; set; }
		public Profile Owner { get; set; }

		[JsonConverter( typeof( UnixTimestampConverter ) )]
		public DateTimeOffset Created { get; set; }

		[JsonConverter( typeof( UnixTimestampConverter ) )]
		public DateTimeOffset Updated { get; set; }

		public List<string> Tags { get; set; }
		public Dictionary<string, string> KeyValues { get; set; }

		/// <summary>
		/// Install this item. This can return null if it's not of the right format.
		/// </summary>
		public async Task<Entry> Install( CancellationToken token = default )
		{
			var fs = await InstallWorkshopFile( Id, token );
			if ( fs == null ) return null;

			return CreateEntryFromFileSystem( fs );
		}
	}
}
