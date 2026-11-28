using System.Collections;

namespace Sandbox.Services;

public static partial class Stats
{
	/// <summary>
	/// Static cache of all the global stats
	/// </summary>
	static Dictionary<PlayerKey, PlayerStats> _playerStats = new();

	/// <summary>A package (any case) and a player: looked up without making a string, as achievements do every second.</summary>
	readonly struct PlayerKey( string package, long steamId ) : IEquatable<PlayerKey>
	{
		readonly string package = package ?? "";
		readonly long steamId = steamId;

		public bool Equals( PlayerKey other ) => steamId == other.steamId && string.Equals( package, other.package, StringComparison.OrdinalIgnoreCase );
		public override bool Equals( object obj ) => obj is PlayerKey other && Equals( other );
		public override int GetHashCode() => HashCode.Combine( StringComparer.OrdinalIgnoreCase.GetHashCode( package ), steamId );
	}

	/// <summary>
	/// Get the global stats for the calling package
	/// </summary>
	public static PlayerStats LocalPlayer
	{
		get
		{
			var package = Application.GameIdent;
			if ( package is null ) return default;

			return GetLocalPlayerStats( package );
		}
	}

	/// <summary>
	/// Get the global stats for this package
	/// </summary>
	public static PlayerStats GetLocalPlayerStats( string packageIdent ) => GetPlayerStats( packageIdent, (long)Sandbox.SteamApi.SteamClient.SteamId.Value );

	/// <summary>
	/// Get the stats for this package
	/// </summary>
	public static PlayerStats GetPlayerStats( string packageIdent, long steamid )
	{
		var key = new PlayerKey( packageIdent, steamid );

		if ( _playerStats.TryGetValue( key, out var s ) )
			return s;

		s = new PlayerStats( packageIdent, steamid );
		_ = s.Refresh();
		_playerStats[key] = s;
		return s;
	}

	public sealed class PlayerStats : IEnumerable<PlayerStat>
	{
		private string _packageIdent;
		private long _steamId;
		private Dictionary<string, PlayerStat> stats = new Dictionary<string, PlayerStat>( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// True if we're currently fetching new stats
		/// </summary>
		public bool IsRefreshing { get; private set; }

		/// <summary>
		/// The UTC datetime when we last fetched new stats
		/// </summary>
		public DateTime LastRefresh { get; private set; }


		internal PlayerStats( string packageIdent, long steamId )
		{
			_packageIdent = packageIdent;
			_steamId = steamId;
		}

		/// <summary>
		/// Make a copy of this class. Allows you to store the stats from a point in time.
		/// </summary>
		public PlayerStats Copy()
		{
			var pc = new PlayerStats( _packageIdent, _steamId );
			pc.LastRefresh = LastRefresh;

			foreach ( var (k, v) in stats )
			{
				pc.stats.Add( k, v );
			}

			return pc;
		}

		/// <summary>
		/// Get a stat by name. Will return an empty stat if not found
		/// </summary>
		public PlayerStat Get( string name )
		{
			if ( TryGet( name, out var stat ) )
				return stat;

			return default;
		}

		/// <summary>
		/// Get a stat by name, returns true if found
		/// </summary>
		public bool TryGet( string name, out PlayerStat stat )
		{
			return stats.TryGetValue( name, out stat );
		}

		/// <summary>
		/// Refresh these global stats - grab the latest values
		/// </summary>
		public async Task Refresh()
		{
			if ( Backend.Stats is null )
				return;

			// if we're refreshing, wait for it to finish and return
			while ( IsRefreshing )
			{
				while ( IsRefreshing )
					await Task.Delay( 100 );

				return;
			}

			// Don't allow refreshing so often
			if ( LastRefresh > DateTime.UtcNow.AddSeconds( -10 ) )
				return;

			if ( _packageIdent.StartsWith( "local." ) )
				return;

			try
			{
				IsRefreshing = true;

				var stats = await Backend.Stats.GetPlayerPackageStats( _packageIdent, _steamId );
				if ( stats is null ) return;

				this.stats.Clear();

				foreach ( var entry in stats )
				{
					this.stats[entry.Name] = new PlayerStat( entry );
				}

				LastRefresh = DateTime.UtcNow;
			}
			catch ( System.Exception e )
			{
				if ( !Application.IsEditor )
				{
					Log.Warning( e, "Exception when fetching player stats" );
				}
			}
			finally
			{
				IsRefreshing = false;
			}
		}

		public IEnumerator<PlayerStat> GetEnumerator() => stats.Values.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => stats.Values.GetEnumerator();

		/// <summary>
		/// When the stat is queued for change on the backend, we can predict its change on the front
		/// end, for achievements etc.
		/// </summary>
		internal void Predict( string name, double amount )
		{
			var stat = Get( name );

			stat = new PlayerStat
			{
				Name = name,
				Title = stat.Title,
				Description = stat.Description,
				Unit = stat.Unit,
				Value = stat.Value,
				ValueString = stat.ValueString,
				Max = Math.Max( stat.Max, amount ),
				Min = Math.Min( stat.Min, amount ),
				Sum = stat.Sum + amount,
				Last = DateTime.UtcNow,
				LastValue = amount,
				First = stat.First,
				FirstValue = stat.FirstValue
			};

			stats[name] = stat;
		}

		public PlayerStat this[string propertyName]
		{
			get => Get( propertyName );
		}
	}

	[Expose]
	public readonly struct PlayerStat
	{
		internal PlayerStat( Sandbox.Services.PlayerStat entry )
		{
			Name = entry.Name;
			Title = entry.Title;
			Description = entry.Description;
			Unit = entry.Unit;
			Value = entry.Value;
			ValueString = entry.ValueString;
			Max = entry.Max;
			Avg = entry.Avg;
			Min = entry.Min;
			Sum = entry.Sum;
			Last = entry.Last;
			LastValue = entry.LastValue;
			First = entry.First;
			FirstValue = entry.FirstValue;
		}

		/// <summary>
		/// The programatic name for this stat. This should probably be called Ident
		/// </summary>
		public string Name { get; init; }

		/// <summary>
		/// The title of this stat, as defined on the backend
		/// </summary>
		public string Title { get; init; }

		/// <summary>
		/// The description of this stat, as defined on the backend
		/// </summary>
		public string Description { get; init; }

		/// <summary>
		/// The unit of this stat as defined on the backend
		/// </summary>
		public string Unit { get; init; }

		/// <summary>
		/// The current stat value
		/// </summary>
		public double Value { get; init; }

		/// <summary>
		/// The current value formatted using Unit
		/// </summary>
		public string ValueString { get; init; }

		/// <summary>
		/// The maximum value
		/// </summary>
		public double Max { get; init; }

		/// <summary>
		/// The minimum value
		/// </summary>
		public double Min { get; init; }

		/// <summary>
		/// The average value
		/// </summary>
		public double Avg { get; init; }

		/// <summary>
		/// The sum of all values
		/// </summary>
		public double Sum { get; init; }

		/// <summary>
		/// The time of the last value
		/// </summary>
		public DateTimeOffset Last { get; init; }

		/// <summary>
		/// The last value
		/// </summary>
		public double LastValue { get; init; }

		/// <summary>
		/// The time of the first value
		/// </summary>
		public DateTimeOffset First { get; init; }


		/// <summary>
		/// The last value
		/// </summary>
		public double FirstValue { get; init; }

		internal readonly double GetValue( AggregationType type )
		{
			return type switch
			{
				AggregationType.Highest => Max,
				AggregationType.Lowest => Min,
				AggregationType.Latest => LastValue,
				AggregationType.Median => Avg,
				_ => Sum,
			};
		}
	}
}
