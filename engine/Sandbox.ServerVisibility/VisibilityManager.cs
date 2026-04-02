using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sandbox.ServerVisibility;

/// <summary>
/// The central orchestrator for the server-side Fog of War system.
/// Runs as a <see cref="GameObjectSystem"/> on the server, coordinating:
///
/// <list type="bullet">
///   <item><b>Spatial Grid</b> — broad-phase bucketing of all tracked entities.</item>
///   <item><b>PVS</b> — Source 2 Potentially Visible Set (handled by the engine; we layer on top).</item>
///   <item><b>Line-of-Sight</b> — 9-point BBox raycasting for fine-grained occlusion.</item>
///   <item><b>Predictive Pre-fetching</b> — velocity + ping-based early transmission.</item>
///   <item><b>Sound Visibility</b> — force-transmit noisy entities within range.</item>
///   <item><b>Tick Interleaving</b> — spread LoS checks across frames to cap CPU usage.</item>
/// </list>
///
/// <para>
/// The system does NOT modify any engine files.  It exposes its results through
/// <see cref="IsVisibleTo"/>, which is called by <see cref="VisibilityComponent"/>
/// (an <see cref="Component.INetworkVisible"/> implementation).
/// </para>
/// </summary>
public sealed class VisibilityManager : GameObjectSystem<VisibilityManager>
{
	// ─── Sub-systems ─────────────────────────────────────────────

	/// <summary>Spatial hash grid for broad-phase proximity queries.</summary>
	public SpatialGrid<GameObject> Grid { get; private set; }

	/// <summary>Predictive pre-fetching and sound-based visibility.</summary>
	public PredictiveVisibility Prediction { get; private set; }

	// ─── Per-pair visibility cache ───────────────────────────────

	/// <summary>
	/// Cached visibility result between an observer connection and a target entity.
	/// </summary>
	private struct CachedVisibility
	{
		public bool IsVisible;
		public int TickStamp;           // server tick when this was last computed
		public float LastVisibleAt;     // RealTime.Now when last seen (for grace period)
		public Vector3 LastTargetPos;   // target position from last check (teleport detection)
	}

	/// <summary>
	/// Key: (ObserverConnectionId, TargetGameObjectId) → cached result.
	/// Thread-safe using ConcurrentDictionary to prevent race conditions.
	/// </summary>
	private readonly ConcurrentDictionary<(System.Guid, System.Guid), CachedVisibility> _cache 
		= new( Environment.ProcessorCount * 2, 512 );

	/// <summary>
	/// Caches the observer GameObject mapping (Connection → owned GameObject).
	/// Prevents O(n) linear search for each visibility query.
	/// </summary>
	private readonly Dictionary<System.Guid, GameObject> _observerGameObjects = new( 32 );

	// ─── Tick interleaving state ─────────────────────────────────

	/// <summary>
	/// Round-robin index per observer, so we resume checking from where we left off
	/// on the previous tick.
	/// </summary>
	private readonly Dictionary<System.Guid, int> _checkOffsets = new( 32 );

	/// <summary>
	/// All GameObjects that have a <see cref="VisibilityComponent"/> attached.
	/// Maintained via Register / Unregister calls.
	/// </summary>
	private readonly List<GameObject> _trackedEntities = new( 128 );

	/// <summary>
	/// Set of tracked entity IDs for O(1) duplicate prevention.
	/// </summary>
	private readonly HashSet<System.Guid> _trackedIds = new( 128 );

	/// <summary>
	/// Reusable buffer for spatial grid queries (avoids per-frame allocation).
	/// </summary>
	private readonly List<GameObject> _nearbyBuffer = new( 64 );

	/// <summary>
	/// Current server tick counter (incremented each frame).
	/// </summary>
	private int _currentTick;

	/// <summary>
	/// Timer for periodic maintenance (pruning stale data).
	/// </summary>
	private RealTimeSince _timeSincePrune;

	// ─── Lifecycle ───────────────────────────────────────────────

	public VisibilityManager( Scene scene ) : base( scene )
	{
		Grid = new SpatialGrid<GameObject>( VisibilityConfig.GridCellSize );
		Prediction = new PredictiveVisibility();

		Listen( Stage.FinishUpdate, 1000, ServerTick, "VisibilityManager.Tick" );
	}

	// ─── Entity Registration ────────────────────────────────────

	/// <summary>
	/// Register a GameObject for visibility tracking.  Called by <see cref="VisibilityComponent.OnEnabled"/>.
	/// </summary>
	public void Register( GameObject go )
	{
		if ( go is null || !go.IsValid() )
			return;

		if ( !_trackedIds.Add( go.Id ) )
			return;

		_trackedEntities.Add( go );
		Grid.InsertOrUpdate( go, go.WorldPosition );

		// Cache observer→gameobject mapping for fast lookup.
		if ( go.Network.Active )
		{
			_observerGameObjects[go.Network.OwnerId] = go;
		}
	}

	/// <summary>
	/// Unregister a GameObject from visibility tracking.  Called by <see cref="VisibilityComponent.OnDisabled"/>.
	/// </summary>
	public void Unregister( GameObject go )
	{
		if ( go is null )
			return;

		if ( !_trackedIds.Remove( go.Id ) )
			return;

		_trackedEntities.Remove( go );
		Grid.Remove( go );
		Prediction.RemoveEntity( go.Id );

		// Clear observer mapping if this is an observer GameObject.
		if ( go.Network.Active )
		{
			_observerGameObjects.Remove( go.Network.OwnerId );
		}

		// Purge cache entries for this entity.
		PurgeCacheForEntity( go.Id );
	}

	// ─── Main Tick ───────────────────────────────────────────────

	/// <summary>
	/// Called every server frame.  Updates the spatial grid positions and runs
	/// interleaved LoS checks for all active connections.
	/// </summary>
	private void ServerTick()
	{
		// Only run on the server / host.
		if ( !Networking.IsHost )
			return;

		_currentTick++;

		// Update grid cell size if the ConVar changed.
		Grid.SetCellSize( VisibilityConfig.GridCellSize );

		// Update all tracked entity positions in the spatial grid.
		for ( var i = _trackedEntities.Count - 1; i >= 0; i-- )
		{
			var entity = _trackedEntities[i];

			if ( !entity.IsValid() )
			{
				// Entity was destroyed — clean up.
				_trackedIds.Remove( entity.Id );
				_trackedEntities.RemoveAt( i );
				Grid.Remove( entity );
				Prediction.RemoveEntity( entity.Id );
				continue;
			}

			Grid.InsertOrUpdate( entity, entity.WorldPosition );
		}

		// Periodic maintenance.
		if ( _timeSincePrune > 2f )
		{
			_timeSincePrune = 0f;
			Prediction.PruneStaleEntries();
			PruneStaleCache();
		}
	}

	// ─── Core Visibility Query ──────────────────────────────────

	/// <summary>
	/// Determine whether <paramref name="target"/> should be network-visible to
	/// <paramref name="observer"/>.  This is the method called by
	/// <see cref="VisibilityComponent.IsVisibleToConnection"/>.
	///
	/// <b>Pipeline:</b>
	/// <list type="number">
	///   <item>Spectator bypass — spectators see everything.</item>
	///   <item>Cache check — return cached result if still valid.</item>
	///   <item>Range check — beyond max range → invisible.</item>
	///   <item>Sound check — noisy targets within range → visible.</item>
	///   <item>Tick interleaving gate — skip if we've hit the per-tick budget.</item>
	///   <item>LoS check — 9-point BBox raycast.</item>
	///   <item>Predictive pre-fetch — if LoS failed, check predicted position.</item>
	///   <item>Grace period — don't cull immediately after losing visibility.</item>
	/// </list>
	/// </summary>
	public bool IsVisibleTo( Connection observer, GameObject target, BBox worldBounds )
	{
		if ( observer is null || target is null || !target.IsValid() )
			return true; // fail-open: transmit if we can't determine

		// ── 1. Spectator bypass ──────────────────────────────────
		// If the observer is a spectator (dead, in spectator mode, etc.),
		// they should see everyone.  Game code should set a tag or property;
		// we check for the "spectator" tag on any GameObject owned by this connection.
		if ( IsSpectator( observer ) )
			return true;

		var pairKey = (observer.Id, target.Id);

		// ── 2. Cache check ───────────────────────────────────────
		if ( _cache.TryGetValue( pairKey, out var cached ) )
		{
			var tickAge = _currentTick - cached.TickStamp;

			if ( tickAge < VisibilityConfig.CacheValidTicks )
			{
				// Additional validity: check if target teleported (cache invalidation).
				var posDelta = targetPos.Distance( cached.LastTargetPos );
				if ( posDelta <= VisibilityConfig.MaxTeleportDistance )
				{
					return cached.IsVisible;
				}
			}
		}

		// ── 3. Range check ───────────────────────────────────────
		var observerPos = GetObserverEyePosition( observer );
		var targetPos = target.WorldPosition;
		var distSq = observerPos.DistanceSquared( targetPos );

		if ( distSq > VisibilityConfig.MaxVisibilityRangeSq )
		{
			UpdateCache( pairKey, false, targetPos );
			return false;
		}

		// ── 4. Sound-based forced visibility ─────────────────────
		if ( Prediction.IsForcedVisibleBySound( target.Id, targetPos, observerPos ) )
		{
			UpdateCache( pairKey, true, targetPos );
			return true;
		}

		// ── 5. Tick interleaving gate ────────────────────────────
		// If we've already done too many LoS checks this tick for this observer,
		// return the last known result (or true if no result exists — fail-open).
		if ( !TryConsumeCheckBudget( observer.Id ) )
		{
			return cached.IsVisible; // stale but better than a CPU spike
		}

		// ── 6. Line-of-Sight check ──────────────────────────────
		var observerGo = FindObserverGameObject( observer );
		var losResult = LineOfSightChecker.Check(
			Scene, observerPos, worldBounds, target, observerGo );

		// Store debug data if enabled.
		if ( VisibilityConfig.DebugEnabled )
		{
			VisibilityDebugOverlay.RecordCheck( observer, target, observerPos, losResult );
		}

		if ( losResult.IsVisible )
		{
			UpdateCache( pairKey, true, targetPos );
			return true;
		}

		// ── 7. Predictive pre-fetch ──────────────────────────────
		var targetVelocity = GetEntityVelocity( target );

		if ( targetVelocity.LengthSquared > 1f )
		{
			// Safety: ensure ping is valid (default 50ms if null/invalid).
			var pingMs = observer.Ping ?? 50f;
			var safePingMs = MathF.Max( 10f, MathF.Min( 500f, pingMs ) );

			if ( Prediction.ShouldPrefetch( Scene, observerPos, safePingMs,
				targetPos, targetVelocity, target, observerGo ) )
			{
				UpdateCache( pairKey, true, targetPos );
				return true;
			}
		}

		// ── 8. Grace period ──────────────────────────────────────
		// Don't cull immediately — allow a grace window to prevent flicker.
		// Use randomized grace period to prevent hacker prediction of culling time.
		if ( cached.IsVisible )
		{
			var randomizedGrace = VisibilityConfig.CullGracePeriod 
				+ Random.Shared.NextSingle() * VisibilityConfig.CullGracePeriodRandomness;
			
			if ( (RealTime.Now - cached.LastVisibleAt) < randomizedGrace )
			{
				// Keep transmitting during grace period but mark as "going invisible".
				return true;
			}
		}

		UpdateCache( pairKey, false, targetPos );
		return false;
	}

	// ─── Helpers ─────────────────────────────────────────────────

	/// <summary>
	/// Get the observer's eye position from their visibility origins.
	/// Falls back to Vector3.Zero if no origins are set.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Vector3 GetObserverEyePosition( Connection observer )
	{
		var origins = observer.VisibilityOrigins;
		if ( origins is null || origins.Length == 0 )
			return Vector3.Zero;

		return origins[0];
	}

	/// <summary>
	/// Try to find the GameObject that the observer connection owns (their player pawn).
	/// Uses cached mapping for O(1) lookup instead of O(n) linear search.
	/// Used to exclude the observer from LoS traces.
	/// </summary>
	private GameObject FindObserverGameObject( Connection observer )
	{
		if ( _observerGameObjects.TryGetValue( observer.Id, out var go ) )
		{
			if ( go.IsValid() )
				return go;
			
			// Stale cached entry — clean up.
			_observerGameObjects.Remove( observer.Id );
		}

		return null;
	}

	/// <summary>
	/// Check if a connection is in spectator mode.
	/// Audits spectator bypass claims to detect spoofing attempts.
	/// </summary>
	private bool IsSpectator( Connection observer )
	{
		var go = FindObserverGameObject( observer );

		if ( go is null || !go.IsValid() )
			return false;

		var isSpectator = go.Tags.Has( "spectator" );

		// Audit logging: spectator bypass claimed.
		if ( isSpectator && VisibilityConfig.AuditLoggingEnabled )
		{
			Log.Info( $"[VIS_AUDIT] SPECTATOR_BYPASS: Connection {observer.Id} (SteamID={observer.SteamId})" );
		}

		return isSpectator;
	}

	/// <summary>
	/// Get the velocity of a tracked entity.  Tries Rigidbody first, then falls back
	/// to the CharacterController or a manual velocity component.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Vector3 GetEntityVelocity( GameObject go )
	{
		// Try Rigidbody
		var rb = go.Components.Get<Rigidbody>( FindMode.EnabledInSelfAndDescendants );
		if ( rb is not null )
			return rb.Velocity;

		// Try CharacterController
		var cc = go.Components.Get<CharacterController>( FindMode.EnabledInSelfAndDescendants );
		if ( cc is not null )
			return cc.Velocity;

		return Vector3.Zero;
	}

	// ─── Cache Management ────────────────────────────────────────

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void UpdateCache( (System.Guid, System.Guid) key, bool isVisible, Vector3 targetPos = default )
	{
		var lastVisibleAt = isVisible ? RealTime.Now : (_cache.TryGetValue( key, out var old ) ? old.LastVisibleAt : 0f);

		_cache[key] = new CachedVisibility
		{
			IsVisible = isVisible,
			TickStamp = _currentTick,
			LastVisibleAt = lastVisibleAt,
			LastTargetPos = targetPos
		};
	}

	private void PurgeCacheForEntity( System.Guid entityId )
	{
		var keysToRemove = new List<(System.Guid, System.Guid)>( 8 );

		foreach ( var kvp in _cache )
		{
			if ( kvp.Key.Item2 == entityId )
			{
				keysToRemove.Add( kvp.Key );
			}
		}

		foreach ( var key in keysToRemove )
		{
			_cache.TryRemove( key, out _ );
		}
	}

	private void PruneStaleCache()
	{
		var threshold = _currentTick - (VisibilityConfig.CacheValidTicks * 10);
		var keysToRemove = new List<(System.Guid, System.Guid)>( 32 );

		foreach ( var kvp in _cache )
		{
			if ( kvp.Value.TickStamp < threshold )
			{
				keysToRemove.Add( kvp.Key );
			}
		}

		foreach ( var key in keysToRemove )
		{
			_cache.TryRemove( key, out _ );
		}
	}

	// ─── Tick Interleaving Budget ────────────────────────────────

	/// <summary>
	/// Per-observer check counters, reset each tick.
	/// </summary>
	private readonly Dictionary<System.Guid, int> _checksThisTick = new( 32 );
	private int _budgetResetTick = -1;

	/// <summary>
	/// Try to consume one LoS check from this observer's per-tick budget.
	/// Returns false if the budget is exhausted.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private bool TryConsumeCheckBudget( System.Guid observerId )
	{
		// Reset counters at the start of each new tick.
		if ( _budgetResetTick != _currentTick )
		{
			_checksThisTick.Clear();
			_budgetResetTick = _currentTick;
		}

		_checksThisTick.TryGetValue( observerId, out var count );

		if ( count >= VisibilityConfig.MaxLoSChecksPerTick )
			return false;

		_checksThisTick[observerId] = count + 1;
		return true;
	}
}
