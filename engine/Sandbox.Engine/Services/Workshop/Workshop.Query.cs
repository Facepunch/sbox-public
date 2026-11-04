using Sandbox.Services;
using System.Threading;

namespace Sandbox;

internal static partial class Workshop
{
	/// <summary>
	/// What kind of thing an item is - its tag on the Workshop
	/// </summary>
	internal enum ItemType
	{
		Map,
		Game,
		Addon,
	}

	internal enum SortOrder
	{
		/// <summary>
		/// The best matches when there's something to search for, otherwise <see cref="Popular"/>
		/// </summary>
		Default,
		Popular,
		Trending,
		Newest,
		Updated,
		Votes,
		Subscriptions,
	}

	/// <summary>
	/// Finds items. Make one with what to search for, say what else you're after, then <see cref="RunAsync"/> it for
	/// the first page of results.
	/// <code>
	/// var maps = await new Workshop.Query( "arena" ).WithType( Workshop.ItemType.Map ).WithSubscribed().RunAsync();
	/// </code>
	/// </summary>
	internal sealed class Query
	{
		/// <summary>
		/// Where it looks - everything on the Workshop, or just what's on this machine
		/// </summary>
		enum Source { Workshop, Subscribed, Installed }

		readonly string search;
		readonly HashSet<ItemType> types = new();
		readonly HashSet<string> tags = new( StringComparer.OrdinalIgnoreCase );
		readonly HashSet<string> excludedTags = new( StringComparer.OrdinalIgnoreCase );
		SteamId author;
		SortOrder sort;
		Source source;

		/// <summary>
		/// Find items, searching for <paramref name="search"/> if there's something to search for
		/// </summary>
		public Query( string search = null )
		{
			this.search = string.IsNullOrWhiteSpace( search ) ? null : search.Trim();
		}

		/// <summary>
		/// Just items of this type. Called again, any of the types.
		/// </summary>
		public Query WithType( ItemType type )
		{
			types.Add( type );
			return this;
		}

		/// <summary>
		/// Just items with this tag
		/// </summary>
		public Query WithTag( string tag )
		{
			tags.Add( tag );
			return this;
		}

		/// <summary>
		/// No items with this tag
		/// </summary>
		public Query WithoutTag( string tag )
		{
			excludedTags.Add( tag );
			return this;
		}

		/// <summary>
		/// Just what this Steam account published
		/// </summary>
		public Query WithAuthor( SteamId steamId )
		{
			author = steamId;
			return this;
		}

		public Query WithSort( SortOrder sortOrder )
		{
			sort = sortOrder;
			return this;
		}

		/// <summary>
		/// Just what the user's subscribed to, rather than everything on the Workshop
		/// </summary>
		public Query WithSubscribed()
		{
			source = Source.Subscribed;
			return this;
		}

		/// <summary>
		/// Just what's downloaded on this machine, subscribed to or not, rather than everything on the Workshop. Works offline.
		/// </summary>
		public Query WithInstalled()
		{
			source = Source.Installed;
			return this;
		}

		/// <summary>
		/// The first page of what it finds - <see cref="QueryResult.GetNextResults"/> for the rest. The Workshop's
		/// pages are 50; what's on this machine all comes on the one.
		/// </summary>
		public async Task<QueryResult> RunAsync( CancellationToken token = default )
		{
			if ( source == Source.Workshop )
				return await QueryResult.RunAsync( ToRequest(), "*", token );

			// Steam lists what's here, the Workshop says what they are - one it can't say anything about is just its
			// id - and the rest of the query's worked out here
			var ids = Ugc.LocalItemIds( source == Source.Subscribed );
			var found = (await FetchAsync( ids, token )).ToDictionary( x => x.Id );

			var items = ids.Select( id => found.GetValueOrDefault( id ) ?? new Item { Id = id } );

			var words = search?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ?? Array.Empty<string>();
			var matching = Order( items.Where( x => Matches( x, words ) ) ).ToArray();

			return new QueryResult { Items = matching, TotalCount = matching.Length };
		}

		static string Tag( ItemType type ) => type.ToString().ToLowerInvariant();

		/// <summary>
		/// The query as Steam takes it - see <see cref="RunQueryAsync"/>
		/// </summary>
		object ToRequest() => new
		{
			// Steam can only ask for all of the tags, not any
			TagsRequired = tags.Concat( types.Select( Tag ) ).ToArray(),
			TagsExcluded = excludedTags.ToArray(),
			SearchText = search,
			Author = (ulong)author,
			RankTrendDays = 30,
			SortOrder = (int)(sort switch
			{
				SortOrder.Trending => Storage.SortOrder.RankedByTrend,
				SortOrder.Newest => Storage.SortOrder.RankedByPublicationDate,
				SortOrder.Updated => Storage.SortOrder.RankedByLastUpdatedDate,
				SortOrder.Votes => Storage.SortOrder.RankedByVotesUp,
				SortOrder.Subscriptions => Storage.SortOrder.RankedByTotalUniqueSubscriptions,
				SortOrder.Popular => Storage.SortOrder.RankedByVote,
				_ => search is null ? Storage.SortOrder.RankedByVote : Storage.SortOrder.RankedByTextSearch
			})
		};

		/// <summary>
		/// Does this item match - for what's on this machine, which Steam can't search
		/// </summary>
		bool Matches( Item item, string[] words )
		{
			return (types.Count == 0 || types.Any( t => item.Tags.Contains( Tag( t ), StringComparer.OrdinalIgnoreCase ) ))
				&& tags.All( t => item.Tags.Contains( t, StringComparer.OrdinalIgnoreCase ) )
				&& !excludedTags.Any( t => item.Tags.Contains( t, StringComparer.OrdinalIgnoreCase ) )
				&& (author == 0 || (item.Owner is { } owner && owner.Id == author))
				&& words.All( w => item.Title?.Contains( w, StringComparison.OrdinalIgnoreCase ) ?? false );
		}

		/// <summary>
		/// In this order - for what's on this machine. There's nothing to say what's trending, so that's the best rated.
		/// </summary>
		IEnumerable<Item> Order( IEnumerable<Item> items ) => sort switch
		{
			SortOrder.Popular or SortOrder.Trending => items.OrderByDescending( x => x.Score ),
			SortOrder.Votes => items.OrderByDescending( x => x.VotesUp ),
			SortOrder.Newest => items.OrderByDescending( x => x.Created ),
			SortOrder.Updated => items.OrderByDescending( x => x.Updated ),
			SortOrder.Subscriptions => items.OrderByDescending( x => x.Subscriptions ),

			// The order Steam has them in
			_ => items
		};
	}

	/// <summary>
	/// A page of what a <see cref="Query"/> found
	/// </summary>
	internal sealed class QueryResult
	{
		public Item[] Items { get; init; } = Array.Empty<Item>();

		/// <summary>
		/// How many there are altogether, on every page
		/// </summary>
		public int TotalCount { get; init; }

		/// <summary>
		/// What to ask Steam for the next page, if there is one
		/// </summary>
		object request;
		string cursor;

		/// <summary>
		/// Is there another page
		/// </summary>
		public bool HasMoreResults() => request is not null;

		/// <summary>
		/// The next page, or null if this was the last
		/// </summary>
		public Task<QueryResult> GetNextResults( CancellationToken token = default )
		{
			return HasMoreResults() ? RunAsync( request, cursor, token ) : Task.FromResult<QueryResult>( null );
		}

		internal static async Task<QueryResult> RunAsync( object request, string cursor, CancellationToken token )
		{
			var page = await RunQueryAsync<Page>( request, cursor, token );
			var next = HasNextPage( cursor, page?.NextCursor, page?.Items?.Count ?? 0 );

			return new QueryResult
			{
				Items = page?.Items?.ToArray() ?? Array.Empty<Item>(),
				TotalCount = page?.TotalCount ?? 0,
				request = next ? request : null,
				cursor = next ? page.NextCursor : null,
			};
		}
	}

	/// <summary>
	/// A page of items, as Steam answers a query
	/// </summary>
	sealed class Page
	{
		public int TotalCount { get; set; }
		public string NextCursor { get; set; }
		public List<Item> Items { get; set; }
	}

	/// <summary>
	/// Ask Steam - a query as it takes it, for the page from <paramref name="cursor"/> ("*" for the first), read as
	/// <typeparamref name="T"/>. What every workshop query comes down to, <see cref="Storage.Query"/>'s too. Null if
	/// Steam couldn't be asked.
	/// </summary>
	internal static async Task<T> RunQueryAsync<T>( object request, string cursor, CancellationToken token ) where T : class
	{
		// The native side wants the sort order as a number - System.Text.Json's default
		var json = System.Text.Json.JsonSerializer.Serialize( request );

		using var query = NativeEngine.CUgcQuery.CreateQuery( json, cursor );
		if ( query.IsNull ) return null;

		while ( !query.m_complete )
		{
			await Task.Delay( 50, token );
		}

		return query.m_success ? Json.Deserialize<T>( query.GetResultJson() ) : null;
	}

	/// <summary>
	/// Is there a page after the one from <paramref name="cursor"/>? Some queries (what someone published) say the
	/// same cursor again rather than none.
	/// </summary>
	internal static bool HasNextPage( string cursor, string next, int count )
	{
		return count > 0 && !string.IsNullOrEmpty( next ) && next != cursor;
	}

	/// <summary>
	/// How old an answer Steam can give from its cache when we ask about items by id - their details hardly change
	/// </summary>
	const int DetailsCacheAge = 10 * 60;

	/// <summary>
	/// Ask the Workshop about an item you know the id of. Null if there isn't one, or the Workshop can't be asked.
	/// </summary>
	internal static async Task<Item> FetchAsync( ulong id, CancellationToken token = default )
	{
		return (await FetchAsync( [id], token )).FirstOrDefault();
	}

	/// <summary>
	/// Ask the Workshop about the items with these ids - leaving out any there aren't, or all of them if it can't be asked.
	/// Steam can answer from what it's cached in the last <paramref name="maxCacheAge"/> seconds.
	/// </summary>
	internal static async Task<Item[]> FetchAsync( IReadOnlyCollection<ulong> ids, CancellationToken token, int maxCacheAge = DetailsCacheAge )
	{
		var pages = await Task.WhenAll( ids.Chunk( 50 ).Select( chunk =>
			RunQueryAsync<Page>( new { FileIds = chunk, MaxCacheAge = maxCacheAge }, "*", token ) ) );

		// One that's been deleted comes back with nothing in it
		return pages.SelectMany( x => x?.Items ?? [] )
			.Where( x => !string.IsNullOrEmpty( x.Title ) )
			.ToArray();
	}
}
