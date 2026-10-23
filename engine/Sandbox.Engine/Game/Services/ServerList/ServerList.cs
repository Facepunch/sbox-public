using Sandbox.Engine;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Services;

public class ServerList : List<ServerList.Entry>, IDisposable, IWeakInteropHandle
{
	CServerList serverlist;

	public ServerList()
	{
		Sandbox.InteropSystem.AllocWeak( this );

		serverlist = CServerList.Create( this );
		serverlist.AddFilter( "gametagsand", $"protocol:{Protocol.Network}" );
	}

	~ServerList()
	{
		Dispose();
	}

	public bool IsQuerying { get; private set; }
	uint IWeakInteropHandle.InteropHandle { get; set; }

	public void Query()
	{
		serverlist.StartQuery();
	}

	public void AddFilter( string key, string value )
	{
		serverlist.AddFilter( key, value );
	}

	internal unsafe void OnServerResponded( IntPtr ptr, ulong steamid )
	{
		var item = Unsafe.Read<gameserveritem_t>( (void*)ptr );
		var tags = Marshal.PtrToStringUTF8( (IntPtr)item.m_szGameTags );
		var ipAddress = new IPAddress( BitConverter.GetBytes( IPAddress.HostToNetworkOrder( (int)item.m_unIP ) ) ).ToString();

		var entry = new Entry
		{
			IPAddressAndPort = $"{ipAddress}:{item.m_usConnectionPort}",
			SteamId = steamid,
			Map = Marshal.PtrToStringUTF8( (IntPtr)item.m_szMap ),
			Name = Marshal.PtrToStringUTF8( (IntPtr)item.m_szServerName ),
			Players = item.m_nPlayers,
			MaxPlayers = item.m_nMaxPlayers - item.m_nBotPlayers,
			Ping = item.m_nPing,
			Game = Marshal.PtrToStringUTF8( (IntPtr)item.m_szGameDescription ),
			HasPassword = item.m_bPassword != 0,
			Tick = 0
		};

		var tagParts = tags.Split( "," ).ToList();

		foreach ( var tag in tagParts.ToArray() )
		{
			if ( !tag.Contains( ':' ) ) continue;
			var parts = tag.Split( ':' );
			if ( parts.Length != 2 ) continue;

			switch ( parts[0] )
			{
				case "game":
					entry.Game = parts[1].ToLower();
					break;

				case "gameversion":
					if ( int.TryParse( parts[1], out var x ) )
						entry.GameVersion = x;
					break;

				case "tick":
					if ( int.TryParse( parts[1], out var i ) )
						entry.Tick = i;
					break;

				default:
					continue;

			}

			tagParts.Remove( tag );
		}

		entry.Tags = tagParts.ToArray();

		Add( entry );
	}

	internal void OnFinished()
	{
		IsQuerying = false;
	}

	internal void OnStarted()
	{
		Clear();
		IsQuerying = true;
	}

	public void Dispose()
	{
		serverlist.Destroy();
		serverlist = default;

		InteropSystem.Free( this );
		GC.SuppressFinalize( this );
	}

	/// <summary>
	/// This is a cleaned up version of gameserveritem_t.
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


/// <summary>
/// Data from c++
/// We skip the steamid because there's some class padding fuckery going on.
/// We pass that in manually to make life easier.
/// </summary>
internal unsafe struct gameserveritem_t
{
#pragma warning disable 0649
	public ushort m_usConnectionPort;
	public ushort m_usQueryPort;
	public uint m_unIP;
	public int m_nPing;

	public byte m_bHadSuccessfulResponse;
	public byte m_bDoNotRefresh;
	public fixed byte m_szGameDir[32];
	public fixed byte m_szMap[32];
	public fixed byte m_szGameDescription[64];
	public uint m_nAppID;
	public int m_nPlayers;
	public int m_nMaxPlayers;
	public int m_nBotPlayers;
	public byte m_bPassword;
	public byte m_bSecure;
	public uint m_ulTimeLastPlayed;
	public int m_nServerVersion;
	public fixed byte m_szServerName[64];
	public fixed byte m_szGameTags[128];
#pragma warning restore 0649
};
