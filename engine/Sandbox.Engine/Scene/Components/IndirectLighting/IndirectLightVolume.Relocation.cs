namespace Sandbox;

using System;
using System.Threading.Tasks;

/// <summary>
/// Probe relocation functionality for DDGI volumes.
/// Moves probes out of geometry to unfuck artifacts.
/// </summary>
public sealed partial class IndirectLightVolume
{
	/// <summary>
	/// Number of ray directions to cast per probe for relocation analysis.
	/// </summary>
	const int RelocationRayCount = 128;

	/// <summary>
	/// Number of iterative refinement steps when computing relocation.
	/// </summary>
	const int RefinementSteps = 12;

	/// <summary>
	/// Threshold for backface hit ratio to consider probe inside geometry.
	/// </summary>
	const float BackfaceThreshold = 0.25f;

	/// <summary>
	/// Minimum distance (as fraction of min spacing) a probe should maintain from frontface geometry.
	/// </summary>
	const float MinFrontfaceDistanceFactor = 0.2f;

	/// <summary>
	/// Maximum offset as fraction of probe spacing (forms ellipsoid constraint).
	/// </summary>
	const float MaxOffsetFactor = 0.45f;

	/// <summary>
	/// How far to trace rays as a multiple of the minimum probe spacing.
	/// </summary>
	const float TraceDistanceFactor = 1.0f;

	/// <summary>
	/// How to handle probes detected inside geometry.
	/// </summary>
	[Property, Group( "Advanced Settings" )]
	public InsideGeometryBehavior InsideGeometry { get; set; } = InsideGeometryBehavior.Relocate;

	/// <summary>
	/// Volume texture storing probe relocation offsets (XYZ = offset, W = active).
	/// </summary>
	private Texture GeneratedRelocationTexture { get; set; }

	/// <summary>
	/// Result of tracing a single ray for relocation purposes.
	/// </summary>
	private readonly struct RelocationRayHit
	{
		public readonly float Distance;
		public readonly bool IsBackface;
		public readonly bool DidHit;

		public RelocationRayHit( float distance, bool isBackface, bool didHit )
		{
			Distance = distance;
			IsBackface = isBackface;
			DidHit = didHit;
		}

		public static RelocationRayHit Miss => new( float.MaxValue, false, false );
	}

	/// <summary>
	/// Aggregated statistics from tracing all rays for a single probe.
	/// </summary>
	private readonly struct ProbeTraceResult
	{
		public readonly int ClosestBackfaceIndex;
		public readonly int ClosestFrontfaceIndex;
		public readonly int FarthestFrontfaceIndex;
		public readonly float ClosestBackfaceDistance;
		public readonly float ClosestFrontfaceDistance;
		public readonly float FarthestFrontfaceDistance;
		public readonly int BackfaceCount;
		public readonly int TotalHits;

		public ProbeTraceResult(
			int closestBackfaceIndex,
			int closestFrontfaceIndex,
			int farthestFrontfaceIndex,
			float closestBackfaceDistance,
			float closestFrontfaceDistance,
			float farthestFrontfaceDistance,
			int backfaceCount,
			int totalHits )
		{
			ClosestBackfaceIndex = closestBackfaceIndex;
			ClosestFrontfaceIndex = closestFrontfaceIndex;
			FarthestFrontfaceIndex = farthestFrontfaceIndex;
			ClosestBackfaceDistance = closestBackfaceDistance;
			ClosestFrontfaceDistance = closestFrontfaceDistance;
			FarthestFrontfaceDistance = farthestFrontfaceDistance;
			BackfaceCount = backfaceCount;
			TotalHits = totalHits;
		}

		public float BackfaceRatio => TotalHits > 0 ? (float)BackfaceCount / TotalHits : 0f;
		public bool HasBackfaceHit => ClosestBackfaceIndex >= 0;
		public bool HasFrontfaceHit => ClosestFrontfaceIndex >= 0;
		public bool HasFarthestFrontface => FarthestFrontfaceIndex >= 0;
	}

	/// <summary>
	/// Computes probe relocation offsets for all probes in the volume.
	/// Uses iterative refinement with mesh tracing.
	/// All computations are relative to probe spacing for resolution-independent behavior.
	/// </summary>
	[Button( "Compute Relocation", "move" ), Hide]
	[Group( "Probe Relocation" )]
	public void ComputeProbeRelocation()
	{
		if ( Scene?.SceneWorld is null )
			return;

		var counts = ProbeCounts;
		var totalProbes = counts.x * counts.y * counts.z;
		var spacing = ComputeSpacing( counts );

		// Generate ray directions based on current settings
		var rayDirections = GenerateSphericalDirections( RelocationRayCount );

		// All distances are relative to the minimum spacing dimension
		var minSpacing = MathF.Min( spacing.x, MathF.Min( spacing.y, spacing.z ) );
		var minFrontfaceDistance = minSpacing * MinFrontfaceDistanceFactor;
		var maxTraceDistance = minSpacing * TraceDistanceFactor;

		Probes = new Probe[totalProbes];
		for ( int i = 0; i < totalProbes; i++ )
			Probes[i] = new Probe();

		// Process probes in parallel
		Parallel.For( 0, counts.z, z =>
		{
			for ( int y = 0; y < counts.y; y++ )
			{
				for ( int x = 0; x < counts.x; x++ )
				{
					var index = new Vector3Int( x, y, z );
					var flatIndex = x + y * counts.x + z * counts.x * counts.y;
					var basePosition = GetProbeWorldPosition( index );
					var probe = Probes[flatIndex];

					// Iteratively refine the probe offset
					var offset = Vector3.Zero;
					var isActive = true;

					for ( int step = 0; step < RefinementSteps && isActive; step++ )
					{
						var currentPosition = basePosition + offset;

						// Trace all rays from current position
						var traceResult = TraceProbeRays( currentPosition, maxTraceDistance, rayDirections );

						// Compute the offset delta for this refinement step
						var (newOffset, shouldDeactivate) = ComputeRelocationOffset(
							offset,
							traceResult,
							spacing,
							minSpacing,
							minFrontfaceDistance,
							rayDirections
						);

						if ( shouldDeactivate )
						{
							isActive = false;
							break;
						}

						offset = newOffset;
					}

					probe.Offset = offset;
					probe.Active = isActive;
				}
			}
		} );

		UpdateRelocationTexture();
		Scene.Get<DDGIVolumeSystem>()?.MarkDirty();
	}

	/// <summary>
	/// Traces rays in all provided directions from a probe position.
	/// Uses mesh tracing with CullMode = 0 (no culling) to detect both front and back faces.
	/// </summary>
	private ProbeTraceResult TraceProbeRays( Vector3 probePosition, float maxDistance, Vector3[] directions )
	{
		int closestBackfaceIndex = -1;
		int closestFrontfaceIndex = -1;
		int farthestFrontfaceIndex = -1;
		float closestBackfaceDistance = float.MaxValue;
		float closestFrontfaceDistance = float.MaxValue;
		float farthestFrontfaceDistance = 0f;
		int backfaceCount = 0;
		int totalHits = 0;

		for ( int i = 0; i < directions.Length; i++ )
		{
			var direction = directions[i];
			var hit = TraceRayWithBackfaceDetection( probePosition, direction, maxDistance );

			if ( !hit.DidHit )
				continue;

			totalHits++;

			if ( hit.IsBackface )
			{
				backfaceCount++;

				if ( hit.Distance < closestBackfaceDistance )
				{
					closestBackfaceDistance = hit.Distance * 0.999f;
					closestBackfaceIndex = i;
				}
			}
			else
			{
				if ( hit.Distance < closestFrontfaceDistance )
				{
					closestFrontfaceDistance = hit.Distance;
					closestFrontfaceIndex = i;
				}

				if ( hit.Distance > farthestFrontfaceDistance )
				{
					farthestFrontfaceDistance = hit.Distance;
					farthestFrontfaceIndex = i;
				}
			}
		}

		return new ProbeTraceResult(
			closestBackfaceIndex,
			closestFrontfaceIndex,
			farthestFrontfaceIndex,
			closestBackfaceDistance,
			closestFrontfaceDistance,
			farthestFrontfaceDistance,
			backfaceCount,
			totalHits
		);
	}

	/// <summary>
	/// Traces a single ray using mesh tracing and determines if it hit a backface.
	/// Uses CullMode = 0 (no culling) to get all hits, then uses dot product with normal to detect backfaces.
	/// </summary>
	private RelocationRayHit TraceRayWithBackfaceDetection( Vector3 origin, Vector3 direction, float maxDistance )
	{
		var endPoint = origin + direction * maxDistance;

		var trace = Scene.Trace
			.Ray( origin, endPoint )
			.UsePhysicsWorld( false )
			.UseRenderMeshes( hitFront: true, hitBack: true ); // CullMode = 0, hit both faces

		var result = trace.Run();

		if ( !result.Hit )
			return RelocationRayHit.Miss;

		// Determine if we hit a backface by checking if normal points away from ray direction
		// Frontface: normal points toward ray origin, so dot(normal, direction) < 0
		// Backface: normal points away from ray origin, so dot(normal, direction) > 0
		var isBackface = Vector3.Dot( result.Normal, direction ) > 0f;

		return new RelocationRayHit( result.Distance, isBackface, true );
	}

	/// <summary>
	/// Computes the new probe offset for relocation.
	/// Returns the new absolute offset (not delta) and whether the probe should be deactivated.
	/// All distances are computed relative to probe spacing.
	/// </summary>
	private (Vector3 newOffset, bool shouldDeactivate) ComputeRelocationOffset(
		Vector3 currentOffset,
		ProbeTraceResult traceResult,
		Vector3 spacing,
		float minSpacing,
		float minFrontfaceDistance,
		Vector3[] directions )
	{
		var maxOffsetFactor = MathF.Min( MaxOffsetFactor, 0.45f );
		var candidateOffset = currentOffset;
		var hasCandidate = false;
		var closestFrontfaceDistance = traceResult.HasFrontfaceHit ? traceResult.ClosestFrontfaceDistance : float.MaxValue;

		// Case 1: Probe is inside geometry (high backface ratio)
		if ( traceResult.HasBackfaceHit && traceResult.BackfaceRatio >= BackfaceThreshold )
		{
			if ( InsideGeometry == InsideGeometryBehavior.Deactivate )
				return (Vector3.Zero, true);

			var escapeDirection = directions[traceResult.ClosestBackfaceIndex];
			var escapeDistance = traceResult.ClosestBackfaceDistance + minFrontfaceDistance * 0.5f;
			candidateOffset = currentOffset + escapeDirection * escapeDistance;
			hasCandidate = true;
		}
		else if ( traceResult.HasFrontfaceHit && closestFrontfaceDistance < minFrontfaceDistance )
		{
			if ( traceResult.HasFarthestFrontface && traceResult.FarthestFrontfaceIndex != traceResult.ClosestFrontfaceIndex )
			{
				var closestDir = directions[traceResult.ClosestFrontfaceIndex];
				var farthestDir = directions[traceResult.FarthestFrontfaceIndex];

				if ( Vector3.Dot( closestDir, farthestDir ) <= 0f )
				{
					var moveDistance = MathF.Min( traceResult.FarthestFrontfaceDistance, minSpacing );
					candidateOffset = currentOffset + farthestDir * moveDistance;
					hasCandidate = true;
				}
			}

			if ( !hasCandidate && InsideGeometry == InsideGeometryBehavior.Deactivate )
				return (Vector3.Zero, true);
		}
		else if ( closestFrontfaceDistance > minFrontfaceDistance && currentOffset.LengthSquared > 0f )
		{
			var moveBackMargin = MathF.Min( closestFrontfaceDistance - minFrontfaceDistance, currentOffset.Length );

			if ( moveBackMargin > 0f )
			{
				var moveBackDirection = -currentOffset.Normal;
				candidateOffset = currentOffset + moveBackDirection * moveBackMargin;
				hasCandidate = true;
			}
		}

		if ( hasCandidate && IsWithinEllipsoidLimit( candidateOffset, spacing, maxOffsetFactor ) )
			return (candidateOffset, false);

		return (currentOffset, false);
	}

	/// <summary>
	/// Returns true if the offset lies within the ellipsoid defined by spacing and the max factor.
	/// </summary>
	private static bool IsWithinEllipsoidLimit( Vector3 offset, Vector3 spacing, float maxFactor )
	{
		var normalizedOffset = new Vector3(
			spacing.x > 0f ? offset.x / spacing.x : 0f,
			spacing.y > 0f ? offset.y / spacing.y : 0f,
			spacing.z > 0f ? offset.z / spacing.z : 0f
		);

		var maxFactorSquared = maxFactor * maxFactor;
		return normalizedOffset.LengthSquared <= maxFactorSquared;
	}

	/// <summary>
	/// Clears all probe relocation offsets.
	/// </summary>
	[Button( "Clear Relocation", "clear" ), Hide]
	[Group( "Probe Relocation" )]
	public void ClearProbeRelocation()
	{
		Probes = null;
		RelocationTexture?.Dispose();
		RelocationTexture = null;
		Scene?.Get<DDGIVolumeSystem>()?.MarkDirty();
	}

	/// <summary>
	/// Generates evenly distributed directions on a sphere using spherical Fibonacci.
	/// This provides a quasi-uniform distribution with good coverage properties.
	/// </summary>
	private static Vector3[] GenerateSphericalDirections( int count )
	{
		var directions = new Vector3[count];
		var goldenRatio = (1.0f + MathF.Sqrt( 5.0f )) / 2.0f;
		var angleIncrement = MathF.PI * 2.0f * goldenRatio;

		for ( int i = 0; i < count; i++ )
		{
			var t = (float)i / count;
			var inclination = MathF.Acos( 1.0f - 2.0f * t );
			var azimuth = angleIncrement * i;

			var sinInc = MathF.Sin( inclination );
			directions[i] = new Vector3(
				sinInc * MathF.Cos( azimuth ),
				sinInc * MathF.Sin( azimuth ),
				MathF.Cos( inclination )
			);
		}

		return directions;
	}

	/// <summary>
	/// Loads probe data from an existing relocation texture.
	/// This restores the Probes array after a scene reload.
	/// </summary>
	private void LoadProbesFromRelocationTexture()
	{
		if ( !Application.IsEditor )
			return;

		if ( Probes is not null )
			return;

		if ( !RelocationTexture.IsValid() )
			return;

		var counts = ProbeCounts;
		var totalProbes = counts.x * counts.y * counts.z;

		// Verify texture dimensions match current probe counts
		if ( RelocationTexture.Width != counts.x ||
			 RelocationTexture.Height != counts.y ||
			 RelocationTexture.Depth != counts.z )
		{
			Log.Warning( $"RelocationTexture dimensions ({RelocationTexture.Width}x{RelocationTexture.Height}x{RelocationTexture.Depth}) don't match probe counts ({counts}), skipping load" );
			return;
		}

		var pixelData = new Half[totalProbes * 4];
		RelocationTexture.GetPixels3D( (0, 0, 0, counts.x, counts.y, counts.z), 0, pixelData.AsSpan(), ImageFormat.RGBA16161616F );

		Probes = new Probe[totalProbes];

		for ( int i = 0; i < totalProbes; i++ )
		{
			var pixelIndex = i * 4;
			Probes[i] = new Probe
			{
				Offset = new Vector3(
					(float)pixelData[pixelIndex + 0],
					(float)pixelData[pixelIndex + 1],
					(float)pixelData[pixelIndex + 2]
				),
				Active = (float)pixelData[pixelIndex + 3] > 0.5f
			};
		}
	}

	/// <summary>
	/// Creates or updates the relocation texture from CPU offset data.
	/// </summary>
	private void UpdateRelocationTexture()
	{
		if ( Probes is null )
			return;

		var counts = ProbeCounts;

		GeneratedRelocationTexture?.Dispose();
		GeneratedRelocationTexture = Texture.CreateVolume( counts.x, counts.y, counts.z, ImageFormat.RGBA16161616F )
			.WithName( "DDGIRelocation" )
			.Finish();

		var pixelData = new Half[counts.x * counts.y * counts.z * 4];

		for ( int i = 0; i < Probes.Length; i++ )
		{
			var probe = Probes[i];
			var pixelIndex = i * 4;

			pixelData[pixelIndex + 0] = (Half)probe.Offset.x;
			pixelData[pixelIndex + 1] = (Half)probe.Offset.y;
			pixelData[pixelIndex + 2] = (Half)probe.Offset.z;
			pixelData[pixelIndex + 3] = (Half)(probe.Active ? 1.0f : 0.0f);
		}

		GeneratedRelocationTexture.Update( pixelData );
	}
}
