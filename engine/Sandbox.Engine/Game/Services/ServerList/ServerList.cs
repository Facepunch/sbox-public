using Sandbox.Engine;
using System.Text.Json;

namespace Sandbox.Services;

/// <summary>
/// Discovers Steam game servers that match the selected filters.
/// </summary>
public class ServerList : List<ServerList.Entry>, IDisposable, IWeakInteropHandle
{
	SteamNative.ServerList serverlist;

	/// <summary>
	/// Creates a server list filtered to the current network protocol.
	/// </summary>
	public ServerList()
	{
		Sandbox.InteropSystem.AllocWeak( this );

		serverlist = SteamNative.ServerList.Create( this );
		serverlist.AddFilter( "gametagsand", $"protocol:{Protocol.Network}" );
	}

	~ServerList()
	{
		MainThread.QueueDispose( this );
	}

	/// <summary>
	/// Whether Steam is still collecting results for this query.
	/// </summary>
	public bool IsQuerying { get; private set; }
	uint IWeakInteropHandle.InteropHandle { get; set; }

	/// <summary>
	/// Replaces the list with matching internet servers.
	/// </summary>
	public void Query() => StartQuery( lan: false );

	/// <summary>
	/// Servers on the local network instead of the internet's, matched to the same filters
	/// </summary>
	internal void QueryLan() => StartQuery( lan: true );

	void StartQuery( bool lan )
	{
		ObjectDisposedException.ThrowIf( serverlist.IsNull, this );
		Clear();
		IsQuerying = true;
		serverlist.StartQuery( lan );
	}

	/// <summary>
	/// Adds a Steam matchmaking filter to this list.
	/// </summary>
	public void AddFilter( string key, string value )
	{
		serverlist.AddFilter( key, value );
	}

	internal void OnServerResponded( string json )
	{
		var entry = JsonSerializer.Deserialize<Entry>( json );
		var tags = entry.Tags;
		int count = 0;
		foreach ( var tag in tags )
		{
			var separator = tag.IndexOf( ':' );
			if ( separator >= 0 && !tag.AsSpan( separator + 1 ).Contains( ':' ) )
			{
				var value = tag.AsSpan( separator + 1 );
				switch ( tag.AsSpan( 0, separator ) )
				{
					case "game":
						entry.Game = value.ToString().ToLower();
						continue;
					case "gameversion":
						if ( int.TryParse( value, out var version ) )
							entry.GameVersion = version;
						continue;
					case "tick":
						if ( int.TryParse( value, out var tick ) )
							entry.Tick = tick;
						continue;
				}
			}

			tags[count++] = tag;
		}

		Array.Resize( ref tags, count );
		entry.Tags = tags;
		Add( entry );
	}

	internal void OnFinished()
	{
		IsQuerying = false;
	}

	/// <summary>
	/// Stops receiving results and releases the Steam query.
	/// </summary>
	public void Dispose()
	{
		if ( !serverlist.IsNull )
		{
			serverlist.Destroy();
			serverlist = default;
		}

		IsQuerying = false;

		InteropSystem.FreeWeak( this );
		GC.SuppressFinalize( this );
	}

	/// <summary>
	/// A server returned by an internet or LAN query.
	/// </summary>
	public struct Entry
	{
		public string IPAddressAndPort { get; set; }
		public ulong SteamId { get; set; }
		public string Map { get; set; }
		public string Game { get; set; }
		public int GameVersion { get; set; }
		public string Name { get; set; }
		public string[] Tags { get; set; }
		public int Players { get; set; }
		public int MaxPlayers { get; set; }
		public int Ping { get; set; }
		public int Tick { get; set; }
		public bool HasPassword { get; set; }
	}
}
