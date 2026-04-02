using System.Runtime.CompilerServices;

namespace Sandbox.ServerVisibility;

/// <summary>
/// Performs fine-grained Line-of-Sight (LoS) checks between an observer position and
/// a target entity's bounding box.
///
/// <b>Mathematical Approach — 8-Corner BBox Projection:</b>
/// <para>
/// An axis-aligned bounding box (AABB) has 8 corners computed from the combinatorial
/// expansion of (Mins.x|Maxs.x, Mins.y|Maxs.y, Mins.z|Maxs.z).  We also test the
/// centroid (average of Mins and Maxs) for a total of 9 test points.
/// </para>
/// <para>
/// For each test point we cast a ray from the observer's eye position.  If ANY ray
/// reaches the test point without hitting solid, opaque geometry, the target is
/// considered visible.  This is an optimistic (OR) test — we want to transmit data
/// unless we are absolutely certain the target is fully occluded.
/// </para>
/// <para>
/// Surfaces tagged as "translucent" (glass, fences, grates) are excluded from the
/// trace via <c>WithoutTags</c>, so they never block visibility.
/// </para>
/// </summary>
public static class LineOfSightChecker
{
	/// <summary>
	/// Pre-allocated corner offset multipliers.  Each Vector3 selects either
	/// Mins (0) or Maxs (1) for each axis.
	/// Index 0–7 = corners, index 8 = center (0.5, 0.5, 0.5).
	/// </summary>
	private static readonly Vector3[] BBoxOffsets = new Vector3[VisibilityConfig.BBoxTestPoints]
	{
		new( 0, 0, 0 ), // corner 0: Mins
		new( 1, 0, 0 ), // corner 1
		new( 0, 1, 0 ), // corner 2
		new( 1, 1, 0 ), // corner 3
		new( 0, 0, 1 ), // corner 4
		new( 1, 0, 1 ), // corner 5
		new( 0, 1, 1 ), // corner 6
		new( 1, 1, 1 ), // corner 7: Maxs
		new( 0.5f, 0.5f, 0.5f ) // center
	};

	/// <summary>
	/// Result of a full LoS check against a target's bounding box.
	/// </summary>
	public readonly struct LoSResult
	{
		/// <summary>True if at least one test point was visible.</summary>
		public readonly bool IsVisible;

		/// <summary>Number of test points that were visible (0–9).</summary>
		public readonly int VisiblePointCount;

		/// <summary>The first visible test point in world space (useful for debug drawing).</summary>
		public readonly Vector3 FirstVisiblePoint;

		/// <summary>The first blocked test point in world space (useful for debug drawing).</summary>
		public readonly Vector3 FirstBlockedPoint;

		public LoSResult( bool isVisible, int visiblePointCount, Vector3 firstVisiblePoint, Vector3 firstBlockedPoint )
		{
			IsVisible = isVisible;
			VisiblePointCount = visiblePointCount;
			FirstVisiblePoint = firstVisiblePoint;
			FirstBlockedPoint = firstBlockedPoint;
		}
	}

	/// <summary>
	/// Perform a full 9-point LoS check from <paramref name="eyePosition"/> to the
	/// world-space bounding box <paramref name="targetBounds"/>.
	/// </summary>
	/// <param name="scene">The active scene (needed for <c>Scene.Trace</c>).</param>
	/// <param name="eyePosition">Observer's eye / camera position.</param>
	/// <param name="targetBounds">World-space AABB of the target entity.</param>
	/// <param name="ignoreObject">GameObject to exclude from traces (the target itself).</param>
	/// <param name="observerObject">GameObject to exclude from traces (the observer).</param>
	/// <returns>A <see cref="LoSResult"/> describing visibility.</returns>
	public static LoSResult Check( Scene scene, Vector3 eyePosition, BBox targetBounds,
		GameObject ignoreObject = null, GameObject observerObject = null )
	{
		var visibleCount = 0;
		var firstVisible = Vector3.Zero;
		var firstBlocked = Vector3.Zero;
		var hasFirstVisible = false;
		var hasFirstBlocked = false;

		var size = targetBounds.Maxs - targetBounds.Mins;

		for ( var i = 0; i < VisibilityConfig.BBoxTestPoints; i++ )
		{
			var offset = BBoxOffsets[i];
			var testPoint = targetBounds.Mins + new Vector3(
				size.x * offset.x,
				size.y * offset.y,
				size.z * offset.z
			);

			if ( IsPointVisible( scene, eyePosition, testPoint, ignoreObject, observerObject ) )
			{
				visibleCount++;

				if ( !hasFirstVisible )
				{
					firstVisible = testPoint;
					hasFirstVisible = true;
				}

				// Early-out: we only need ONE visible point to consider the entity visible.
				// We still count for debug purposes but could break here for max perf.
				// For debug overlay quality we continue checking all points.
				if ( !VisibilityConfig.DebugEnabled )
					break;
			}
			else
			{
				if ( !hasFirstBlocked )
				{
					firstBlocked = testPoint;
					hasFirstBlocked = true;
				}
			}
		}

		return new LoSResult(
			isVisible: visibleCount > 0,
			visiblePointCount: visibleCount,
			firstVisiblePoint: firstVisible,
			firstBlockedPoint: firstBlocked
		);
	}

	/// <summary>
	/// Quick single-point LoS check.  Used by the predictive system to test
	/// a predicted future position without the full 9-point sweep.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsPointVisible( Scene scene, Vector3 from, Vector3 to,
		GameObject ignoreTarget = null, GameObject ignoreObserver = null )
	{
		var trace = scene.Trace
			.Ray( from, to )
			.UsePhysicsWorld( true )
			.UseHitboxes( false )
			.WithoutTags( VisibilityConfig.NonBlockingTags );

		if ( ignoreTarget is not null )
			trace = trace.IgnoreGameObjectHierarchy( ignoreTarget );

		if ( ignoreObserver is not null )
			trace = trace.IgnoreGameObjectHierarchy( ignoreObserver );

		var result = trace.Run();

		// If the trace didn't hit anything, or it reached the target point
		// (fraction ≈ 1.0), the point is visible.
		if ( !result.Hit )
			return true;

		// Check if we hit close enough to the target point.
		// A small epsilon accounts for floating-point imprecision.
		var distToTarget = from.Distance( to );
		var hitDist = result.Fraction * distToTarget;
		var remaining = distToTarget - hitDist;

		return remaining < 1f; // within 1 unit = effectively reached the target
	}

	/// <summary>
	/// Compute the 9 test points for a given world-space bounding box.
	/// Useful for debug visualisation.
	/// </summary>
	public static void GetTestPoints( BBox worldBounds, Span<Vector3> output )
	{
		if ( output.Length < VisibilityConfig.BBoxTestPoints )
			throw new System.ArgumentException( $"Output span must have at least {VisibilityConfig.BBoxTestPoints} elements." );

		var size = worldBounds.Maxs - worldBounds.Mins;

		for ( var i = 0; i < VisibilityConfig.BBoxTestPoints; i++ )
		{
			var offset = BBoxOffsets[i];
			output[i] = worldBounds.Mins + new Vector3(
				size.x * offset.x,
				size.y * offset.y,
				size.z * offset.z
			);
		}
	}
}
