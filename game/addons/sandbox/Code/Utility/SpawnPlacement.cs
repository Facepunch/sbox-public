using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure queries for placing things in the world: drop a position to the floor, test whether a
/// spot is occupied, and pick a spawn position from one or more candidates. Never moves anything.
/// </summary>
public static class SpawnPlacement
{
	private const float FloorStartLift = 4f;
	private const float ClearanceRadiusScale = 0.5f;
	private const float AvoidRadius = 16f;
	private const float DefaultMaxDrop = 1024f;
	private const int ScanRings = 2;
	private const int ScanSamples = 8;

	/// <summary>
	/// Settle <paramref name="position"/> onto the first solid (non-trigger) surface within
	/// <paramref name="maxDrop"/> below it. Returns it unchanged if nothing solid is below.
	/// </summary>
	public static Vector3 DropToFloor( Vector3 position, BBox hull, float maxDrop = DefaultMaxDrop, GameObject ignore = null )
	{
		var from = position + Vector3.Up * FloorStartLift;
		var to = position + Vector3.Down * maxDrop;

		var trace = Game.ActiveScene.Trace.Box( hull, from, to ).WithoutTags( "trigger" );
		if ( ignore.IsValid() )
			trace = trace.IgnoreGameObjectHierarchy( ignore );

		var tr = trace.Run();
		return tr.Hit ? tr.EndPosition : position;
	}

	/// <summary>
	/// True if nothing solid (excluding the static world and triggers) overlaps the hull at
	/// <paramref name="position"/> — i.e. no player, prop or NPC is already standing there.
	/// </summary>
	public static bool IsPositionClear( Vector3 position, BBox hull, GameObject ignore = null )
	{
		// Sphere over the hull, centred so the floor below doesn't count.
		var center = position + Vector3.Up * (hull.Maxs.z * 0.5f);
		var radius = MathF.Max( hull.Size.x, hull.Size.y ) * ClearanceRadiusScale;
		if ( radius <= 0f )
			radius = 8f;

		var ignoreRoot = ignore.IsValid() ? ignore.Root : null;

		foreach ( var obj in Game.ActiveScene.FindInPhysics( new Sphere( center, radius ) ) )
		{
			if ( obj.Tags.Has( "trigger" ) || obj.Tags.Has( "world" ) )
				continue;

			if ( ignoreRoot is not null && obj.Root == ignoreRoot )
				continue;

			return false;
		}

		return true;
	}

	/// <summary>
	/// Pick a spawn position from one or more seed transforms: settle to the floor (unless
	/// <paramref name="dropToFloor"/> is false), make sure nothing is standing there, and scan
	/// outward up to <paramref name="scanRadius"/> for a clear spot. Prefers spots away from
	/// <paramref name="avoid"/>. Best-effort if nothing is clear.
	/// </summary>
	public static Transform FindSpawnPosition( IEnumerable<Transform> seeds, BBox hull, bool dropToFloor = true, float scanRadius = 0f, Vector3? avoid = null )
	{
		var ordered = seeds;

		// Push seeds at the avoided position to the back.
		if ( avoid.HasValue )
		{
			var avoidPos = avoid.Value;
			ordered = seeds.OrderBy( s => s.Position.Distance( avoidPos ) < AvoidRadius ? 1 : 0 );
		}

		Transform fallback = default;
		var haveFallback = false;

		foreach ( var seed in ordered )
		{
			foreach ( var candidate in Candidates( seed.Position, scanRadius ) )
			{
				var pos = dropToFloor ? DropToFloor( candidate, hull ) : candidate;
				var transform = new Transform( pos, seed.Rotation );

				if ( IsPositionClear( pos, hull ) )
					return transform;

				if ( !haveFallback )
				{
					fallback = transform;
					haveFallback = true;
				}
			}
		}

		return fallback;
	}

	/// <summary>
	/// Candidate positions for a seed: the seed itself, then concentric rings of offsets when
	/// scanning is enabled.
	/// </summary>
	private static IEnumerable<Vector3> Candidates( Vector3 center, float scanRadius )
	{
		yield return center;

		if ( scanRadius <= 0f )
			yield break;

		for ( var ring = 1; ring <= ScanRings; ring++ )
		{
			var radius = scanRadius * ring / ScanRings;
			for ( var i = 0; i < ScanSamples; i++ )
			{
				var yaw = (360f / ScanSamples) * i;
				yield return center + Rotation.FromYaw( yaw ).Forward * radius;
			}
		}
	}
}
