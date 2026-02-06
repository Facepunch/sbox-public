using System.Text.Json;
using System.Timers;

namespace Sandbox;

internal struct CookieItem
{
	public string Value { get; set; }
	public long Timeout { get; set; }

	/// <summary>
	/// If set to non 0, this key will be deleted at/after given time.
	/// This is useful in case you didn't open your game for more than 30 days (which is the default expiry time),
	/// and then you lose all your cookies because they all are expired and get deleted on launch.
	///
	/// This way you have a 24 hour grace period.
	/// </summary>
	public long DeleteAt { get; set; }
}

public sealed class CookieContainer
{
	private Dictionary<string, CookieItem> CookieCache;
	private Timer Timer;
	private string Name;
	private BaseFileSystem FileSystem;
	private bool NoExpiration;

	internal CookieContainer( string name, bool noexpire = false, BaseFileSystem fileSystem = null )
	{
		Name = name;
		FileSystem = fileSystem ?? EngineFileSystem.Config;
		NoExpiration = noexpire;
		Load();
	}

	/// <summary>
	/// Not public, not IDisposable. Don't want people to be able to do this from game.
	/// </summary>
	internal void Dispose()
	{
		StopTimer();
		Save();
	}

	/// <summary>
	/// Set a cookie to be stored between sessions. The cookie will expire one month
	/// from when it was set.
	/// </summary>
	public void SetString( string key, string value )
	{
		if ( CookieCache == null ) return;

		CookieCache[key] = new CookieItem
		{
			Value = value,
			Timeout = NoExpiration ? -1 : DateTimeOffset.Now.AddDays( 30 ).ToUnixTimeSeconds(),
			DeleteAt = 0
		};
	}

	/// <summary>
	/// Get a stored session cookie.
	/// </summary>
	public string GetString( string key, string fallback = "" )
	{
		if ( CookieCache == null ) return fallback;

		if ( CookieCache.TryGetValue( key, out CookieItem item ) )
		{
			MarkUsed( key );

			return item.Value;
		}
		else
		{
			return fallback;
		}
	}

	/// <summary>
	/// Get a stored session cookie.
	/// </summary>
	public bool TryGetString( string key, out string val )
	{
		val = default;
		if ( CookieCache == null ) return false;

		if ( CookieCache.TryGetValue( key, out CookieItem item ) )
		{
			MarkUsed( key );

			val = item.Value;
			return true;
		}

		return false;
	}

	public bool TryGet<T>( string key, out T val )
	{
		val = default;
		if ( CookieCache == null ) return false;

		var json = GetString( key, null );
		if ( json == null ) return false;

		try
		{
			val = JsonSerializer.Deserialize<T>( json );
			return true;
		}
		catch ( Exception )
		{
			return false;
		}
	}

	/// <summary>
	/// Load JSON encodable data from cookies
	/// </summary>
	public T Get<T>( string key, T fallback )
	{
		if ( CookieCache == null ) return fallback;

		var json = GetString( key, null );
		if ( json == null ) return fallback;

		try
		{
			return JsonSerializer.Deserialize<T>( json );
		}
		catch ( Exception )
		{
			return fallback;
		}
	}

	/// <summary>
	/// Set JSON encodable object to data
	/// </summary>
	public void Set<T>( string key, T value )
	{
		if ( CookieCache == null ) return;
		SetString( key, JsonSerializer.Serialize( value ) );
	}

	/// <summary>
	/// Removes a cookie from the cache entirely
	/// </summary>
	/// <param name="key"></param>
	public void Remove( string key )
	{
		CookieCache.Remove( key );
	}

	void MarkUsed( string key )
	{
		if ( NoExpiration ) return;

		if ( CookieCache.TryGetValue( key, out CookieItem item ) )
		{
			item.Timeout = DateTimeOffset.Now.AddDays( 30 ).ToUnixTimeSeconds();
			item.DeleteAt = 0;
			CookieCache[key] = item;
		}
	}

	private void ClearExpired()
	{
		if ( CookieCache == null ) return;
		if ( NoExpiration ) return;

		var expiredKeys = new List<string>();
		var unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

		foreach ( var kv in CookieCache )
		{
			var expired = unixTime >= kv.Value.Timeout;
			var remove = kv.Value.DeleteAt != 0 && unixTime >= kv.Value.DeleteAt;
			if ( expired && remove )
			{
				expiredKeys.Add( kv.Key );
			}
			else if ( expired && kv.Value.DeleteAt == 0 )
			{
				var item = kv.Value;
				item.DeleteAt = DateTimeOffset.Now.AddDays( 1 ).ToUnixTimeSeconds();
				CookieCache[kv.Key] = item;
			}
		}

		foreach ( var key in expiredKeys )
		{
			CookieCache.Remove( key );
		}

		if ( expiredKeys.Count > 0 )
		{
			Log.Info( $"Clearing {expiredKeys.Count} expired Cookies" );
		}
	}

	private void StartTimer()
	{
		if ( Timer != null ) return;

		Timer = new Timer();
		Timer.Elapsed += new ElapsedEventHandler( OnTimerElapsed );
		Timer.Interval = 1000 * 60 * 1;
		Timer.AutoReset = false; // we will restart manually, otherwise saves can overlap
		Timer.Start();
	}

	internal void StopTimer()
	{
		if ( Timer == null ) return;

		Timer.Dispose();
		Timer = null;
	}

	private void OnTimerElapsed( object sender, ElapsedEventArgs e )
	{
		MainThread.Queue( () =>
		{
			try
			{
				Save();
			}
			finally
			{
				// reenable for next interval
				Timer?.Start();
			}
		} );
	}

	private void Load()
	{
		if ( CookieCache == null )
		{
			var fn = $"{Name}.json.backup";
			CookieCache = FileSystem?.ReadJsonOrDefault<Dictionary<string, CookieItem>>( fn, null );
			if ( CookieCache is not null )
			{
				Log.Warning( $"Restored {Name} cookies from backup {fn}!" );
				Save();
			}
		}

		if ( CookieCache == null )
		{
			var fn = $"{Name}.json";

			CookieCache = FileSystem?.ReadJsonOrDefault<Dictionary<string, CookieItem>>( fn, null );
		}

		CookieCache ??= new();

		ClearExpired();
		StartTimer();
	}

	internal void Save()
	{
		if ( CookieCache == null ) return;

		var fnorig = $"{Name}.json";
		var fnback = $"{Name}.json.backup";

		// Before doing anything, copy the old file as backup
		if ( FileSystem.FileExists( fnorig ) )
		{
			var data = FileSystem.ReadAllText( fnorig );

			try
			{
				FileSystem.WriteAllText( fnback, data );
			}
			catch ( System.IO.IOException e )
			{
				Log.Warning( $"Couldn't create cookie backup - {e.Message}" );
			}
		}

		if ( Name.Contains( "/" ) )
		{
			FileSystem.CreateDirectory( System.IO.Path.GetDirectoryName( Name ) );
		}

		ClearExpired();

		try
		{
			if ( CookieCache.Count == 0 )
			{
				FileSystem.DeleteFile( fnorig );
			}
			else
			{
				FileSystem.WriteAllText( fnorig, JsonSerializer.Serialize( CookieCache, new JsonSerializerOptions( JsonSerializerOptions.Default ) { WriteIndented = true } ) );
			}

			// that succeeded, so we can delete any backups
			{
				FileSystem.DeleteFile( fnback );
			}
		}
		catch ( System.IO.IOException e )
		{
			Log.Warning( e, "IO error when saving cookies!" );
		}
	}
}
