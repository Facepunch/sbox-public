namespace Sandbox;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Probe relocation functionality for DDGI volumes.
/// Moves probes out of geometry to unfuck artifacts.
/// </summary>
public sealed partial class IndirectLightVolume
{
	/// <summary>
	/// Maximum distance a probe can be relocated (as fraction of probe spacing).
	/// </summary>
	[Property, Range( 0.1f, 1.0f ), Hide]
	[Group( "Probe Relocation" )]
	public float MaxRelocationDistanceFraction { get; set; } = 0.75f;

	/// <summary>
	/// Minimum distance from surfaces to maintain (as fraction of probe spacing).
	/// </summary>
	[Property, Range( 0.1f, 1.0f ), Hide]
	[Group( "Probe Relocation" )]
	public float MinSurfaceDistanceFraction { get; set; } = 0.75f;

	/// <summary>
	/// Number of refinement steps when computing relocation.
	/// </summary>
	readonly private float RefinementSteps = 5;

	/// <summary>
	/// Number of rays to cast when computing relocation.
	/// </summary>
	[Property, Range( 8, 128 ), Hide]
	[Group( "Probe Relocation" )]
	public int RelocationRayCount { get; set; } = 48;

	/// <summary>
	/// How to handle probes detected inside geometry.
	/// </summary>
	[Property]
	[Group( "Advanced Settings" )]
	public InsideGeometryBehavior InsideGeometry { get; set; } = InsideGeometryBehavior.Deactivate;

	/// <summary>
	/// Volume texture storing probe relocation offsets (XYZ = offset, W = active).
	/// </summary>
	private Texture GeneratedRelocationTexture { get; set; }

	/// <summary>
	/// Computes probe relocation offsets for all probes in the volume.
	/// </summary>
	[Button( "Compute Relocation", "move" ), Hide]
	[Group( "Probe Relocation" )]
	public void ComputeProbeRelocation()
	{
		if ( Scene is null )
			return;

		var counts = ProbeCounts;
		var totalProbes = counts.x * counts.y * counts.z;
		var spacing = ComputeSpacing( counts );
		var minSpacing = MathF.Min( spacing.x, MathF.Min( spacing.y, spacing.z ) );
		var maxOffset = minSpacing * MaxRelocationDistanceFraction;
		var minSurfaceDistance = minSpacing * MinSurfaceDistanceFraction;

		Probes = new Probe[totalProbes];
		for ( int i = 0; i < totalProbes; i++ )
			Probes[i] = new Probe();

		// Compute relocation for each probe
		Parallel.For( 0, counts.z, z =>
		{
			for ( int y = 0; y < counts.y; y++ )
			{
				for ( int x = 0; x < counts.x; x++ )
				{
					var index = new Vector3Int( x, y, z );
					var flatIndex = x + y * counts.x + z * counts.x * counts.y;
					var probePosition = GetProbeWorldPosition( index );
					var probe = Probes[flatIndex];
					var offset = Vector3.Zero;

					bool isActive = true;
					for ( int i = 0; i < RefinementSteps; i++ )
					{
						offset += ComputeProbeOffset( probePosition + offset, maxOffset / (i + 1), minSurfaceDistance / (i + 1), out isActive );

						if ( !isActive )
							break;
					}

					probe.Offset = offset;
					probe.Active = isActive;
				}
			}
		} );

		// Create/update the relocation texture
		UpdateRelocationTexture();

		Scene.Get<DDGIVolumeSystem>()?.MarkDirty();
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
	/// Computes the offset needed to relocate a single probe out of geometry.
	/// Uses bidirectional tracing since physics traces don't hit backfaces.
	/// </summary>
	private Vector3 ComputeProbeOffset( Vector3 probePosition, float maxOffset, float minSurfaceDistance, out bool isActive )
	{
		isActive = true;

		var directions = GenerateSphericalDirections( RelocationRayCount );
		var traceDistance = maxOffset * 2.0f;

		var frontfaceHits = new List<(Vector3 direction, Vector3 normal, float distance)>();
		var insideGeometryCount = 0;
		var clearDirections = new List<Vector3>();

		foreach ( var direction in directions )
		{
			var outsidePoint = probePosition + direction * traceDistance;

			var outwardTrace = Scene.Trace.Ray( probePosition, outsidePoint )
				.UseRenderMeshes() // Blergh
				.Run();

			var inwardTrace = Scene.Trace.Ray( outsidePoint, probePosition )
				.UseRenderMeshes() // Blergh
				.Run();

			// Case 1: Outward hits immediately (distance ~0) - we're touching or inside geometry
			if ( outwardTrace.Hit && outwardTrace.Distance < 0.1f )
			{
				insideGeometryCount++;
				continue;
			}

			// Case 2: Outward doesn't hit but inward does - we're behind a backface
			if ( !outwardTrace.Hit && inwardTrace.Hit )
			{
				insideGeometryCount++;
				continue;
			}

			// Case 3: Neither hits - completely clear direction
			if ( !outwardTrace.Hit && !inwardTrace.Hit )
			{
				clearDirections.Add( direction );
				continue;
			}

			// Case 4: Outward hits a visible frontface
			if ( outwardTrace.Hit )
			{
				frontfaceHits.Add( (direction, outwardTrace.Normal, outwardTrace.Distance) );
			}
		}

		// Determine if we're inside geometry (more than 25% of rays detect inside)
		var isInsideGeometry = insideGeometryCount > RelocationRayCount / 4;

		if ( isInsideGeometry )
		{
			if ( InsideGeometry == InsideGeometryBehavior.Deactivate )
			{
				isActive = false;
				return Vector3.Zero;
			}

			// Try to escape: prefer completely clear directions
			if ( clearDirections.Count > 0 )
			{
				return clearDirections[0] * maxOffset;
			}

			// Otherwise find the direction with furthest inward hit (thinnest geometry)
			Vector3 bestDirection = Vector3.Zero;
			float bestDistance = 0;

			foreach ( var direction in directions )
			{
				var outsidePoint = probePosition + direction * traceDistance;
				var inwardTrace = Scene.Trace.Ray( outsidePoint, probePosition )
					.IgnoreGameObjectHierarchy( GameObject )
					.Run();

				if ( !inwardTrace.Hit )
				{
					return direction * maxOffset;
				}

				// The further the inward hit, the closer we are to escaping in this direction
				if ( inwardTrace.Distance > bestDistance )
				{
					bestDistance = inwardTrace.Distance;
					bestDirection = direction;
				}
			}

			if ( bestDirection != Vector3.Zero )
			{
				var escapeDistance = traceDistance - bestDistance + minSurfaceDistance;
				return bestDirection * MathF.Min( escapeDistance, maxOffset );
			}
		}

		// Not inside geometry - check if any surface is too close
		if ( frontfaceHits.Count > 0 )
		{
			var closest = frontfaceHits.MinBy( h => h.distance );

			if ( closest.distance < minSurfaceDistance )
			{
				var pushDistance = minSurfaceDistance - closest.distance;
				var offset = closest.normal * pushDistance;

				if ( offset.Length > maxOffset )
					offset = offset.Normal * maxOffset;

				return offset;
			}
		}

		return Vector3.Zero;
	}

	/// <summary>
	/// Generates evenly distributed directions on a sphere using spherical Fibonacci.
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
