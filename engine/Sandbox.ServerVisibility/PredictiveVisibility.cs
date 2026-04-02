using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sandbox.ServerVisibility;

/// <summary>
/// Handles two anti-pop-in mechanisms:
///
/// 1. <b>Predictive Pre-fetching</b> — If a target's velocity indicates it will become
///    visible within a short time window (scaled by the observer's ping), we start
///    transmitting early so the entity doesn't "pop in" when it rounds a corner.
///
/// 2. <b>Sound-based Visibility</b> — If a target is making noise (footsteps, gunfire),
///    we force-transmit its data within a configurable range regardless of visual
///    occlusion.  This prevents the jarring desync where a player hears a sound but
///    the entity doesn't exist on their client.
/// </summary>
public sealed class PredictiveVisibility
{
	// ─── Sound Tracking ──────────────────────────────────────────

	/// <summary>
	/// Tracks the last time each GameObject made a noise event.
	/// Key = GameObject.Id, Value = RealTime.Now at the moment of the noise.
	/// </summary>
	private readonly Dictionary<System.Guid, float> _lastNoiseTime = new( 64 );

	/// <summary>
	/// Register that a GameObject has made a noise (footstep, gunshot, explosion, etc.).
	/// Call this from game code whenever a sound event occurs on a networked entity.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void RegisterNoise( GameObject source )
	{
		if ( source is null || !source.IsValid() )
			return;

		_lastNoiseTime[source.Id] = RealTime.Now;
	}

	/// <summary>
	/// Register noise by entity ID directly (useful when you don't have the GameObject reference).
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void RegisterNoise( System.Guid entityId )
	{
		_lastNoiseTime[entityId] = RealTime.Now;
	}

	/// <summary>
	/// Check whether a target entity is currently "noisy" — i.e. it made a sound recently
	/// enough that it should be force-visible within the sound range.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool IsNoisy( System.Guid entityId )
	{
		if ( !_lastNoiseTime.TryGetValue( entityId, out var lastTime ) )
			return false;

		return (RealTime.Now - lastTime) <= VisibilityConfig.SoundLingerTime;
	}

	/// <summary>
	/// Check if a target should be force-visible to an observer due to sound.
	/// The target must be noisy AND within <see cref="VisibilityConfig.SoundVisibilityRange"/>
	/// of the observer.
	/// </summary>
	public bool IsForcedVisibleBySound( System.Guid targetId, Vector3 targetPosition, Vector3 observerPosition )
	{
		if ( !IsNoisy( targetId ) )
			return false;

		var distSq = observerPosition.DistanceSquared( targetPosition );
		var rangeSq = VisibilityConfig.SoundVisibilityRange * VisibilityConfig.SoundVisibilityRange;

		return distSq <= rangeSq;
	}

	// ─── Predictive Pre-fetching ─────────────────────────────────

	/// <summary>
	/// Determine whether a currently-invisible target should be pre-fetched because
	/// its velocity will carry it into the observer's view within the prediction window.
	///
	/// <b>Algorithm:</b>
	/// <list type="number">
	///   <item>Compute the effective lookahead time = base + ping-scaled bonus.</item>
	///   <item>Extrapolate the target's position: <c>futurePos = currentPos + velocity * lookahead</c>.</item>
	///   <item>Run a quick single-point LoS check from the observer to the predicted position.</item>
	///   <item>If the predicted position IS visible, start transmitting now.</item>
	/// </list>
	/// </summary>
	/// <param name="scene">Active scene for tracing.</param>
	/// <param name="observerPosition">Observer's eye position.</param>
	/// <param name="observerPingMs">Observer's round-trip ping in milliseconds.</param>
	/// <param name="targetPosition">Target's current world position.</param>
	/// <param name="targetVelocity">Target's current velocity vector.</param>
	/// <param name="targetObject">Target GameObject (excluded from traces).</param>
	/// <param name="observerObject">Observer GameObject (excluded from traces).</param>
	/// <returns>True if the target should be pre-fetched.</returns>
	public bool ShouldPrefetch( Scene scene, Vector3 observerPosition, float observerPingMs,
		Vector3 targetPosition, Vector3 targetVelocity,
		GameObject targetObject = null, GameObject observerObject = null )
	{
		// If the target isn't moving, no prediction needed.
		if ( targetVelocity.LengthSquared < 1f )
			return false;

		var lookahead = ComputeLookaheadTime( observerPingMs );
		var futurePosition = targetPosition + targetVelocity * lookahead;

		// Quick distance check first — don't bother tracing if the predicted position
		// is still way out of range.
		var distSq = observerPosition.DistanceSquared( futurePosition );
		if ( distSq > VisibilityConfig.MaxVisibilityRangeSq )
			return false;

		// Single-point LoS check to the predicted position.
		return LineOfSightChecker.IsPointVisible( scene, observerPosition, futurePosition,
			targetObject, observerObject );
	}

	/// <summary>
	/// Compute the effective lookahead time based on the observer's ping.
	/// Higher ping → more aggressive pre-fetching to compensate for network delay.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float ComputeLookaheadTime( float pingMs )
	{
		var baseTime = VisibilityConfig.PredictionLookaheadTime;

		if ( pingMs <= VisibilityConfig.PingPredictionThresholdMs )
			return baseTime;

		var extraMs = pingMs - VisibilityConfig.PingPredictionThresholdMs;
		return baseTime + extraMs * VisibilityConfig.PingPredictionScalePerMs;
	}

	// ─── Maintenance ─────────────────────────────────────────────

	/// <summary>
	/// Prune stale noise entries.  Call periodically (e.g. once per second) to keep
	/// the dictionary from growing unbounded.
	/// </summary>
	public void PruneStaleEntries()
	{
		var now = RealTime.Now;
		var expiry = VisibilityConfig.SoundLingerTime + 1f; // 1 s extra buffer

		// Collect keys to remove (can't modify during enumeration).
		List<System.Guid> toRemove = null;

		foreach ( var (id, lastTime) in _lastNoiseTime )
		{
			if ( now - lastTime > expiry )
			{
				toRemove ??= new List<System.Guid>( 8 );
				toRemove.Add( id );
			}
		}

		if ( toRemove is null )
			return;

		foreach ( var id in toRemove )
			_lastNoiseTime.Remove( id );
	}

	/// <summary>
	/// Remove tracking for a specific entity (e.g. when it's destroyed).
	/// </summary>
	public void RemoveEntity( System.Guid entityId )
	{
		_lastNoiseTime.Remove( entityId );
	}

	/// <summary>
	/// Clear all tracked state.
	/// </summary>
	public void Clear()
	{
		_lastNoiseTime.Clear();
	}
}
