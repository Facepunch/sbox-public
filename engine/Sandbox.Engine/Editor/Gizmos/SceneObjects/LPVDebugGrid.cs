namespace Sandbox;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Debug grid overlay for visualizing probe positions.
/// </summary>
sealed class LPVDebugGridObject : SceneCustomObject
{
	[StructLayout( LayoutKind.Sequential )]
	private struct ProbeVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector4 TexCoord0;
		[VertexLayout.TexCoord( 12 )] public Vector3 ProbePosition;
	}

	private static readonly int[] QuadIndices = [0, 1, 2, 0, 2, 3];
	private static readonly Vector4[] QuadUvs =
	[
		new( -1, -1, 0, 0 ),
		new( -1,  1, 0, 0 ),
		new(  1,  1, 0, 0 ),
		new(  1, -1, 0, 0 )
	];

	private readonly Material _material = Material.FromShader( "shaders/lpv_debug.shader" );
	private readonly List<Vector3> _centers = new();
	private readonly List<ProbeVertex> _vertices = new();
	private bool _dirty = true;

	public Transform GridTransform;
	public Vector3Int Counts;
	public BBox LPVBounds;
	public float SampleSize = 10f;
	public int VolumeIndex = -1;
	public IndirectLightVolume.Probe[] Probes;

	public LPVDebugGridObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
	}

	public void UpdateGrid( Transform transform, BBox lpvBounds, Vector3Int counts, float sampleSize, int volumeIndex, IndirectLightVolume.Probe[] probes = null )
	{
		if ( transform != GridTransform || counts != Counts | LPVBounds != lpvBounds || SampleSize != sampleSize || volumeIndex != VolumeIndex || probes != Probes )
			_dirty = true;

		GridTransform = transform;
		Counts = counts;
		LPVBounds = lpvBounds;
		SampleSize = sampleSize;
		VolumeIndex = volumeIndex;
		Probes = probes;

		if ( !_dirty )
			return;

		Rebuild();
		_dirty = false;
	}

	public override void RenderSceneObject()
	{
		if ( _centers.Count == 0 )
			return;

		// Probes blend without depth writes, so draw the farthest first like the native grid.
		var camera = Graphics.CameraPosition;
		_centers.Sort( ( a, b ) => b.DistanceSquared( camera ).CompareTo( a.DistanceSquared( camera ) ) );

		var normal = GridTransform.Rotation * Vector3.Left;
		var tangent = new Vector4( GridTransform.Rotation * Vector3.Up, -1f );

		_vertices.Clear();
		foreach ( var center in _centers )
		{
			foreach ( var i in QuadIndices )
				_vertices.Add( new ProbeVertex { Position = center, Normal = normal, Tangent = tangent, TexCoord0 = QuadUvs[i], ProbePosition = center } );
		}

		Graphics.Attributes.Set( "SampleSize", Math.Clamp( SampleSize, 0.1f, 100f ) );
		Graphics.Attributes.Set( "DDGIVolume", VolumeIndex );
		Graphics.Draw( _vertices, _vertices.Count, _material );
	}

	private void Rebuild()
	{
		_centers.Clear();

		var spacing = ComputeSpacing( LPVBounds.Size, Counts );
		var rotation = GridTransform.Rotation;

		for ( int z = 0; z < Counts.z; z++ )
		{
			for ( int y = 0; y < Counts.y; y++ )
			{
				for ( int x = 0; x < Counts.x; x++ )
				{
					var flatIndex = x + y * Counts.x + z * Counts.x * Counts.y;
					var probe = (Probes is not null && flatIndex < Probes.Length) ? Probes[flatIndex] : null;

					if ( probe is not null && !probe.Active )
						continue;

					var localPos = LPVBounds.Mins + new Vector3( x * spacing.x, y * spacing.y, z * spacing.z );
					var center = GridTransform.PointToWorld( localPos );

					_centers.Add( center + rotation * (probe?.Offset ?? Vector3.Zero) );
				}
			}
		}
	}

	private static Vector3 ComputeSpacing( Vector3 size, Vector3Int counts ) => new(
		counts.x > 1 ? size.x / (counts.x - 1) : 0f,
		counts.y > 1 ? size.y / (counts.y - 1) : 0f,
		counts.z > 1 ? size.z / (counts.z - 1) : 0f
	);
}
