using Sandbox.Engine;
using Sandbox.Utility;
using System.Diagnostics;
using NetworkLog = Sandbox.Diagnostics.DevTools.NetworkLog;

namespace Sandbox;

[Expose]
sealed partial class NetworkDebugSystem : GameObjectSystem<NetworkDebugSystem>
{
	[ConVar( "net_debug_culling", ConVarFlags.Protected | ConVarFlags.Cheat )]
	private static bool DebugCulling { get; set; }

	[ConVar( "net_diag_record", ConVarFlags.Protected, Help = "Record network RPC stats for use with net_diag_dump" )]
	private static bool NetworkRecord { get; set; }

	public NetworkDebugSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, Tick, "Tick" );
	}

	internal Dictionary<string, MessageStats> InboundStats;
	internal Dictionary<string, MessageStats> OutboundStats;
	internal Dictionary<Guid, Dictionary<string, MessageStats>> ConnectionStats;

	internal Dictionary<string, MessageStats> SyncVarInboundStats;
	internal Dictionary<string, MessageStats> SyncVarOutboundStats;
	internal Dictionary<Guid, Dictionary<string, MessageStats>> SyncVarConnectionStats;
	private readonly Stopwatch _trackingTimer = new();

	internal enum MessageType
	{
		Rpc,
		Refresh,
		Spawn,
		Snapshot,
		SyncVars,
		Culling,
		StringTable,
		UserCommands
	}

	internal class Sample
	{
		public readonly Dictionary<MessageType, int> BytesPerType = new();
	}

	internal const float SampleRate = 1f / 30f; // 30 Hz
	internal const int MaxSamples = 300; // ~10 seconds of history
	internal readonly Queue<Sample> Samples = new();

	private RealTimeUntil _nextSampleTime = 0f;
	private Sample _currentTick = new();

	[ConCmd( "net_dump_objects" )]
	internal static void DumpNetworkObjects()
	{
		foreach ( var o in Game.ActiveScene.networkedObjects )
		{
			Log.Info( o.GameObject );
		}
	}

	internal class MessageStats
	{
		public int TotalCalls { get; private set; }
		public int TotalBytes { get; private set; }
		public int BytesPerMessage { get; private set; }
		public int PeakBytes { get; private set; }
		private CircularBuffer<int> History { get; set; } = new( 10 );
		private int _historySum;

		public void Add( int messageSize )
		{
			TotalCalls++;
			TotalBytes += messageSize;
			if ( messageSize > PeakBytes ) PeakBytes = messageSize;

			if ( History.IsFull ) _historySum -= History.Front();
			History.PushBack( messageSize );
			_historySum += messageSize;
			BytesPerMessage = _historySum / History.Size;
		}
	}

	/// <summary>
	/// How long stats have been accumulating since the last reset (or first tracked message).
	/// </summary>
	internal TimeSpan TrackingElapsed => _trackingTimer.Elapsed;

	/// <summary>
	/// Reset all accumulated RPC tracking stats.
	/// </summary>
	internal void Reset()
	{
		InboundStats = null;
		OutboundStats = null;
		ConnectionStats = null;
		SyncVarInboundStats = null;
		SyncVarOutboundStats = null;
		SyncVarConnectionStats = null;
		_trackingTimer.Reset();
	}

	private void EnsureInitialized()
	{
		if ( _trackingTimer.IsRunning )
			return;

		InboundStats = new();
		OutboundStats = new();
		ConnectionStats = new();
		SyncVarInboundStats = new();
		SyncVarOutboundStats = new();
		SyncVarConnectionStats = new();
		_trackingTimer.Start();
	}

	internal static int SerializedSize<T>( T message )
	{
		var bs = ByteStream.Create( 256 );
		try
		{
			GlobalContext.Current.TypeLibrary.ToBytes( message, ref bs );
			return bs.Length;
		}
		finally
		{
			bs.Dispose();
		}
	}

	/// <summary>
	/// Track a network message for diagnostic purposes.
	/// </summary>
	internal void Track<T>( string name, T message, bool outbound = false, Connection peer = default )
	{
		if ( !TracksCalls )
			return;

		Track( name, SerializedSize( message ), outbound, peer );
	}

	/// <summary>
	/// Track a message and add its size to the graph.
	/// </summary>
	internal void Track<T>( MessageType type, string name, T message, bool outbound = false, Connection peer = default )
	{
		if ( !TracksCalls )
		{
			return;
		}

		var size = SerializedSize( message );
		Record( type, size );
		Track( name, size, outbound, peer );
	}

	void Track( string name, int size, bool outbound, Connection peer )
	{
		NetworkLog.Message( NetworkLog.Kind.Object, name, size, outbound, peer );
		CountCall( name, size, outbound, peer );
	}

	/// <summary>
	/// Track a received scene snapshot. Static since it can arrive before there's a scene with this system.
	/// </summary>
	internal static void TrackSnapshot<T>( T message, Connection source )
	{
		if ( !Diagnostics.DevTools.IsCollecting( "network" ) )
		{
			return;
		}

		var size = SerializedSize( message );
		Current?.Record( MessageType.Snapshot, size );
		NetworkLog.Message( NetworkLog.Kind.Snapshot, "SceneSnapshot", size, false, source );
	}

	/// <summary>
	/// Whether calls are being tracked, so callers can skip building their names.
	/// </summary>
	internal bool TracksCalls => NetworkRecord || Diagnostics.DevTools.IsCollecting( "network" );

	/// <summary>
	/// Track an RPC and its arguments. A null peer is a broadcast.
	/// </summary>
	internal void TrackRpc( string name, int msgSize, bool outbound, Connection peer, object owner, string args )
	{
		if ( !TracksCalls )
		{
			return;
		}

		NetworkLog.Rpc( name, msgSize, outbound, peer, owner, args );
		CountCall( name, msgSize, outbound, peer );
	}

	/// <summary>
	/// Track a broadcast RPC narrowed by <see cref="Rpc.Filter"/>.
	/// </summary>
	internal void TrackFilteredRpc( string name, int msgSize, IReadOnlyList<Connection> recipients, object owner, string args )
	{
		if ( !TracksCalls )
		{
			return;
		}

		NetworkLog.FilteredRpc( name, msgSize, recipients, owner, args );
		CountCall( name, msgSize, outbound: true, null );
	}

	void CountCall( string name, int msgSize, bool outbound, Connection source, bool sync = false )
	{
		EnsureInitialized();
		var stats = sync
			? outbound ? SyncVarOutboundStats : SyncVarInboundStats
			: outbound ? OutboundStats : InboundStats;

		if ( !stats.TryGetValue( name, out var stat ) )
			stat = stats[name] = new();

		stat.Add( msgSize );

		// Per-connection breakdown for inbound messages
		if ( !outbound && source is not null )
		{
			var connections = sync ? SyncVarConnectionStats : ConnectionStats;
			if ( !connections.TryGetValue( source.Id, out var connStats ) )
				connStats = connections[source.Id] = new();

			if ( !connStats.TryGetValue( name, out var connStat ) )
				connStat = connStats[name] = new();

			connStat.Add( msgSize );
		}
	}

	/// <summary>
	/// Track a sync var property update for diagnostic purposes.
	/// </summary>
	internal void TrackSync( Network.NetworkTable.Entry entry, ReadOnlySpan<byte> wire, bool outbound = false, Connection source = default )
	{
		if ( !TracksCalls )
			return;

		NetworkLog.SyncVar( entry, wire, outbound, outbound ? null : source );
		CountCall( entry.DebugName, wire.Length, outbound, source, sync: true );
	}

	/// <summary>
	/// Record the size of a message by category to be added to the current tick sample. These
	/// can be shown on a network graph.
	/// </summary>
	internal void Record<T>( MessageType type, T message )
	{
		if ( !Diagnostics.DevTools.IsCollecting( "network" ) )
			return;

		Record( type, SerializedSize( message ) );
	}

	/// <summary>
	/// Record the size of a message by category to be added to the current tick sample. These
	/// can be shown on a network graph.
	/// </summary>
	internal void Record( MessageType type, int size )
	{
		if ( !Diagnostics.DevTools.IsCollecting( "network" ) )
			return;

		if ( !_currentTick.BytesPerType.TryAdd( type, size ) )
			_currentTick.BytesPerType[type] += size;
	}

	void Tick()
	{
		if ( DebugCulling )
		{
			DrawPvs();
		}

		DrawDormancy();

		if ( !_nextSampleTime )
			return;

		Samples.Enqueue( _currentTick );

		// Once the history is full, the oldest sample is reused for the next
		if ( Samples.Count > MaxSamples )
		{
			_currentTick = Samples.Dequeue();
			_currentTick.BytesPerType.Clear();
		}
		else
		{
			_currentTick = new Sample();
		}

		_nextSampleTime = SampleRate;
	}

	void DrawPvs()
	{
		using var _ = Gizmo.Scope();
		Gizmo.Draw.IgnoreDepth = true;

		foreach ( var no in Scene.networkedObjects )
		{
			using ( Gizmo.ObjectScope( no, no.GameObject.WorldTransform ) )
			{
				var bounds = no.GameObject.GetLocalBounds();
				var isVisible = Scene.IsPointVisibleToConnection( Connection.Local, no.GameObject.WorldPosition );

				if ( no.IsProxy )
					isVisible = !no.GameObject.IsNetworkCulled;

				Gizmo.Draw.Color = isVisible ? Color.Green : Color.Red;

				if ( isVisible )
				{
					Gizmo.Draw.LineBBox( bounds );
				}
				else
				{
					Gizmo.Draw.Sprite( Vector3.Zero, 32f, "materials/gizmo/tracked_object.png" );
				}
			}
		}
	}
}
