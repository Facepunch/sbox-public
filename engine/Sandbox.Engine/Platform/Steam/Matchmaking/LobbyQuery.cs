using System.Threading;

namespace Sandbox.SteamApi
{
	internal struct LobbyQuery
	{
		// Steam shares the next request's filters and the last request's results across all callers.
		static readonly SemaphoreSlim lobbyQuerySemaphore = new( 1 );

		int? slotsAvailable;
		int? maxResults;
		Dictionary<string, string> stringFilters;

		/// <summary>
		/// Require a lobby metadata key to match the given value.
		/// </summary>
		internal LobbyQuery WithKeyValue( string key, string value )
		{
			if ( string.IsNullOrEmpty( key ) )
				throw new System.ArgumentException( "Key string provided for LobbyQuery filter is null or empty", nameof( key ) );

			if ( key.Length > SteamMatchmaking.MaxLobbyKeyLength )
				throw new System.ArgumentException( $"Key length is longer than {SteamMatchmaking.MaxLobbyKeyLength}", nameof( key ) );

			stringFilters ??= new();
			stringFilters.Add( key, value );

			return this;
		}

		/// <summary>
		/// Require at least this many open slots.
		/// </summary>
		internal LobbyQuery WithSlotsAvailable( int minSlots )
		{
			slotsAvailable = minSlots;
			return this;
		}

		/// <summary>
		/// Limit how many matching lobbies Steam returns.
		/// </summary>
		internal LobbyQuery WithMaxResults( int max )
		{
			maxResults = max;
			return this;
		}

		/// <summary>
		/// Run the query and return the matching lobbies.
		/// </summary>
		internal async Task<Lobby[]> RequestAsync( CancellationToken ct )
		{
			if ( !SteamMatchmaking.IsAvailable )
				return [];

			using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
			cts.CancelAfter( TimeSpan.FromSeconds( 30 ) );
			ct = cts.Token;

			await lobbyQuerySemaphore.WaitAsync( ct );
			try
			{
				if ( !SteamMatchmaking.IsAvailable )
					return [];

				var filters = stringFilters is null ? null : System.Text.Json.JsonSerializer.Serialize( stringFilters );
				var call = SteamNative.Matchmaking.RequestLobbyList( slotsAvailable ?? -1, maxResults ?? -1, filters );
				return await SteamCalls.GetResultAsync( call, ReadLobbyList, ct, fallback: [] );
			}
			finally
			{
				lobbyQuerySemaphore.Release();
			}
		}

		static Lobby[] ReadLobbyList( ulong call )
		{
			using var result = SteamNative.Matchmaking.GetLobbyListResult( call );
			var ids = result.AsSpan<ulong>();
			if ( ids.IsEmpty ) return [];

			var lobbies = new Lobby[ids.Length];
			for ( int i = 0; i < ids.Length; ++i )
				lobbies[i] = new Lobby( ids[i] );

			return lobbies;
		}
	}
}
