using Basalt.Gpu;
using System.IO;
using System.Runtime.InteropServices;

namespace Basalt.Backend;

/// <summary>
/// The built-in lit mesh program (<c>Shaders/forward.hlsl</c>) and its pipelines, made on first use per shader mode and
/// target formats. Its resources sit where vfx puts a shader's: set 0 for the vertex shader (view constants at b0,
/// <c>g_TransformBuffer</c> at t0), set 1 for the pixel shader (view constants, <c>DirectionalLightCB</c>,
/// <c>ViewLightingConfigV2</c> at b0 to b2, <c>BinnedLightBufferV2</c> at t0).
/// </summary>
internal sealed class MeshProgram : IDisposable
{
	/// <summary>What a draw pushes: the material's values.</summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct MaterialConstants
	{
		public System.Numerics.Vector4 Albedo;
		public uint ShadowSampler;
		public uint Flags;
	}

	public readonly record struct Key( bool Depth, Format Color, Format DepthFormat, int Samples );

	readonly Device gpu;
	readonly ShaderHandle vertexShader, pixelShader;
	readonly Dictionary<Key, PipelineHandle> pipelines = new();

	public BindingLayoutHandle VertexSet { get; }
	public BindingLayoutHandle PixelSet { get; }

	public MeshProgram( Device gpu )
	{
		this.gpu = gpu;
		vertexShader = gpu.CreateShader( Load( "forward.vs.spv" ), "Basalt mesh vs" );
		pixelShader = gpu.CreateShader( Load( "forward.ps.spv" ), "Basalt mesh ps" );

		VertexSet = gpu.CreateBindingLayout(
		[
			new BindingSlot( ShaderAbi.ConstantBuffer( 0 ), BindingType.UniformBuffer, ShaderStages.Vertex ),
			new BindingSlot( ShaderAbi.Texture( 0 ), BindingType.StorageBuffer, ShaderStages.Vertex ),
		] );

		PixelSet = gpu.CreateBindingLayout(
		[
			new BindingSlot( ShaderAbi.ConstantBuffer( 0 ), BindingType.UniformBuffer, ShaderStages.Fragment ),
			new BindingSlot( ShaderAbi.ConstantBuffer( 1 ), BindingType.UniformBuffer, ShaderStages.Fragment ),
			new BindingSlot( ShaderAbi.ConstantBuffer( 2 ), BindingType.UniformBuffer, ShaderStages.Fragment ),
			new BindingSlot( ShaderAbi.Texture( 0 ), BindingType.StorageBuffer, ShaderStages.Fragment ),
		] );
	}

	static byte[] Load( string name )
	{
		using var stream = typeof( MeshProgram ).Assembly.GetManifestResourceStream( $"Basalt.Backend.{name}" ) ?? throw new FileNotFoundException( $"No embedded shader {name}" );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	/// <summary>
	/// The pipeline for a mode into targets of these formats. Depth mode has no pixel shader and takes depth bias (shadow
	/// views set it; the prepass leaves it 0).
	/// </summary>
	public PipelineHandle Pipeline( in Key key )
	{
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var existing ) ) return existing;

			var info = new GraphicsPipelineInfo
			{
				VertexShader = vertexShader,
				VertexEntry = "MainVs",
				FragmentShader = key.Depth ? default : pixelShader,
				FragmentEntry = "MainPs",
				Sets = [VertexSet, PixelSet],
				VertexBuffers = [new VertexBufferLayout( Marshal.SizeOf<MeshVertex>() ), new VertexBufferLayout( 4, VertexStepRate.Instance )],
				Attributes =
				[
					new VertexAttribute( 0, 0, VertexFormat.Float3, 0 ),
					new VertexAttribute( 1, 0, VertexFormat.Float3, 12 ),
					new VertexAttribute( 3, 1, VertexFormat.UInt, 0 ),
				],
				Topology = PrimitiveTopology.TriangleList,
				// Counter-clockwise from outside, in Vulkan's framebuffer terms once the vertex shader has inverted Y.
				// No depth clamp: the prepass and forward must clip identically, or forward loses the depth test on
				// geometry crossing the near plane
				CullMode = CullMode.Back,
				FrontFace = FrontFace.CounterClockwise,
				DepthBias = key.Depth,
				DepthTest = key.DepthFormat != Format.Unknown,
				DepthWrite = key.DepthFormat != Format.Unknown,
				DepthCompare = CompareFunction.GreaterEqual,
				ColorFormats = key.Color == Format.Unknown ? [] : [key.Color],
				DepthFormat = key.DepthFormat,
				Samples = key.Samples,
				Name = key.Depth ? "Basalt mesh depth" : "Basalt mesh forward",
			};

			var pipeline = gpu.CreateGraphicsPipeline( info );
			pipelines[key] = pipeline;
			return pipeline;
		}
	}

	public void Dispose()
	{
		foreach ( var pipeline in pipelines.Values ) gpu.Destroy( pipeline );
		pipelines.Clear();
		gpu.Destroy( vertexShader );
		gpu.Destroy( pixelShader );
		gpu.Destroy( VertexSet );
		gpu.Destroy( PixelSet );
	}
}
