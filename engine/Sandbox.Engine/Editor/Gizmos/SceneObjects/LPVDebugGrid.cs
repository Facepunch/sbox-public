namespace Sandbox;

using Sandbox.Rendering;
using System;
using System.Collections.Generic;

/// <summary>
/// Debug grid overlay for visualizing probe positions.
/// </summary>
sealed class LPVDebugGridObject : SceneCustomObject
{
	private static readonly int[] QuadIndices = [0, 1, 2, 0, 2, 3];
	private static readonly Vector4[] QuadUvs =
	[
		new( -1, -1, 0, 0 ),
		new( -1,  1, 0, 0 ),
		new(  1,  1, 0, 0 ),
		new(  1, -1, 0, 0 )
	];

	private readonly Material _material = Material.FromShader( "shaders/lpv_debug.shader" );
	private readonly List<Vertex> _vertices = new();
	private readonly CommandList _commandList = new( "LPVDebugGrid" );
	private GpuBuffer<Vertex> _gpuVertexBuffer;
	private int _gpuVertexCount;
	private bool _dirty = true;

	public Transform GridTransform;
	public Vector3Int Counts;
	public BBox LPVBounds;
	public float SampleSize = 10f;
	public IndirectLightVolume.Probe[] Probes;

	public LPVDebugGridObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
	}

	public void UpdateGrid( Transform transform, BBox lpvBounds, Vector3Int counts, float sampleSize, IndirectLightVolume.Probe[] probes = null )
	{
		if ( transform != GridTransform || counts != Counts | LPVBounds != lpvBounds || SampleSize != sampleSize || probes != Probes )
			_dirty = true;

		GridTransform = transform;
		Counts = counts;
		LPVBounds = lpvBounds;
		SampleSize = sampleSize;
		Probes = probes;

		if ( !_dirty )
			return;

		Rebuild();

		if ( _vertices.Count > 0 )
		{
			if ( _gpuVertexBuffer is null || _gpuVertexBuffer.ElementCount < _vertices.Count )
			{
				MainThread.QueueDispose( _gpuVertexBuffer );
				_gpuVertexBuffer = new GpuBuffer<Vertex>( _vertices.Count, GpuBuffer.UsageFlags.Vertex );
			}

			_gpuVertexBuffer.SetData( _vertices );
		}

		_gpuVertexCount = _vertices.Count;
		BuildCommandList();
		_dirty = false;
	}

	public override void RenderSceneObject()
	{
		_commandList.ExecuteOnRenderThread();
	}

	private void BuildCommandList()
	{
		_commandList.Reset();

		if ( _gpuVertexCount == 0 || !_gpuVertexBuffer.IsValid() || _material is null )
			return;

		_commandList.Attributes.Set( "SampleSize", Math.Clamp( SampleSize, 0.1f, 100f ) );
		_commandList.Draw( _gpuVertexBuffer, _material, 0, _gpuVertexCount );
	}

	private void Rebuild()
	{
		_vertices.Clear();

		var spacing = ComputeSpacing( LPVBounds.Size, Counts );
		var halfSize = MathF.Max( 0.001f, SampleSize );
		var rotation = GridTransform.Rotation;
		var normal = rotation * Vector3.Left;
		var tangent = new Vector4( rotation * Vector3.Up, -1f );

		//Bounds = LPVBounds.Transform( GridTransform ).Grow( halfSize );

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

					center += rotation * (probe?.Offset ?? Vector3.Zero);

					for ( int i = 0; i < 6; i++ )
					{
						_vertices.Add( new Vertex
						{
							Position = center,
							Normal = normal,
							Tangent = tangent,
							TexCoord0 = QuadUvs[QuadIndices[i]]
						} );
					}
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
