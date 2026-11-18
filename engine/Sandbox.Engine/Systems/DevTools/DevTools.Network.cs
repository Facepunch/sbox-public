namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// Dormancy of an object this machine sends.
	/// </summary>
	internal enum NetDormancy : byte
	{
		None,
		Awake,
		Grace,
		AsleepIdle,
		AsleepHidden,
	}

	internal static bool IsAsleep( this NetDormancy state ) => state is NetDormancy.AsleepIdle or NetDormancy.AsleepHidden;

	/// <summary>
	/// The network tab's traffic log. Messages can arrive from a snapshot's encoding worker, so it's locked.
	/// </summary>
	internal static partial class NetworkLog
	{
		internal enum Kind
		{
			Session,
			SyncVar,
			Rpc,
			Object,
			Snapshot,
			Dormancy,
			Transform,
		}

		[Flags]
		internal enum TransformParts
		{
			None = 0,
			Position = 1,
			Rotation = 2,
			Scale = 4,
		}

		/// <summary>
		/// One message or state change. Objects are stored by id and name so the log doesn't keep them alive.
		/// </summary>
		internal readonly record struct Entry( DateTime Time, Kind Kind, string Name, string Peer, bool Outbound, int Size,
			Guid ObjectId, string ObjectName, string Value = null, bool Resend = false, NetDormancy Dormancy = NetDormancy.None )
		{
			public double Seconds => (Time - Epoch).TotalSeconds;
		}

		internal const int Capacity = 20000;

		/// <summary>
		/// Lane for snapshots, object messages and session events.
		/// </summary>
		internal static readonly Guid SessionLane = new( "5e5510a0-0000-4000-8000-000000000000" );

		readonly record struct Peer( int Number, string Name, bool Host, bool Local, string Label );
		readonly record struct WireKey( Guid Owner, string Name, bool Outbound );
		readonly record struct DormancyRecord( Entry Entry, long Sequence );
		readonly record struct DormancyChange( Guid Id, string Name, NetDormancy State, NetDormancy Previous );

		const string SessionLaneName = "Session";
		const string SessionEventsName = "Events";
		const string DormancyName = "Dormancy";
		const string TransformName = "Transform";
		const int ValueLength = 48;
		const int HistoryCapacity = 8192;
		const int MaxNamedRecipients = 3;

		static readonly DateTime Epoch = DateTime.UtcNow;
		static Queue<Entry> _events;
		static readonly Dictionary<Guid, Peer> _peers = new();
		static readonly Dictionary<WireKey, int> _lastWire = new();
		static readonly Dictionary<Guid, DormancyRecord> _dormancy = new();
		static readonly Dictionary<Guid, DateTime> _lastActivity = new();
		static readonly HashSet<Guid> _dormancySeen = new();
		static readonly List<Guid> _dormancyGone = new();
		static readonly List<DormancyChange> _dormancyChanged = new();
		static readonly object _lock = new();
		static DateTime _lastDormancySample;
		static DateTime _newest;
		static bool _inSession;

		/// <summary>
		/// The record button. Off, nothing new is logged.
		/// </summary>
		internal static bool Capturing { get; set; } = true;

		internal static int Clears { get; private set; }

		/// <summary>
		/// Entries dropped since the last clear because the log was full.
		/// </summary>
		internal static int Dropped { get; private set; }

		/// <summary>
		/// Entries logged so far. Never reset.
		/// </summary>
		internal static long Added { get; private set; }

		internal static DateTime? Oldest
		{
			get
			{
				lock ( _lock ) return _events is not null && _events.TryPeek( out var e ) ? e.Time : null;
			}
		}

		internal static DateTime SessionEnded { get; private set; }

		internal static double Now => (DateTime.UtcNow - Epoch).TotalSeconds;

		/// <summary>
		/// Whether messages are being logged, so callers can skip building their names and sizes.
		/// </summary>
		internal static bool LogsMessages => Capturing && IsCollecting( "network" );

		/// <summary>
		/// The Network tab's transform toggle. Off by default since every moving object adds one a tick.
		/// </summary>
		internal static bool CaptureTransforms { get; set; }

		/// <summary>
		/// Tools recording transforms without flipping <see cref="CaptureTransforms"/>.
		/// </summary>
		internal static int TransformRequests { get; set; }

		internal static bool LogsTransforms => (CaptureTransforms || TransformRequests > 0) && LogsMessages;

		/// <summary>
		/// Copies entries since <paramref name="since"/>, plus current dormancy states that have already left the queue.
		/// Returns <see cref="Added"/> for <see cref="CopyAdded"/> to carry on from.
		/// </summary>
		internal static long CopyTimeline( List<Entry> into, DateTime since )
		{
			into.Clear();
			lock ( _lock )
			{
				if ( _events is null ) return Added;

				var lastDropped = Added - _events.Count;
				foreach ( var (e, sequence) in _dormancy.Values )
				{
					if ( sequence <= lastDropped && e.Time >= since ) into.Add( e );
				}

				into.Sort( static ( a, b ) => a.Time.CompareTo( b.Time ) );

				foreach ( var e in _events )
				{
					if ( e.Time >= since ) into.Add( e );
				}

				return Added;
			}
		}

		/// <summary>
		/// Appends entries added since <paramref name="copied"/>. False if some were already dropped.
		/// </summary>
		internal static bool CopyAdded( List<Entry> into, ref long copied )
		{
			lock ( _lock )
			{
				var fresh = Added - copied;
				if ( fresh == 0 ) return true;
				if ( _events is null || fresh > _events.Count ) return false;

				var skip = _events.Count - fresh;
				foreach ( var e in _events )
				{
					if ( skip-- > 0 ) continue;
					into.Add( e );
				}

				copied = Added;
				return true;
			}
		}

		internal static void Clear()
		{
			lock ( _lock )
			{
				_events?.Clear();
				ForgetObjectsLocked();
				Dropped = 0;
				Clears++;
			}
		}

		static void ForgetObjectsLocked()
		{
			_lastWire.Clear();
			_dormancy.Clear();
			_lastActivity.Clear();
			_lastDormancySample = default;
		}

		/// <summary>
		/// A message of a known size. A null peer is a broadcast.
		/// </summary>
		internal static void Message( Kind kind, string name, int size, bool outbound, Connection peer )
		{
			if ( !LogsMessages ) return;
			Add( new Entry( DateTime.UtcNow, kind, name, MessagePeerName( peer ), outbound, size, SessionLane, SessionLaneName ) );
		}

		/// <summary>
		/// A message to or from a peer named on the main thread (<see cref="PeerName"/>), for a worker to log.
		/// </summary>
		internal static void Message( Kind kind, string name, int size, bool outbound, string peer )
		{
			if ( !Capturing ) return;
			Add( new Entry( DateTime.UtcNow, kind, name, peer, outbound, size, SessionLane, SessionLaneName ) );
		}

		/// <summary>
		/// A sync var update. Repeated wire bytes are marked as a resend, except for <see cref="INetworkSerializer"/>
		/// types, which send deltas where the same bytes can be a different change.
		/// </summary>
		internal static void SyncVar( Network.NetworkTable.Entry entry, ReadOnlySpan<byte> wire, bool outbound, Connection peer )
		{
			if ( !LogsMessages ) return;

			var (id, objectName) = DescribeOwner( entry.Owner );
			var value = FormatValue( entry.GetValue );

			var key = new WireKey( entry.Owner is Component component ? component.Id : id, entry.DebugName, outbound );
			int? wireHash = null;
			if ( !entry.IsSerializerType )
			{
				var hash = new HashCode();
				hash.AddBytes( wire );
				wireHash = hash.ToHashCode();
			}

			var peerName = MessagePeerName( peer );
			var now = DateTime.UtcNow;

			lock ( _lock )
			{
				var resend = wireHash is { } current && _lastWire.TryGetValue( key, out var last ) && last == current;
				if ( wireHash is { } h )
				{
					if ( _lastWire.Count >= HistoryCapacity && !_lastWire.ContainsKey( key ) ) _lastWire.Clear();
					_lastWire[key] = h;
				}

				if ( !resend && id != Guid.Empty )
				{
					if ( _lastActivity.Count >= HistoryCapacity && !_lastActivity.ContainsKey( id ) ) _lastActivity.Clear();
					_lastActivity[id] = now;
				}

				AddLocked( new Entry( now, Kind.SyncVar, entry.DebugName, peerName, outbound, wire.Length, id, objectName, value, resend ) );
			}
		}

		/// <summary>
		/// An RPC and its arguments. A null peer is a broadcast.
		/// </summary>
		internal static void Rpc( string name, int size, bool outbound, Connection peer, object owner, string args )
		{
			if ( !LogsMessages ) return;
			AddRpc( name, size, outbound, MessagePeerName( peer ), owner, args );
		}

		/// <summary>
		/// A broadcast RPC narrowed by a filter, naming who it went to rather than "all".
		/// </summary>
		internal static void FilteredRpc( string name, int size, IReadOnlyList<Connection> recipients, object owner, string args )
		{
			if ( !LogsMessages ) return;
			AddRpc( name, size, true, RecipientsName( recipients ), owner, args );
		}

		static void AddRpc( string name, int size, bool outbound, string peer, object owner, string args )
		{
			var (id, objectName) = DescribeOwner( owner );
			Add( new Entry( DateTime.UtcNow, Kind.Rpc, name, peer, outbound, size, id, objectName, args ) );
		}

		static string RecipientsName( IReadOnlyList<Connection> recipients )
		{
			if ( recipients.Count == 0 ) return "nobody";

			var sb = new System.Text.StringBuilder();
			var named = Math.Min( recipients.Count, MaxNamedRecipients );
			for ( int i = 0; i < named; i++ )
			{
				if ( i > 0 ) sb.Append( ", " );
				sb.Append( PeerName( recipients[i] ) );
			}

			if ( recipients.Count > named ) sb.Append( $" +{recipients.Count - named} more" );
			return sb.ToString();
		}

		internal static void TransformUpdate( GameObject go, in Transform tx, TransformParts parts, int size, bool outbound, Connection peer )
		{
			if ( !LogsTransforms ) return;

			var position = (parts & TransformParts.Position) != 0 ? $"position {tx.Position.x:0.#},{tx.Position.y:0.#},{tx.Position.z:0.#}" : null;
			var turned = (parts & (TransformParts.Rotation | TransformParts.Scale)) switch
			{
				TransformParts.Rotation => "rotation",
				TransformParts.Scale => "scale",
				TransformParts.Rotation | TransformParts.Scale => "rotation, scale",
				_ => null,
			};
			var value = position is null ? turned ?? "" : turned is null ? position : $"{position}, {turned}";

			Add( new Entry( DateTime.UtcNow, Kind.Transform, TransformName, MessagePeerName( peer ), outbound, size, go.Id, go.Name, value ) );
		}

		internal static string FormatRpcArgs( object[] values, System.Reflection.ParameterInfo[] parameters )
		{
			if ( values.Length == 0 ) return "no args";

			var sb = new System.Text.StringBuilder();
			for ( int i = 0; i < values.Length; i++ )
			{
				if ( i > 0 ) sb.Append( ", " );
				if ( parameters is not null && i < parameters.Length && parameters[i].Name is { } paramName ) sb.Append( paramName ).Append( '=' );

				sb.Append( FormatValue( values[i] ) );
			}

			return sb.ToString();
		}

		/// <summary>
		/// Logs dormancy changes of the objects this machine sends. Proxies' dormancy isn't known here.
		/// </summary>
		internal static void SampleDormancy( HashSet<NetworkObject> objects )
		{
			if ( !LogsMessages ) return;

			var now = DateTime.UtcNow;
			var since = _lastDormancySample;
			_lastDormancySample = now;

			_dormancySeen.Clear();
			_dormancyChanged.Clear();
			foreach ( var obj in objects )
			{
				if ( !obj.IsValid || obj.GameObject is not { } go ) continue;

				var state = NetworkDebugSystem.GetDormancyState( obj ) switch
				{
					NetworkDebugSystem.DormancyState.PendingAck => NetDormancy.Awake,
					NetworkDebugSystem.DormancyState.AlwaysAwake => NetDormancy.Awake,
					NetworkDebugSystem.DormancyState.AwakeGrace => NetDormancy.Grace,
					NetworkDebugSystem.DormancyState.AckClean => NetDormancy.AsleepIdle,
					NetworkDebugSystem.DormancyState.VisibilitySleep => NetDormancy.AsleepHidden,
					_ => NetDormancy.None,
				};

				if ( state == NetDormancy.None ) continue;
				_dormancySeen.Add( go.Id );

				var previous = _dormancy.TryGetValue( go.Id, out var last ) ? last.Entry.Dormancy : NetDormancy.None;
				if ( previous != state ) _dormancyChanged.Add( new DormancyChange( go.Id, go.Name, state, previous ) );
			}

			_dormancyGone.Clear();
			foreach ( var id in _dormancy.Keys )
			{
				if ( !_dormancySeen.Contains( id ) ) _dormancyGone.Add( id );
			}

			if ( _dormancyChanged.Count == 0 && _dormancyGone.Count == 0 ) return;

			lock ( _lock )
			{
				foreach ( var (id, name, state, previous) in _dormancyChanged )
				{
					var hadTraffic = _lastActivity.TryGetValue( id, out var active ) && active >= since;
					var e = new Entry( now, Kind.Dormancy, DormancyName, "", true, 0, id, name, DescribeDormancy( state, previous, hadTraffic ), Dormancy: state );
					AddLocked( e );
					_dormancy[id] = new DormancyRecord( e, Added );
				}

				foreach ( var id in _dormancyGone )
				{
					_dormancy.Remove( id );
				}
			}

			_dormancyChanged.Clear();
		}

		/// <summary>
		/// The session started, a peer came or went, or it ended. Peers are numbered from here, the host 0.
		/// </summary>
		internal static void Session( string what, Connection peer, bool starts = false, bool ends = false )
		{
			if ( ends && !_inSession ) return;

			if ( starts )
			{
				lock ( _lock )
				{
					_peers.Clear();
					ForgetObjectsLocked();
				}
				_inSession = true;
			}

			if ( Capturing ) Add( new Entry( DateTime.UtcNow, Kind.Session, SessionEventsName, peer is null ? "" : PeerName( peer ), false, 0, SessionLane, SessionLaneName, what ) );

			if ( ends )
			{
				_inSession = false;
				SessionEnded = DateTime.UtcNow;
			}
		}

		static void Add( in Entry e )
		{
			lock ( _lock ) AddLocked( e );
		}

		static void AddLocked( in Entry e )
		{
			_events ??= new Queue<Entry>();
			if ( e.Kind != Kind.Session ) _events.EnsureCapacity( Capacity + 1 );

			if ( e.Time < _newest )
			{
				_events.Enqueue( e with { Time = _newest } );
			}
			else
			{
				_newest = e.Time;
				_events.Enqueue( e );
			}

			Added++;
			while ( _events.Count > Capacity )
			{
				_events.Dequeue();
				Dropped++;
			}
		}

		static string MessagePeerName( Connection peer ) => peer is null ? "all" : PeerName( peer );

		/// <summary>
		/// A peer as the log shows it: its name, its number in the session (the host 0, the rest as they're first seen),
		/// and whether it's the host or this machine.
		/// </summary>
		internal static string PeerName( Connection c )
		{
			var name = ConnectionName( c );
			var host = c.IsHost;
			var local = c == Connection.Local;

			lock ( _lock )
			{
				var known = _peers.TryGetValue( c.Id, out var peer );
				if ( known && peer.Name == name && peer.Host == host && peer.Local == local ) return peer.Label;

				var number = known ? peer.Number : host ? 0 : _peers.Count( x => x.Value.Number > 0 ) + 1;
				var label = $"{name} (#{number}{(host ? ", host" : "")}{(local ? ", local" : "")})";
				_peers[c.Id] = new Peer( number, name, host, local, label );
				return label;
			}
		}

		/// <summary>
		/// A host without player info is a dedicated server, whose <see cref="Connection.Name"/> is a placeholder.
		/// </summary>
		internal static string ConnectionName( Connection c )
		{
			if ( c.IsHost && !c.HasInfo ) return "Server";
			var display = c.DisplayName;
			return string.IsNullOrEmpty( display ) ? c.Name : display;
		}

		static string DescribeDormancy( NetDormancy state, NetDormancy previous, bool hadTraffic )
		{
			var text = state switch
			{
				NetDormancy.Awake => "awake",
				NetDormancy.Grace => "settling",
				NetDormancy.AsleepIdle => "asleep - idle",
				NetDormancy.AsleepHidden => "asleep - not visible",
				_ => "?",
			};

			if ( previous.IsAsleep() && state is NetDormancy.Awake or NetDormancy.Grace )
			{
				var why = previous == NetDormancy.AsleepHidden ? "became visible" : hadTraffic ? "sync change" : "moved or refreshed";
				text += $" - {why}";
			}

			return text;
		}

		static (Guid Id, string Name) DescribeOwner( object owner ) => owner switch
		{
			Component c when c.GameObject is { } go => (go.Id, go.Name),
			GameObject go => (go.Id, go.Name),
			GameObjectSystem s => (s.Id, s.GetType().Name),
			_ => (Guid.Empty, "Static"),
		};

		static string FormatValue( Func<object> getValue )
		{
			try
			{
				return FormatValue( getValue() );
			}
			catch ( Exception )
			{
				return "?";
			}
		}

		static string FormatValue( object value )
		{
			try
			{
				var text = value switch
				{
					null => "null",
					string s => $"\"{s.AsSpan( 0, Math.Min( s.Length, ValueLength ) )}\"",
					GameObject go => go.Name,
					Component c => $"{c.GetType().Name} on {c.GameObject?.Name}",
					System.Collections.ICollection list => $"{value.GetType().Name} ({list.Count})",
					_ => value.ToString(),
				};

				return text is { Length: > ValueLength } ? text[..(ValueLength - 1)] + "…" : text;
			}
			catch ( Exception )
			{
				return "?";
			}
		}
	}
}
