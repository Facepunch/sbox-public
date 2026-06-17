using System.Threading;

namespace Steamworks.Data
{
	internal struct LobbyQuery
	{
		// TODO FILTERS
		// AddRequestLobbyListStringFilter
		// - WithoutKeyValue

		#region Distance Filter
		internal LobbyDistanceFilter? distance;

		/// <summary>
		/// only lobbies in the same immediate region will be returned
		/// </summary>
		internal LobbyQuery FilterDistanceClose()
		{
			distance = LobbyDistanceFilter.Close;
			return this;
		}

		/// <summary>
		/// only lobbies in the same immediate region will be returned
		/// </summary>
		internal LobbyQuery FilterDistanceFar()
		{
			distance = LobbyDistanceFilter.Far;
			return this;
		}

		/// <summary>
		/// only lobbies in the same immediate region will be returned
		/// </summary>
		internal LobbyQuery FilterDistanceWorldwide()
		{
			distance = LobbyDistanceFilter.Worldwide;
			return this;
		}
		#endregion

		#region String key/value filter
		internal Dictionary<string, string> stringFilters;

		/// <summary>
		/// Filter by specified key/value pair; string parameters
		/// </summary>
		internal LobbyQuery WithKeyValue( string key, string value )
		{
			if ( string.IsNullOrEmpty( key ) )
				throw new System.ArgumentException( "Key string provided for LobbyQuery filter is null or empty", nameof( key ) );

			if ( key.Length > SteamMatchmaking.MaxLobbyKeyLength )
				throw new System.ArgumentException( $"Key length is longer than {SteamMatchmaking.MaxLobbyKeyLength}", nameof( key ) );

			stringFilters ??= new Dictionary<string, string>();

			stringFilters.Add( key, value );

			return this;
		}
		#endregion

		#region Numerical filters
		internal List<NumericalFilter> numericalFilters;

		/// <summary>
		/// Numerical filter where value is less than the value provided
		/// </summary>
		internal LobbyQuery WithLower( string key, int value )
		{
			AddNumericalFilter( key, value, LobbyComparison.LessThan );
			return this;
		}

		/// <summary>
		/// Numerical filter where value is greater than the value provided
		/// </summary>
		internal LobbyQuery WithHigher( string key, int value )
		{
			AddNumericalFilter( key, value, LobbyComparison.GreaterThan );
			return this;
		}

		/// <summary>
		/// Numerical filter where value must be equal to the value provided
		/// </summary>
		internal LobbyQuery WithEqual( string key, int value )
		{
			AddNumericalFilter( key, value, LobbyComparison.Equal );
			return this;
		}

		/// <summary>
		/// Numerical filter where value must not equal the value provided
		/// </summary>
		internal LobbyQuery WithNotEqual( string key, int value )
		{
			AddNumericalFilter( key, value, LobbyComparison.NotEqual );
			return this;
		}

		/// <summary>
		/// Test key, initialize numerical filter list if necessary, then add new numerical filter
		/// </summary>
		internal void AddNumericalFilter( string key, int value, LobbyComparison compare )
		{
			if ( string.IsNullOrEmpty( key ) )
				throw new System.ArgumentException( "Key string provided for LobbyQuery filter is null or empty", nameof( key ) );

			if ( key.Length > SteamMatchmaking.MaxLobbyKeyLength )
				throw new System.ArgumentException( $"Key length is longer than {SteamMatchmaking.MaxLobbyKeyLength}", nameof( key ) );

			if ( numericalFilters == null )
				numericalFilters = new List<NumericalFilter>();

			numericalFilters.Add( new NumericalFilter( key, value, compare ) );
		}
		#endregion

		#region Near value filter
		internal Dictionary<string, int> nearValFilters;

		/// <summary>
		/// Order filtered results according to key/values nearest the provided key/value pair.
		/// Can specify multiple near value filters; each successive filter is lower priority than the previous.
		/// </summary>
		internal LobbyQuery OrderByNear( string key, int value )
		{
			if ( string.IsNullOrEmpty( key ) )
				throw new System.ArgumentException( "Key string provided for LobbyQuery filter is null or empty", nameof( key ) );

			if ( key.Length > SteamMatchmaking.MaxLobbyKeyLength )
				throw new System.ArgumentException( $"Key length is longer than {SteamMatchmaking.MaxLobbyKeyLength}", nameof( key ) );

			if ( nearValFilters == null )
				nearValFilters = new Dictionary<string, int>();

			nearValFilters.Add( key, value );

			return this;
		}
		#endregion

		#region Slots Filter
		internal int? slotsAvailable;

		/// <summary>
		/// returns only lobbies with the specified number of slots available
		/// </summary>
		internal LobbyQuery WithSlotsAvailable( int minSlots )
		{
			slotsAvailable = minSlots;
			return this;
		}

		#endregion

		#region Max results filter
		internal int? maxResults;

		/// <summary>
		/// sets how many results to return, the lower the count the faster it is to download the lobby results
		/// </summary>
		internal LobbyQuery WithMaxResults( int max )
		{
			maxResults = max;
			return this;
		}

		#endregion

		void ApplyFilters()
		{
			if ( distance.HasValue )
			{
				SteamMatchmaking.Internal.AddRequestLobbyListDistanceFilter( distance.Value );
			}

			if ( slotsAvailable.HasValue )
			{
				SteamMatchmaking.Internal.AddRequestLobbyListFilterSlotsAvailable( slotsAvailable.Value );
			}

			if ( maxResults.HasValue )
			{
				SteamMatchmaking.Internal.AddRequestLobbyListResultCountFilter( maxResults.Value );
			}

			if ( stringFilters != null )
			{
				foreach ( var k in stringFilters )
				{
					SteamMatchmaking.Internal.AddRequestLobbyListStringFilter( k.Key, k.Value, LobbyComparison.Equal );
				}
			}

			if ( numericalFilters != null )
			{
				foreach ( var n in numericalFilters )
				{
					SteamMatchmaking.Internal.AddRequestLobbyListNumericalFilter( n.Key, n.Value, n.Comparer );
				}
			}

			if ( nearValFilters != null )
			{
				foreach ( var v in nearValFilters )
				{
					SteamMatchmaking.Internal.AddRequestLobbyListNearValueFilter( v.Key, v.Value );
				}
			}
		}

		//
		// Only one lobby query can run at a time. If we run another while 
		// one is running, the last query will be discarded and the async will never
		// complete!
		//
		static SemaphoreSlim lobbyQuerySemaphore = new SemaphoreSlim( 1 );

		/// <summary>
		/// Run the query, get the matching lobbies
		/// </summary>
		internal async Task<Lobby[]> RequestAsync( CancellationToken ct )
		{
			if ( SteamMatchmaking.Internal == null )
				return [];

			using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
			cts.CancelAfter( TimeSpan.FromSeconds( 30 ) );
			ct = cts.Token;

			try
			{
				await lobbyQuerySemaphore.WaitAsync();
				ApplyFilters();

				var task = SteamMatchmaking.Internal.RequestLobbyList();
				while ( !task.IsCompleted )
				{
					await Task.Delay( 100, ct );
				}

				LobbyMatchList_t? list = task.GetResult();
				if ( !list.HasValue || list.Value.LobbiesMatching == 0 )
					return [];

				Lobby[] lobbies = new Lobby[list.Value.LobbiesMatching];

				for ( int i = 0; i < list.Value.LobbiesMatching; i++ )
				{
					lobbies[i] = new Lobby { Id = SteamMatchmaking.Internal.GetLobbyByIndex( i ) };
				}

				return lobbies;

			}
			finally
			{
				lobbyQuerySemaphore.Release();
			}
		}
	}
}
