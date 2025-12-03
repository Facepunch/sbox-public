using Sandbox.Utility;

namespace Sandbox;

[Expose]
sealed class NetworkDebugSystem : GameObjectSystem<NetworkDebugSystem>
{
	[ConVar( "net_debug_culling", ConVarFlags.Protected )]
	private static bool DebugCulling { get; set; }

	public NetworkDebugSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, Tick, "Tick" );
	}

	internal readonly Dictionary<string, MessageStats> Stats = new();

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
		private CircularBuffer<int> History { get; set; } = new( 10 );

		public void Add( int messageSize )
		{
			TotalCalls++;
			TotalBytes += messageSize;
			History.PushBack( messageSize );
			BytesPerMessage = (int)History.Average( x => x );
		}
	}

	/// <summary>
	/// Track an incoming message so that we can gather data about how frequently it is called
	/// and the size of the messages.
	/// </summary>
	internal void Track<T>( string name, T message )
	{
		if ( DebugOverlay.overlay_network_calls == 0 )
			return;

		var toBytes = Game.TypeLibrary.ToBytes( message );

		if ( !Stats.TryGetValue( name, out var stat ) )
		{
			stat = Stats[name] = new();
		}

		stat.Add( toBytes.Length );
	}

	/// <summary>
	/// Record the size of a message by category to be added to the current tick sample. These
	/// can be shown on a network graph.
	/// </summary>
	internal void Record<T>( MessageType type, T message )
	{
		if ( DebugOverlay.overlay_network_graph == 0 )
			return;

		var toBytes = Game.TypeLibrary.ToBytes( message );

		if ( !_currentTick.BytesPerType.TryAdd( type, toBytes.Length ) )
			_currentTick.BytesPerType[type] += toBytes.Length;
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
