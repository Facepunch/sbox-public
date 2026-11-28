using Sandbox.Services;
using System.Collections.Concurrent;
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

		/// <summary>
		/// The most played, all time - Steam counts the time it's been played for
		/// </summary>
		Playtime,

		/// <summary>
		/// Just what the creators the user follows published, newest first
		/// </summary>
		Followed,
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
		bool favourited;
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

		/// <summary>
		/// Just what the user's favourited on the Workshop
		/// </summary>
		public Query WithFavourited()
		{
			author = Utility.Steam.SteamId;
			favourited = true;
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
			var words = search?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ?? Array.Empty<string>();

			if ( source == Source.Workshop )
			{
				var result = await QueryResult.RunAsync( ToRequest(), "*", token );

				// Steam doesn't search what someone published - all of it comes on the one page, searched here
				if ( author != 0 && words.Length > 0 && result is not null )
					return new QueryResult { Items = result.Items.Where( x => Matches( x, words ) ).ToArray(), TotalCount = result.TotalCount };

				return result;
			}

			// Steam lists what's here, the Workshop says what they are - one it can't say anything about is just its
			// id - and the rest of the query's worked out here
			var ids = Ugc.LocalItemIds( source == Source.Subscribed );
			var found = (await FetchAsync( ids, token )).ToDictionary( x => x.Id );

			var items = ids.Select( id => found.GetValueOrDefault( id ) ?? new Item { Id = id } );

			var matching = Order( items.Where( x => Matches( x, words ) ) ).ToArray();

			return new QueryResult { Items = matching, TotalCount = matching.Length };
		}

		static string Tag( ItemType type ) => type.ToString().ToLowerInvariant();

		/// <summary>
		/// The query as Steam takes it - see <see cref="RunPageAsync"/>
		/// </summary>
		object ToRequest() => new
		{
			// Steam can only ask for all of the tags, not any
			TagsRequired = tags.Concat( types.Select( Tag ) ).ToArray(),
			TagsExcluded = excludedTags.ToArray(),
			SearchText = search,
			Author = (ulong)author,
			UserList = favourited ? "favorited" : null,
			RankTrendDays = 30,
			SortOrder = (int)(sort switch
			{
				SortOrder.Trending => Storage.SortOrder.RankedByTrend,
				SortOrder.Newest => Storage.SortOrder.RankedByPublicationDate,
				SortOrder.Updated => Storage.SortOrder.RankedByLastUpdatedDate,
				SortOrder.Votes => Storage.SortOrder.RankedByVotesUp,
				SortOrder.Subscriptions => Storage.SortOrder.RankedByTotalUniqueSubscriptions,
				SortOrder.Playtime => Storage.SortOrder.RankedByTotalPlaytime,
				SortOrder.Popular => Storage.SortOrder.RankedByVote,
				SortOrder.Followed => Storage.SortOrder.CreatedByFollowedUsersRankedByPublicationDate,
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
				&& (author == 0 || favourited || (item.Owner is { } owner && owner.Id == author))
				&& words.All( w => item.Title?.Contains( w, StringComparison.OrdinalIgnoreCase ) ?? false );
		}

		/// <summary>
		/// In this order - for what's on this machine. There's nothing to say what's trending or most played, so those are
		/// the best rated.
		/// </summary>
		IEnumerable<Item> Order( IEnumerable<Item> items ) => sort switch
		{
			SortOrder.Popular or SortOrder.Trending or SortOrder.Playtime => items.OrderByDescending( x => x.Score ),
			SortOrder.Votes => items.OrderByDescending( x => x.VotesUp ),
			SortOrder.Newest or SortOrder.Followed => items.OrderByDescending( x => x.Created ),
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
			var page = await RunPageAsync( request, cursor, token );
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
	internal sealed class Page
	{
		/// <summary>
		/// Number of results Steam reported for this page.
		/// </summary>
		public int ResultCount { get; set; }
		public int TotalCount { get; set; }
		public string NextCursor { get; set; }
		public List<Item> Items { get; set; }
	}

	static ulong nextQueryId;
	static readonly ConcurrentDictionary<ulong, TaskCompletionSource<string>> pendingQueries = new();

	internal static void OnQueryCompleted( ulong requestId, string json )
	{
		if ( pendingQueries.TryRemove( requestId, out var completion ) )
			completion.TrySetResult( json );
	}

	static async Task<string> RequestPageAsync( string json, string cursor, CancellationToken token )
	{
		if ( !ThreadSafe.IsMainThread ) await MainThread.Wait();
		token.ThrowIfCancellationRequested();

		if ( !SteamApi.SteamClient.IsValid && !SteamNative.GameServer.IsAvailable() )
			return null;

		var id = ++nextQueryId;
		var completion = new TaskCompletionSource<string>( TaskCreationOptions.RunContinuationsAsynchronously );
		pendingQueries[id] = completion;

		try
		{
			if ( !SteamNative.Workshop.StartQuery( id, json, cursor ) ) return null;
			return await completion.Task.WaitAsync( token ).ConfigureAwait( false );
		}
		finally
		{
			if ( pendingQueries.TryRemove( id, out _ ) )
				MainThread.Queue( () => SteamNative.Workshop.CancelQuery( id ) );
		}
	}

	/// <summary>
	/// Reads one page from Steam and caches its items for later lookups by Workshop item ID.
	/// Pass "*" for the first page. Returns null if Steam couldn't answer.
	/// </summary>
	internal static async Task<Page> RunPageAsync( object request, string cursor, CancellationToken token )
	{
		// The native side wants the sort order as a number - System.Text.Json's default
		var json = System.Text.Json.JsonSerializer.Serialize( request );
		var result = await RequestPageAsync( json, cursor, token );

		if ( string.IsNullOrEmpty( result ) ) return null;
		var page = Json.Deserialize<Page>( result );
		foreach ( var item in page?.Items ?? [] )
		{
			if ( item.Id is 0 or ulong.MaxValue ) continue;

			// Steam returns an empty title for a deleted item.
			if ( string.IsNullOrEmpty( item.Title ) )
				ItemsById.TryRemove( item.Id, out _ );
			else
				ItemsById[item.Id] = item;
		}

		return page;
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
	/// How old, in seconds, Steam's answer about an item can be and still count as current when deciding whether to
	/// update it. Asking about a game that's about to be played with this too means Steam answers that check from its cache.
	/// </summary>
	internal const int UpdateCheckCacheAge = 60;

	static readonly ConcurrentDictionary<ulong, Item> ItemsById = new();

	/// <summary>
	/// Returns the cached item, or looks it up on Steam. Returns null if it's unavailable.
	/// Supplying <paramref name="maxCacheAge"/> always queries Steam with that age limit in seconds.
	/// </summary>
	internal static async Task<Item> FetchAsync( ulong workshopItemId, CancellationToken token = default, int? maxCacheAge = null )
	{
		if ( workshopItemId is 0 or ulong.MaxValue ) return null;
		if ( maxCacheAge is null && ItemsById.TryGetValue( workshopItemId, out var item ) ) return item;

		return (await FetchAsync( [workshopItemId], token, maxCacheAge )).FirstOrDefault();
	}

	/// <summary>
	/// Returns known items and fetches missing ones from Steam in batches. Unavailable items are omitted.
	/// Supplying <paramref name="maxCacheAge"/> queries Steam for every requested item with that age limit in seconds.
	/// </summary>
	internal static async Task<Item[]> FetchAsync( IReadOnlyCollection<ulong> workshopItemIds, CancellationToken token, int? maxCacheAge = null )
	{
		var requestedIds = workshopItemIds.Where( workshopItemId => workshopItemId is not (0 or ulong.MaxValue) ).Distinct().ToArray();
		var idsToFetch = maxCacheAge.HasValue ? requestedIds : requestedIds.Where( workshopItemId => !ItemsById.ContainsKey( workshopItemId ) );

		var pages = await Task.WhenAll( idsToFetch.Chunk( 50 ).Select( chunk =>
			RunPageAsync( new { FileIds = chunk, MaxCacheAge = maxCacheAge ?? DetailsCacheAge }, "*", token ) ) );

		if ( maxCacheAge is null )
			return requestedIds.Select( workshopItemId => ItemsById.GetValueOrDefault( workshopItemId ) ).OfType<Item>().ToArray();

		return pages.SelectMany( x => x?.Items ?? [] )
			.Where( x => !string.IsNullOrEmpty( x.Title ) )
			.ToArray();
	}
}
