using Borealis.Gpu;
using System.IO;
using System.Numerics;
using N = System.Numerics;
using System.Runtime.InteropServices;

namespace BorealisGpuSample;

/// <summary>
/// What the sample draws, into any colour target: a compute dispatch writing a texture through the bindless storage
/// table, two textured, lit cubes with depth (one with an uploaded checkerboard, one with the compute texture), and a
/// triangle over them. The tests render it offscreen and read the pixels back.
/// </summary>
internal sealed class SampleScene : IDisposable
{
	[StructLayout( LayoutKind.Sequential )]
	struct Vertex( N.Vector3 position, N.Vector3 normal, N.Vector2 uv )
	{
		public N.Vector3 Position = position;
		public N.Vector3 Normal = normal;
		public N.Vector2 Uv = uv;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct MeshConstants
	{
		public Matrix4x4 ObjectToProjection;
		public N.Vector4 NormalRow0, NormalRow1, NormalRow2;
		public uint Texture;
		public uint Sampler;
		public N.Vector2 Pad;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct TriangleConstants
	{
		public N.Vector2 Offset;
		public float Scale;
		public float Pad;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct PatternConstants
	{
		public uint Target;
		public float Time;
		public uint Width, Height;
	}

	public const Format DepthFormat = Format.D32Float;
	public const int PatternSize = 256;

	/// <summary>The colour the target is cleared to.</summary>
	public static readonly N.Vector4 ClearColor = new( 0.05f, 0.06f, 0.08f, 1 );

	readonly Device device;
	readonly ShaderHandle[] shaders;
	readonly PipelineHandle meshPipeline, trianglePipeline, patternPipeline;
	readonly BufferHandle vertices, indices;
	readonly TextureHandle checker, pattern;
	readonly SamplerHandle sampler;
	TextureHandle depth;
	bool patternWritten;

	/// <summary>The colour format it draws into.</summary>
	public Format ColorFormat { get; }

	public SampleScene( Device device, Format colorFormat )
	{
		this.device = device;
		ColorFormat = colorFormat;

		var meshVs = device.CreateShader( Load( "mesh.vs.spv" ), "mesh.vs" );
		var meshPs = device.CreateShader( Load( "mesh.ps.spv" ), "mesh.ps" );
		var triangleVs = device.CreateShader( Load( "triangle.vs.spv" ), "triangle.vs" );
		var trianglePs = device.CreateShader( Load( "triangle.ps.spv" ), "triangle.ps" );
		var patternCs = device.CreateShader( Load( "pattern.cs.spv" ), "pattern.cs" );
		shaders = [meshVs, meshPs, triangleVs, trianglePs, patternCs];

		meshPipeline = device.CreateGraphicsPipeline( new GraphicsPipelineInfo
		{
			VertexShader = meshVs,
			VertexEntry = "MainVs",
			FragmentShader = meshPs,
			FragmentEntry = "MainPs",
			VertexBuffers = [new VertexBufferLayout( Marshal.SizeOf<Vertex>() )],
			Attributes = [new VertexAttribute( 0, 0, VertexFormat.Float3, 0 ), new VertexAttribute( 1, 0, VertexFormat.Float3, 12 ), new VertexAttribute( 2, 0, VertexFormat.Float2, 24 )],
			Topology = PrimitiveTopology.TriangleList,
			CullMode = CullMode.Back,
			FrontFace = FrontFace.CounterClockwise,
			DepthTest = true,
			DepthWrite = true,
			DepthCompare = CompareFunction.Greater,
			ColorFormats = [colorFormat],
			DepthFormat = DepthFormat,
			Samples = 1,
			Name = "Sample mesh",
		} );

		trianglePipeline = device.CreateGraphicsPipeline( new GraphicsPipelineInfo
		{
			VertexShader = triangleVs,
			VertexEntry = "MainVs",
			FragmentShader = trianglePs,
			FragmentEntry = "MainPs",
			Topology = PrimitiveTopology.TriangleList,
			ColorFormats = [colorFormat],
			DepthFormat = DepthFormat,
			Blend = [BlendState.AlphaBlend],
			Samples = 1,
			Name = "Sample triangle",
		} );

		patternPipeline = device.CreateComputePipeline( new ComputePipelineInfo { Shader = patternCs, Entry = "MainCs", Name = "Sample pattern" } );

		var (cubeVertices, cubeIndices) = Cube();
		vertices = device.CreateBuffer( new BufferInfo { Size = cubeVertices.Length * Marshal.SizeOf<Vertex>(), Usage = BufferUsage.Vertex, Name = "Cube vertices" } );
		indices = device.CreateBuffer( new BufferInfo { Size = cubeIndices.Length * sizeof( ushort ), Usage = BufferUsage.Index, Name = "Cube indices" } );
		device.WriteBuffer<Vertex>( vertices, cubeVertices );
		device.WriteBuffer<ushort>( indices, cubeIndices );

		checker = device.CreateTexture( TextureInfo.Texture2D( 64, 64, Format.RGBA8Unorm, name: "Checker" ) );
		device.WriteTexture( checker, Checkerboard( 64 ) );

		var patternInfo = TextureInfo.Texture2D( PatternSize, PatternSize, Format.RGBA8Unorm, name: "Pattern" );
		patternInfo.Usage |= TextureUsage.Storage;
		pattern = device.CreateTexture( patternInfo );

		sampler = device.CreateSampler( SamplerInfo.Linear( AddressMode.Wrap, 8 ) );
	}

	static byte[] Load( string name )
	{
		using var stream = typeof( SampleScene ).Assembly.GetManifestResourceStream( name ) ?? throw new FileNotFoundException( $"No embedded shader {name}" );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	static byte[] Checkerboard( int size )
	{
		var pixels = new byte[size * size * 4];
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				var light = ((x / 8) + (y / 8)) % 2 == 0;
				var i = (y * size + x) * 4;
				pixels[i + 0] = light ? (byte)230 : (byte)40;
				pixels[i + 1] = light ? (byte)230 : (byte)120;
				pixels[i + 2] = light ? (byte)230 : (byte)60;
				pixels[i + 3] = 255;
			}
		}

		return pixels;
	}

	/// <summary>A unit cube, counter-clockwise from outside, with a normal and uvs per face.</summary>
	static (Vertex[] Vertices, ushort[] Indices) Cube()
	{
		N.Vector3[] normals = [N.Vector3.UnitX, -N.Vector3.UnitX, N.Vector3.UnitY, -N.Vector3.UnitY, N.Vector3.UnitZ, -N.Vector3.UnitZ];
		var vertices = new List<Vertex>();
		var indices = new List<ushort>();

		foreach ( var n in normals )
		{
			// Two axes across the face, so (u, v, n) is right-handed
			var u = MathF.Abs( n.Y ) > 0.5f ? N.Vector3.UnitX : N.Vector3.Cross( N.Vector3.UnitY, n );
			var v = N.Vector3.Cross( n, u );
			var first = (ushort)vertices.Count;

			vertices.Add( new Vertex( (n - u - v) * 0.5f, n, new N.Vector2( 0, 1 ) ) );
			vertices.Add( new Vertex( (n + u - v) * 0.5f, n, new N.Vector2( 1, 1 ) ) );
			vertices.Add( new Vertex( (n + u + v) * 0.5f, n, new N.Vector2( 1, 0 ) ) );
			vertices.Add( new Vertex( (n - u + v) * 0.5f, n, new N.Vector2( 0, 0 ) ) );

			indices.AddRange( [first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)] );
		}

		return (vertices.ToArray(), indices.ToArray());
	}

	void EnsureDepth( int width, int height )
	{
		if ( depth.IsValid )
		{
			var info = device.InfoOf( depth );
			if ( info.Width == width && info.Height == height ) return;
			device.Destroy( depth );
		}

		depth = device.CreateTexture( TextureInfo.RenderTarget( width, height, DepthFormat, name: "Sample depth" ) );
	}

	/// <summary>
	/// Record the scene into <paramref name="target"/>, which goes from <paramref name="from"/> to <paramref name="to"/>.
	/// </summary>
	public void Render( CommandList list, TextureHandle target, ImageLayout from, ImageLayout to, float time )
	{
		var info = device.InfoOf( target );
		EnsureDepth( info.Width, info.Height );

		// Compute writes the pattern through the bindless storage table, then hands it to pixel shaders
		list.BeginLabel( "Pattern" );
		list.Barrier( TextureBarrier.Transition( pattern, patternWritten ? ImageLayout.ShaderRead : ImageLayout.Undefined, ImageLayout.General,
			PipelineStage.FragmentShader, MemoryAccess.None, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite ) );
		list.SetPipeline( patternPipeline );
		list.PushConstants( new PatternConstants { Target = (uint)device.StorageIndex( pattern ), Time = time, Width = PatternSize, Height = PatternSize } );
		list.Dispatch( PatternSize / 8, PatternSize / 8 );
		list.Barrier( TextureBarrier.Transition( pattern, ImageLayout.General, ImageLayout.ShaderRead,
			PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.FragmentShader, MemoryAccess.ShaderRead ) );
		patternWritten = true;
		list.EndLabel();

		list.Barrier( TextureBarrier.Transition( target, from, ImageLayout.ColorTarget,
			PipelineStage.ColorTargetOutput, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.DepthTarget,
			PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetWrite,
			PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetWrite | MemoryAccess.DepthTargetRead ) );

		list.BeginLabel( "Scene" );
		list.BeginRendering( [ColorTarget.Of( target, ClearColor )], DepthTarget.Of( depth, 0 ) );

		// Reverse-Z: near at 1, far at 0, as Borealis renders
		var aspect = info.Width / (float)info.Height;
		var view = Matrix4x4.CreateLookAt( new N.Vector3( 0, 1.2f, 3.2f ), new N.Vector3( 0, 0, 0 ), N.Vector3.UnitY );
		var reverseZ = new Matrix4x4( 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 1, 1 );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, aspect, 0.1f, 100 ) * reverseZ;

		list.SetPipeline( meshPipeline );
		list.SetVertexBuffer( 0, vertices );
		list.SetIndexBuffer( indices, IndexFormat.UInt16 );
		DrawCube( list, view * projection, Matrix4x4.CreateRotationY( time * 0.7f ) * Matrix4x4.CreateTranslation( -0.8f, 0, 0 ), checker );
		DrawCube( list, view * projection, Matrix4x4.CreateRotationX( time * 0.5f ) * Matrix4x4.CreateRotationY( -time ) * Matrix4x4.CreateTranslation( 0.8f, 0, 0 ), pattern );

		list.SetPipeline( trianglePipeline );
		list.PushConstants( new TriangleConstants { Offset = new N.Vector2( 0, 0.55f ), Scale = 0.35f } );
		list.Draw( 3 );

		list.EndRendering();
		list.EndLabel();

		list.Barrier( TextureBarrier.Transition( target, ImageLayout.ColorTarget, to,
			PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
	}

	void DrawCube( CommandList list, Matrix4x4 worldToProjection, Matrix4x4 objectToWorld, TextureHandle texture )
	{
		list.PushConstants( new MeshConstants
		{
			ObjectToProjection = objectToWorld * worldToProjection,
			NormalRow0 = new N.Vector4( objectToWorld.M11, objectToWorld.M12, objectToWorld.M13, 0 ),
			NormalRow1 = new N.Vector4( objectToWorld.M21, objectToWorld.M22, objectToWorld.M23, 0 ),
			NormalRow2 = new N.Vector4( objectToWorld.M31, objectToWorld.M32, objectToWorld.M33, 0 ),
			Texture = (uint)device.BindlessIndex( texture ),
			Sampler = (uint)device.SamplerIndex( sampler ),
		} );

		list.DrawIndexed( 36 );
	}

	public void Dispose()
	{
		foreach ( var shader in shaders ) device.Destroy( shader );
		device.Destroy( meshPipeline );
		device.Destroy( trianglePipeline );
		device.Destroy( patternPipeline );
		device.Destroy( vertices );
		device.Destroy( indices );
		device.Destroy( checker );
		device.Destroy( pattern );
		device.Destroy( sampler );
		if ( depth.IsValid ) device.Destroy( depth );
	}
}
