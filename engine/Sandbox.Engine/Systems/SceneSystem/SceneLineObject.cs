using Sandbox.Rendering;
using System.Numerics;

namespace Sandbox;

/// <summary>
/// A scene object which is used to draw lines
/// </summary>
public class SceneLineObject : SceneCustomObject
{
	public enum CapStyle
	{
		None = 0,
		[Icon( "signal_cellular_4_bar" )]
		Triangle = 1,
		[Icon( "label_important" )]
		Arrow = 2,
		[Icon( "fiber_smart_record" )]
		Rounded = 3
	}

	public enum FaceMode
	{
		[Icon( "settings_ethernet" )]
		Camera = 0,
		[Icon( "rotate_90_degrees_cw" )]
		Normal = 1,
		[Icon( "circle" )]
		Cylinder = 2,
	}

	[Obsolete( "Use Material property instead", false )]
	public Texture LineTexture
	{
		get => Attributes.GetTexture( "BaseTexture" );
		set => Attributes.Set( "BaseTexture", value ?? Texture.White );
	}

	private static Material _defaultMaterial = Material.FromShader( "line" );
	public Material Material = _defaultMaterial;

	public CapStyle StartCap
	{
		get => (CapStyle)Attributes.GetInt( "StartCap" );
		set => Attributes.Set( "StartCap", (int)value );
	}

	public CapStyle EndCap
	{
		get => (CapStyle)Attributes.GetInt( "EndCap" );
		set => Attributes.Set( "EndCap", (int)value );
	}

	public FaceMode Face
	{
		get => (FaceMode)_csAttributes.GetInt( "FaceMode" );
		set => _csAttributes.Set( "FaceMode", (int)value );
	}

	public bool Wireframe
	{
		get => Attributes.GetComboBool( "D_WIREFRAME" );
		set => Attributes.SetCombo( "D_WIREFRAME", value );
	}

	public bool Lighting
	{
		get => Attributes.GetComboBool( "D_ENABLE_LIGHTING" );
		set => Attributes.SetCombo( "D_ENABLE_LIGHTING", value );
	}

	[Obsolete( "Use Sampler State property instead" )]
	public bool Clamped
	{
		get => _clamped;
		set
		{
			_clamped = value;
			if ( _clamped )
			{
				Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( ClampSampler ) );
			}
			else
			{
				Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( WrapSampler ) );
			}
		}
	}

	private bool _clamped = false;

	public SamplerState SamplerState
	{
		get => _samplerState;
		set
		{
			_samplerState = value;
			Attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( _samplerState ) );
		}
	}

	private SamplerState _samplerState = new() { Filter = FilterMode.Anisotropic, MaxAnisotropy = 8, AddressModeU = TextureAddressMode.Wrap, AddressModeV = TextureAddressMode.Wrap };

	public int Smoothness
	{
		get => Attributes.GetInt( "Smoothness" );
		set => Attributes.Set( "Smoothness", value );
	}

	public bool Opaque
	{
		set
		{
			Attributes.SetCombo( "D_OPAQUE", value ? 1 : 0 );
			Flags.IsOpaque = value;
			Flags.IsTranslucent = !value;
		}
	}

	private int _roundedCapSegments = 8;
	private int _tessellationLevel = 1; // Number of subdivisions between left and right sides of each segment

	/// <summary>
	/// Number of tessellation subdivisions across the width of each line segment.
	/// 1 = no tessellation (just left and right), 2 = one subdivision in the middle, etc.
	/// Higher values create smoother curves and more detailed geometry but use more vertices.
	/// </summary>
	public int TessellationLevel
	{
		get => _tessellationLevel;
		set => _tessellationLevel = Math.Max( 1, value );
	}

	private struct LinePoint( uint offset, in Vector3 position, in Vector3 normal, Color color, float width, float textureCoord )
	{
		public uint Offset = offset;
		public Vector3 Position = position;
		public Vector3 Normal = normal;
		public Color Color = color;
		public float Width = width;
		public float TextureCoord = textureCoord;
	}

	private struct LineVertex()
	{
		[VertexLayout.Position] public Vector3 Position = default;
		[VertexLayout.Normal] public Vector3 Normal = default;
		[VertexLayout.Tangent] public Vector3 Tangent = default;
		[VertexLayout.Color] public Color Color = default;
		[VertexLayout.TexCoord] public Vector2 TextureCoord = default;
	}

	private static ComputeShader _cs = new( "line_cs" );
	private RenderAttributes _csAttributes = new();
	private GpuBuffer<LinePoint> _pointBuffer;
	private GpuBuffer<LineVertex> _vertexBuffer;
	private GpuBuffer<uint> _indexBuffer;
	private static readonly SamplerState WrapSampler = new() { Filter = FilterMode.Anisotropic, MaxAnisotropy = 8, AddressModeU = TextureAddressMode.Wrap, AddressModeV = TextureAddressMode.Wrap };
	private static readonly SamplerState ClampSampler = new() { Filter = FilterMode.Anisotropic, MaxAnisotropy = 8, AddressModeU = TextureAddressMode.Clamp, AddressModeV = TextureAddressMode.Clamp };
	private LinePoint[] _points = [];
	private readonly CommandList _commandList = new( "SceneLine" );
	private BBox _bounds;

	private int _pointCount;
	private int _pointCapacity;

	private int _indexCapacity;
	private int _vertexCapacity;

	public SceneLineObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
#pragma warning disable CS0618
		LineTexture = Texture.White;
#pragma warning restore CS0618
	}

	public void StartLine()
	{
		_pointCount = 0;

		_bounds = BBox.FromPositionAndSize( Transform.Position, 10 );
	}

	public void AddLinePoint( in Vector3 pos, Color color, float width )
	{
		AddLinePoint( pos, color, width, _pointCount / 20.0f );
	}

	public void AddLinePoint( in Vector3 pos, Color color, float width, float textureCoord )
	{
		AddLinePoint( pos, Vector3.Up, color, width, textureCoord );
	}

	public void AddLinePoint( in Vector3 pos, in Vector3 normal, Color color, float width, float textureCoord )
	{
		if ( _pointCount > 0 && _points[_pointCount - 1].Position.DistanceSquared( pos ) < 0.01f )
			return;

		if ( _pointCount >= _points.Length )
		{
			Array.Resize( ref _points, Math.Max( 8, _points.Length * 2 ) );
		}

		_points[_pointCount] = new LinePoint( 0, pos, normal, color, width, textureCoord );

		_pointCount++;
		_bounds = _bounds.AddPoint( pos );
	}

	/// <summary>
	/// Calculate the number of vertices needed for a specific cap type
	/// </summary>
	private int GetCapVertexCount( CapStyle capStyle )
	{
		return capStyle switch
		{
			CapStyle.None => 0,
			CapStyle.Triangle => 1, // 1 additional vertex (center)
			CapStyle.Arrow => 3, // Additional triangle
			CapStyle.Rounded => _roundedCapSegments + 1 + 1, // Multiple vertices for the rounded cap
			_ => 0
		};
	}

	/// <summary>
	/// Calculate the number of indices needed for a specific cap type
	/// </summary>
	private int GetCapIndexCount( CapStyle capStyle )
	{
		return capStyle switch
		{
			CapStyle.None => 0,
			CapStyle.Triangle => 3, // One triangle (3 indices)
			CapStyle.Arrow => 3,    // One triangle (3 indices)
			CapStyle.Rounded => _roundedCapSegments * 3, // Multiple triangles (3 indices each)
			_ => 0
		};
	}

	public void EndLine()
	{
		Bounds = _bounds;

		if ( _pointCount == 0 )
			return;

		var startCap = StartCap;
		var endCap = EndCap;

		if ( _pointCount > _pointCapacity )
		{
			MainThread.QueueDispose( _pointBuffer );

			_pointCapacity = (int)(_pointCount * 1.5);
			_pointBuffer = new GpuBuffer<LinePoint>( _pointCapacity, GpuBuffer.UsageFlags.Structured );
		}

		if ( CalculateIndexCount() > _indexCapacity )
		{
			MainThread.QueueDispose( _indexBuffer );

			_indexCapacity = (int)(CalculateIndexCount() * 1.5);
			_indexBuffer = new GpuBuffer<uint>( _indexCapacity, GpuBuffer.UsageFlags.Index | GpuBuffer.UsageFlags.Structured );
		}

		if ( CalculateVertexCount() > _vertexCapacity )
		{
			MainThread.QueueDispose( _vertexBuffer );

			_vertexCapacity = (int)(CalculateVertexCount() * 1.5);
			_vertexBuffer = new GpuBuffer<LineVertex>( _vertexCapacity, GpuBuffer.UsageFlags.Vertex | GpuBuffer.UsageFlags.Structured );
		}

		if ( Face == FaceMode.Cylinder )
		{
			// Unsupported on Cylinder
			_csAttributes.Set( "StartCap", 0 );
			_csAttributes.Set( "EndCap", 0 );
		}
		else
		{
			_csAttributes.Set( "StartCap", (int)startCap );
			_csAttributes.Set( "EndCap", (int)endCap );
		}

		_csAttributes.Set( "TessellationLevel", _tessellationLevel );
		_csAttributes.Set( "RoundedCapSegments", _roundedCapSegments );

		BuildCommandList();
	}

	public void Clear()
	{
		_pointCount = 0;

		_pointCapacity = 0;
		_vertexCapacity = 0;
		_indexCapacity = 0;

		MainThread.QueueDispose( _pointBuffer );
		MainThread.QueueDispose( _vertexBuffer );
		MainThread.QueueDispose( _indexBuffer );

		_pointBuffer = default;
		_vertexBuffer = default;
		_indexBuffer = default;

		_commandList.Reset();
	}

	private int CalculateIndexCount()
	{
		// With tessellation, each segment creates tessellationLevel * 2 triangles
		// (tessellationLevel quads, each quad = 2 triangles, each triangle = 3 indices)
		int indicesPerSegment = _tessellationLevel * 2 * 3;
		int lineIndices = (_pointCount - 1) * indicesPerSegment;

		// Add indices for caps
		int capIndices = GetCapIndexCount( StartCap ) + GetCapIndexCount( EndCap );

		return lineIndices + capIndices;
	}

	private int CalculateVertexCount()
	{
		// Basic line vertices (tessellationLevel + 1 vertices per point to create tessellationLevel segments across width)
		int lineVertices = _pointCount * (_tessellationLevel + 1);

		// Add vertices for caps
		int capVertices = GetCapVertexCount( StartCap ) + GetCapVertexCount( EndCap );

		return lineVertices + capVertices;
	}

	private void BuildCommandList()
	{
		_commandList.Reset();

		var indexCount = CalculateIndexCount();

		if ( indexCount < 6 )
			return;

		if ( !_pointBuffer.IsValid() || !_vertexBuffer.IsValid() || !_indexBuffer.IsValid() )
			return;

		// Upload point data to GPU (deferred to render thread)
		_commandList.SetBufferData( _pointBuffer, _points, 0, _pointCount );

		// Compute shader attributes
		_commandList.Attributes.Set( "PointBuffer", (GpuBuffer)_pointBuffer );
		_commandList.Attributes.Set( "VertexBuffer", (GpuBuffer)_vertexBuffer );
		_commandList.Attributes.Set( "IndexBuffer", (GpuBuffer)_indexBuffer );
		_commandList.Attributes.Set( "PointCount", _pointCount );
		_commandList.Attributes.Set( "FaceMode", _csAttributes.GetInt( "FaceMode" ) );
		_commandList.Attributes.Set( "StartCap", _csAttributes.GetInt( "StartCap" ) );
		_commandList.Attributes.Set( "EndCap", _csAttributes.GetInt( "EndCap" ) );
		_commandList.Attributes.Set( "TessellationLevel", _csAttributes.GetInt( "TessellationLevel" ) );
		_commandList.Attributes.Set( "RoundedCapSegments", _csAttributes.GetInt( "RoundedCapSegments" ) );
		_commandList.DispatchCompute( _cs, _pointCount, 1, 1 );

		_commandList.ResourceBarrierTransition( (GpuBuffer)_vertexBuffer, ResourceState.UnorderedAccess, ResourceState.VertexOrIndexBuffer );
		_commandList.ResourceBarrierTransition( (GpuBuffer)_indexBuffer, ResourceState.UnorderedAccess, ResourceState.VertexOrIndexBuffer );

		// Setup and draw using sceneobjects attributes... this needs a good refactor.
		if ( Lighting )
		{
			_commandList.SetupLighting( this, Attributes );
		}

		_commandList.DrawIndexed( _vertexBuffer, (GpuBuffer)_indexBuffer, Material, 0, indexCount, Attributes );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();
		_commandList.ExecuteOnRenderThread();
	}
}
