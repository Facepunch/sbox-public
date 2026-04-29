using Sandbox.Utility;
using System.Diagnostics;

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

	/// <summary>
	/// Track a network message for diagnostic purposes.
	/// </summary>
	internal void Track<T>( string name, T message, bool outbound = false, Connection source = default )
	{
		if ( DebugOverlay.overlay_network_calls == 0 && !NetworkRecord )
			return;

		var bs = ByteStream.Create( 256 );
		int msgSize;
		try
		{
			Game.TypeLibrary.ToBytes( message, ref bs );
			msgSize = bs.Length;
		}
		finally
		{
			bs.Dispose();
		}

		EnsureInitialized();

		var stats = outbound ? OutboundStats : InboundStats;

		if ( !stats.TryGetValue( name, out var stat ) )
			stat = stats[name] = new();

		stat.Add( msgSize );

		// Per-connection breakdown for inbound messages
		if ( !outbound && source is not null )
		{
			if ( !ConnectionStats.TryGetValue( source.Id, out var connStats ) )
				connStats = ConnectionStats[source.Id] = new();

			if ( !connStats.TryGetValue( name, out var connStat ) )
				connStat = connStats[name] = new();

			connStat.Add( msgSize );
		}
	}

	/// <summary>
	/// Track a sync var property update for diagnostic purposes.
	/// </summary>
	internal void TrackSync( string name, int bytes, bool outbound = false, Connection source = default )
	{
		if ( !NetworkRecord )
			return;

		EnsureInitialized();

		var stats = outbound ? SyncVarOutboundStats : SyncVarInboundStats;

		if ( !stats.TryGetValue( name, out var stat ) )
			stat = stats[name] = new();

		stat.Add( bytes );

		if ( !outbound && source is not null )
		{
			if ( !SyncVarConnectionStats.TryGetValue( source.Id, out var connStats ) )
				connStats = SyncVarConnectionStats[source.Id] = new();

			if ( !connStats.TryGetValue( name, out var connStat ) )
				connStat = connStats[name] = new();

			connStat.Add( bytes );
		}
	}

	/// <summary>
	/// Record the size of a message by category to be added to the current tick sample. These
	/// can be shown on a network graph.
	/// </summary>
	internal void Record<T>( MessageType type, T message )
	{
		if ( DebugOverlay.overlay_network_graph == 0 )
			return;

		var bs = ByteStream.Create( 256 );
		int msgSize;
		try
		{
			Game.TypeLibrary.ToBytes( message, ref bs );
			msgSize = bs.Length;
		}
		finally
		{
			bs.Dispose();
		}

		if ( !_currentTick.BytesPerType.TryAdd( type, msgSize ) )
			_currentTick.BytesPerType[type] += msgSize;
	}

	/// <summary>
	/// Record the size of a message by category to be added to the current tick sample. These
	/// can be shown on a network graph.
	/// </summary>
	internal void Record( MessageType type, int size )
	{
		if ( DebugOverlay.overlay_network_graph == 0 )
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

		if ( !_nextSampleTime )
			return;

		Samples.Enqueue( _currentTick );

		if ( Samples.Count > MaxSamples )
			Samples.Dequeue();

		_currentTick = new Sample();
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
