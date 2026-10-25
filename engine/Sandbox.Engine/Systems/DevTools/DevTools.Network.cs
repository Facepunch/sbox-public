namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// The network tab's traffic log: each message sent or received, and the session's comings and goings, newest last.
	/// Messages are logged while the tab collects (<see cref="IsCollecting"/>) and capture is on; session events always,
	/// they're few. Messages can arrive from a snapshot's encoding worker, so it's locked.
	/// </summary>
	internal static class NetworkLog
	{
		internal enum Kind
		{
			Session,
			SyncVar,
			Rpc,
			Object,
			Snapshot,
		}

		/// <summary>
		/// One message or session event. Peer is who it went to or came from, as shown; "all" for a broadcast.
		/// </summary>
		internal readonly record struct Entry( DateTime Time, Kind Kind, string Name, string Peer, bool Outbound, int Size );

		internal const int Capacity = 10000;

		static readonly Queue<Entry> _events = new();
		static readonly Dictionary<Guid, int> _peerNumbers = new();
		static readonly object _lock = new();
		static bool _inSession;

		/// <summary>
		/// The record button. Off, nothing new is logged.
		/// </summary>
		internal static bool Capturing { get; set; } = true;

		/// <summary>
		/// Goes up with every change, so a view knows when to look again.
		/// </summary>
		internal static int Version { get; private set; }

		internal static int Count
		{
			get { lock ( _lock ) return _events.Count; }
		}

		/// <summary>
		/// Whether messages are being logged, so callers can skip building their names and sizes.
		/// </summary>
		internal static bool LogsMessages => Capturing && IsCollecting( "network" );

		/// <summary>
		/// Bytes logged as sent and received over the last second.
		/// </summary>
		internal static (long Sent, long Received) LastSecond()
		{
			var since = DateTime.UtcNow.AddSeconds( -1 );
			long sent = 0, received = 0;
			lock ( _lock )
			{
				foreach ( var e in _events )
				{
					if ( e.Time < since || e.Kind == Kind.Session ) continue;
					if ( e.Outbound ) sent += e.Size;
					else received += e.Size;
				}
			}

			return (sent, received);
		}

		internal static Entry[] Snapshot()
		{
			lock ( _lock ) return _events.ToArray();
		}

		internal static void Clear()
		{
			lock ( _lock )
			{
				_events.Clear();
				Version++;
			}
		}

		/// <summary>
		/// A message of a known size. A null peer is a broadcast.
		/// </summary>
		internal static void Message( Kind kind, string name, int size, bool outbound, Connection peer )
		{
			if ( !LogsMessages ) return;
			Add( new Entry( DateTime.UtcNow, kind, name, peer is null ? "all" : PeerName( peer ), outbound, size ) );
		}

		/// <summary>
		/// A message to or from a peer named on the main thread (<see cref="PeerName"/>), for a worker to log.
		/// </summary>
		internal static void Message( Kind kind, string name, int size, bool outbound, string peer )
		{
			if ( !Capturing ) return;
			Add( new Entry( DateTime.UtcNow, kind, name, peer, outbound, size ) );
		}

		/// <summary>
		/// A message, measured by serializing it - only while logging.
		/// </summary>
		internal static void Message<T>( Kind kind, string name, T message, bool outbound, Connection peer )
		{
			if ( !LogsMessages ) return;

			var bs = ByteStream.Create( 256 );
			try
			{
				Game.TypeLibrary.ToBytes( message, ref bs );
				Message( kind, name, bs.Length, outbound, peer );
			}
			finally
			{
				bs.Dispose();
			}
		}

		/// <summary>
		/// The session started, a peer came or went, or it ended. Peers are numbered from here, the host 0.
		/// </summary>
		internal static void Session( string what, Connection peer, bool starts = false, bool ends = false )
		{
			if ( ends && !_inSession ) return;

			if ( starts )
			{
				lock ( _lock ) _peerNumbers.Clear();
				_inSession = true;
			}

			if ( Capturing ) Add( new Entry( DateTime.UtcNow, Kind.Session, what, peer is null ? "" : PeerName( peer ), false, 0 ) );
			if ( ends ) _inSession = false;
		}

		static void Add( in Entry e )
		{
			lock ( _lock )
			{
				_events.Enqueue( e );
				while ( _events.Count > Capacity ) _events.Dequeue();
				Version++;
			}
		}

		/// <summary>
		/// A peer as the log shows it: its name, its number in the session (the host 0, the rest as they're first seen),
		/// and whether it's the host or this machine.
		/// </summary>
		internal static string PeerName( Connection c )
		{
			int number;
			lock ( _lock )
			{
				if ( !_peerNumbers.TryGetValue( c.Id, out number ) )
				{
					number = c.IsHost ? 0 : _peerNumbers.Count( x => x.Value > 0 ) + 1;
					_peerNumbers[c.Id] = number;
				}
			}

			var name = string.IsNullOrEmpty( c.DisplayName ) ? c.Name : c.DisplayName;
			var host = c.IsHost ? ", host" : "";
			var local = c == Connection.Local ? ", local" : "";
			return $"{name} (#{number}{host}{local})";
		}
	}
}
