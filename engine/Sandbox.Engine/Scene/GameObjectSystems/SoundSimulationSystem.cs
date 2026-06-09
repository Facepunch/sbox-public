using Sandbox.Audio;

namespace Sandbox;

internal sealed partial class SoundSimulationSystem : GameObjectSystem<SoundSimulationSystem>
{
	readonly List<Audio.Listener> _sceneListeners = new();

	readonly List<SoundHandle> _sortedHandles = new();
	readonly List<SoundHandle> _culledHandles = new();
	readonly Dictionary<Audio.Mixer, int> _voiceCountByMixer = new();

	[ConVar] internal static bool snd_simulation_enable { get; set; } = true;

	public SoundSimulationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, -50, Update, "SoundSimulation" );
	}

	int _tick;

	static PhysicsTraceBuilder ApplySimulationTags( PhysicsTraceBuilder t, SoundHandle handle )
	{
		var mixer = handle?.GetEffectiveMixer() ?? Audio.Mixer.Master;
		if ( mixer is null ) return t;

		var blocking = mixer.GetBlockingTags();
		if ( blocking is { IsEmpty: false } ) t = t.WithAnyTags( blocking );

		var ignored = mixer.GetIgnoredTags();
		if ( ignored is { IsEmpty: false } ) t = t.WithoutTags( ignored );

		return t;
	}

	const int MaxEscapeBodies = 4;

	[System.Runtime.CompilerServices.InlineArray( MaxEscapeBodies )]
	private struct EscapeBodyBuffer { private PhysicsBody _e; }

	// Listener pos is well-defined (camera deep inside head capsule) — tight radius is enough.
	// Source pos is user-placed and often a few inches inside its parent mesh — wider radius.
	int GatherListenerEscapeBodies( Vector3 pos, Span<PhysicsBody> result ) => Scene.FindBodiesInPhysics( pos, 4f, result );
	int GatherSourceEscapeBodies( Vector3 pos, Span<PhysicsBody> result ) => Scene.FindBodiesInPhysics( pos, 12f, result );

	// Native trace filter that skips escape bodies, so a single Run() returns the closest real surface.
	// Thread-static ignore set + cached static delegate => allocation-free and safe under parallel traces.
	[ThreadStatic] static PhysicsBody[] _tlIgnore;
	[ThreadStatic] static int _tlIgnoreCount;

	internal static readonly Func<PhysicsShape, bool> EscapeFilter = ShouldHitEscape;

	static bool ShouldHitEscape( PhysicsShape shape )
	{
		if ( shape.Tags.Has( "world" ) ) return true; // never escape through world

		// SelfOrParent to match PhysicsTraceResult.Body / what the ignore set holds.
		var body = shape.Body?.SelfOrParent;
		for ( int i = 0; i < _tlIgnoreCount; i++ )
			if ( _tlIgnore[i] == body ) return false;

		return true;
	}

	// Stage the bodies to skip for this thread's next trace (one or two sets). Pair with ClearTraceIgnore.
	static void SetTraceIgnore( ReadOnlySpan<PhysicsBody> a, ReadOnlySpan<PhysicsBody> b = default )
	{
		_tlIgnore ??= new PhysicsBody[MaxEscapeBodies * 2];
		int n = 0;
		for ( int i = 0; i < a.Length && n < _tlIgnore.Length; i++ ) _tlIgnore[n++] = a[i];
		for ( int i = 0; i < b.Length && n < _tlIgnore.Length; i++ ) _tlIgnore[n++] = b[i];
		_tlIgnoreCount = n;
	}

	static void ClearTraceIgnore()
	{
		if ( _tlIgnore is null ) return;
		Array.Clear( _tlIgnore, 0, _tlIgnoreCount );
		_tlIgnoreCount = 0;
	}

	internal static float LastSimUpdateMs { get; private set; }

	void Update()
	{
		using var _ = PerformanceStats.Timings.Audio.Scope();
		var sw = DebugOverlay.overlay_audio != 0 ? System.Diagnostics.Stopwatch.StartNew() : null;

		var world = Scene.PhysicsWorld;
		if ( !world.IsValid() ) return;

		_sceneListeners.Clear();
		foreach ( var l in Audio.Listener.ActiveList )
		{
			if ( l.Scene == Scene ) _sceneListeners.Add( l );
		}

		_sortedHandles.Clear();
		SoundHandle.GetActive( _sortedHandles );
		_sortedHandles.Sort( static ( x, y ) => y._CreatedTime.CompareTo( x._CreatedTime ) );

		// Shared per-mixer voice cap. Both occlusion and room iterate _culledHandles, applying
		// their own extra predicates while sharing the priority/cap pass.
		_culledHandles.Clear();
		_voiceCountByMixer.Clear();
		foreach ( var handle in _sortedHandles )
		{
			if ( !handle.IsValid || handle.Scene != Scene || handle.ListenLocal || !handle.CanBeMixed() ) continue;
			var mixer = handle.GetEffectiveMixer();
			if ( mixer is null ) continue;
			_voiceCountByMixer.TryGetValue( mixer, out var count );
			if ( count >= mixer.MaxVoices ) continue;
			_voiceCountByMixer[mixer] = count + 1;
			_culledHandles.Add( handle );
		}

		GatherOcclusionWork( world );
		GatherRoomWork( world );

		int occCount = _occPendingUpdates.Count;
		int roomCount = _roomWork.Count;
		int total = occCount + roomCount;

		if ( total > 0 )
		{
			_parallelWorld = world;
			_parallelOccCount = occCount;
			_parallelRoomCount = roomCount;
			_parallelInterleave = Math.Min( occCount, roomCount ) * 2;
			_parallelDispatchAction ??= ParallelDispatch;
			System.Threading.Tasks.Parallel.For( 0, total, _parallelDispatchAction );
		}

		ApplyOcclusionResults();
		ApplyRoomResults();
		_tick++;
		if ( sw is not null ) LastSimUpdateMs = (float)sw.Elapsed.TotalMilliseconds;
	}

	PhysicsWorld _parallelWorld;
	int _parallelOccCount;
	int _parallelRoomCount;
	int _parallelInterleave;
	Action<int> _parallelDispatchAction;

	void ParallelDispatch( int i )
	{
		bool isRoom;
		int sub;
		if ( i < _parallelInterleave )
		{
			isRoom = (i & 1) == 1;
			sub = i >> 1;
		}
		else
		{
			sub = (_parallelInterleave >> 1) + (i - _parallelInterleave);
			isRoom = _parallelOccCount <= _parallelRoomCount;
		}
		if ( isRoom ) RoomSourceUpdate( sub, _parallelWorld );
		else OcclusionUpdate( sub, _parallelWorld );
	}
}
