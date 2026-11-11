using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Shaders;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BasaltGpuTests;

/// <summary>
/// Reading s&amp;box's compiled shaders (<c>.shader_c</c>) from the checkout's <c>game/core/shaders</c>, which Bootstrap
/// builds. Inconclusive where they haven't been built.
/// </summary>
[TestClass]
public class ShaderFileTests
{
	internal static ContentFiles Files
	{
		get
		{
			var files = ContentFiles.FindGame();
			if ( files.Find( "shaders/complex.shader" ) is null ) Assert.Inconclusive( "Compiled shaders aren't built (run Bootstrap)" );
			return files;
		}
	}

	/// <summary>
	/// Every combo of depth_only decompresses to valid SPIR-V that reflects, and the vertex shader reads the instance
	/// transform id and position by semantic.
	/// </summary>
	[TestMethod]
	public void DepthOnlyCombosRead()
	{
		var shader = ShaderFile.Read( Files.Read( "shaders/depth_only.shader" ), "depth_only" );
		Assert.AreEqual( "Depth", shader.Modes.Single().Name );

		foreach ( var type in new[] { ShaderProgramType.Vertex, ShaderProgramType.Pixel } )
		{
			var program = shader[type];
			Assert.IsTrue( program.ComboCount > 0 );
			foreach ( var id in program.ComboIds.ToArray() )
			{
				foreach ( var module in program.LoadCombo( id ).Shaders.Where( s => s is not null ) )
				{
					Assert.AreEqual( 0x07230203u, BitConverter.ToUInt32( module.Spirv ) );
					SpirvReflection.Read( module.Spirv );
				}
			}
		}

		var vertex = shader[ShaderProgramType.Vertex].LoadCombo( 0 ).Dynamic( 0 ).Shader;
		var semantics = vertex.AttributeMap.Select( s => ((VertexSemantic)(s >> 4), s & 15) ).ToArray();
		CollectionAssert.Contains( semantics, (VertexSemantic.Position, 0) );
		CollectionAssert.Contains( semantics, (VertexSemantic.TexCoord, 13) );
		Assert.AreEqual( "MainVs", vertex.EntryPoint( ShaderProgramType.Vertex ) );
	}

	/// <summary>
	/// complex.shader: its modes and features, the constant buffer layouts it was compiled against, and every pixel
	/// shader combo - shared modules with specialization constants included - read and reflected.
	/// </summary>
	[TestMethod]
	public void ComplexCombosRead()
	{
		var shader = ShaderFile.Read( Files.Read( "shaders/complex.shader" ), "complex" );
		CollectionAssert.AreEqual( new[] { "Forward", "Depth", "ToolsShadingComplexity" }, shader.Modes.Select( m => m.Name ).ToArray() );
		Assert.AreEqual( ("S_MODE_DEPTH", 1), shader.Mode( "Depth" ).Settings.Single() );
		Assert.IsTrue( shader.FeatureIndex( "F_ALPHA_TEST" ) >= 0 );

		// The layouts Basalt's mirrors must match
		var pixel = shader[ShaderProgramType.Pixel];
		Assert.AreEqual( 1088, pixel.ConstantBuffers.Single( c => c.Name == "PerViewConstantBuffer_t" ).Size );
		Assert.AreEqual( 416, pixel.ConstantBuffers.Single( c => c.Name == "DirectionalLightCB" ).Size );
		Assert.AreEqual( 4128, pixel.ConstantBuffers.Single( c => c.Name == "ViewLightingConfigV2" ).Size );

		var timer = Stopwatch.StartNew();
		var modules = 0;
		var specialized = 0;
		var reflected = new HashSet<byte[]>( ReferenceEqualityComparer.Instance );
		foreach ( var id in pixel.ComboIds.ToArray() )
		{
			foreach ( var module in pixel.LoadCombo( id ).Shaders.Where( s => s is not null ) )
			{
				modules++;
				if ( module.Constants.Length > 0 ) specialized++;
				if ( reflected.Add( module.Shared.Spirv ) ) SpirvReflection.Read( module.Spirv );
			}
		}

		Console.WriteLine( $"{pixel.ComboCount} pixel combos, {modules} shaders ({specialized} specialized, {reflected.Count} distinct modules) in {timer.ElapsedMilliseconds} ms" );
		Assert.IsTrue( specialized > 0, "complex's ShaderConstant() combos come as specialized shared modules" );
	}

	/// <summary>
	/// A compiled material's KV3: its shader, features and parameters, and the compiled textures it names, mips
	/// (LZ4 or Zstandard compressed, or raw) all present.
	/// </summary>
	[TestMethod]
	public void MaterialAndTexturesRead()
	{
		var files = Files;
		var path = files.Find( "materials/default/white.vmat" ) ?? files.Find( "materials/default.vmat" );
		if ( path is null ) Assert.Inconclusive( "No compiled core materials" );

		var material = Basalt.Source2.Materials.MaterialFile.Load( path );
		Assert.AreEqual( "shaders/complex.shader", material.Shader );
		Assert.IsTrue( material.Textures.ContainsKey( "g_tColor" ) );
		Assert.AreEqual( 4, material.Value( "g_vColorTint" ).Length );

		foreach ( var texture in material.Textures.Values )
		{
			var file = files.Find( texture );
			if ( file is null ) continue;
			var vtex = Basalt.Source2.Materials.TextureFile.Load( file );
			Assert.AreNotEqual( Format.Unknown, vtex.Format, texture );
			for ( int mip = 0; mip < vtex.Mips; mip++ )
			{
				var w = Math.Max( vtex.Width >> mip, 1 );
				var h = Math.Max( vtex.Height >> mip, 1 );
				Assert.AreEqual( vtex.Format.RowPitch( w ) * vtex.Format.Rows( h ) * vtex.Slices, vtex.MipData[mip].Length, $"{texture} mip {mip}" );
			}
		}
	}

	/// <summary>
	/// Combo ids follow the material system: the Depth mode sets S_MODE_DEPTH, a feature sets its static combo, and every
	/// chosen combo was compiled.
	/// </summary>
	[TestMethod]
	public void ComboIdsFollowModeAndFeatures()
	{
		var shader = ShaderFile.Read( Files.Read( "shaders/complex.shader" ), "complex" );
		var pixel = shader[ShaderProgramType.Pixel];
		var features = new int[shader.Features.StaticCombos.Length];

		var forward = shader.StaticComboId( pixel, shader.Mode( "Forward" ), features );
		var depth = shader.StaticComboId( pixel, shader.Mode( "Depth" ), features );
		var modeDepth = pixel.StaticCombos.Single( c => c.Name == "S_MODE_DEPTH" );
		Assert.AreEqual( forward + modeDepth.Stride, depth );

		features[shader.FeatureIndex( "F_ALPHA_TEST" )] = 1;
		var alphaTest = shader.StaticComboId( pixel, shader.Mode( "Forward" ), features );
		Assert.AreEqual( forward + pixel.StaticCombos.Single( c => c.Name == "S_ALPHA_TEST" ).Stride, alphaTest );

		Assert.IsTrue( pixel.HasCombo( forward ) && pixel.HasCombo( depth ) && pixel.HasCombo( alphaTest ) );
	}
}

/// <summary>
/// Drawing with s&amp;box's compiled shaders on Basalt.Gpu, offscreen with validation: pipelines, layouts and bindings all
/// come from the <c>.shader_c</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ShaderDrawTests : GpuTest
{
	const int Size = 64;

	[StructLayout( LayoutKind.Sequential )]
	struct Vertex
	{
		public Vector3 Position;
		public Vector2 Uv;
		public Vector4 Normal;
		public Vector4 Tangent;
	}

	/// <summary>
	/// depth_only's Depth mode has no pixel shader; it writes a quad's depth.
	/// </summary>
	[TestMethod]
	public void DepthOnlyWritesDepth()
	{
		using var library = new ShaderLibrary( Device, ShaderFileTests.Files );
		using var defaults = new ShaderDefaults( Device );
		var shader = library.Load( "shaders/depth_only.shader" );
		var variant = library.Variant( shader, "Depth", new int[shader.Features.StaticCombos.Length] );
		Assert.IsNull( variant.Pixel.Bytecode, "no pixel shader without alpha test" );

		var depth = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32Float, name: "Depth" ) );
		Draw( library, defaults, variant, default, depth );

		var pixels = ReadBack( depth, ImageLayout.DepthTarget );
		Assert.AreEqual( 0.5f, BitConverter.ToSingle( pixels, (Size / 2 * Size + Size / 2) * 4 ), 1e-4f, "the quad's depth" );
		Assert.AreEqual( 0f, BitConverter.ToSingle( pixels, 0 ), "the corner stays cleared" );
	}

	/// <summary>
	/// complex's Forward mode (with its specialization constants and the material constant buffer filled from the
	/// shader's defaults) colours a quad, and its Depth mode draws depth, both through layouts reflected from the SPIR-V.
	/// </summary>
	[TestMethod]
	public void ComplexDrawsForwardAndDepth()
	{
		using var library = new ShaderLibrary( Device, ShaderFileTests.Files );
		using var defaults = new ShaderDefaults( Device );
		var shader = library.Load( "shaders/complex.shader" );
		var features = new int[shader.Features.StaticCombos.Length];

		var forward = library.Variant( shader, "Forward", features );
		Assert.IsNotNull( forward.Pixel.Bytecode );
		Assert.IsTrue( forward.Resources.Any( r => r.Kind == ShaderResourceKind.MaterialConstants && r.Set == ShaderAbi.FragmentSet ) );
		Assert.IsTrue( forward.Resources.Any( r => r.Kind == ShaderResourceKind.Texture && r.Variable.Name == "g_tColor" ) );
		Assert.IsTrue( forward.Resources.Any( r => r.Kind == ShaderResourceKind.ConstantBuffer && r.Name == "PerViewConstantBuffer_t" ) );
		Assert.IsTrue( forward.Resources.Any( r => r.Kind == ShaderResourceKind.Buffer && r.Name == "g_TransformBuffer" ) );

		var color = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.RGBA16Float, name: "Colour" ) );
		var depth = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32FloatS8Uint, name: "Depth" ) );
		Draw( library, defaults, forward, color, depth );

		var pixels = ReadBack( color, ImageLayout.ColorTarget );
		var centre = Half4( pixels, Size / 2, Size / 2 );
		var corner = Half4( pixels, 0, 0 );
		Assert.AreEqual( new Vector4( 1, 0, 1, 1 ), corner, "the corner keeps the clear colour" );
		Assert.AreNotEqual( corner, centre, "the quad is shaded" );

		var depthMode = library.Variant( shader, "Depth", features );
		var depthOnly = Device.CreateTexture( TextureInfo.RenderTarget( Size, Size, Format.D32Float, name: "Depth only" ) );
		Draw( library, defaults, depthMode, default, depthOnly );
		var depthPixels = ReadBack( depthOnly, ImageLayout.DepthTarget );
		Assert.AreEqual( 0.5f, BitConverter.ToSingle( depthPixels, (Size / 2 * Size + Size / 2) * 4 ), 1e-4f );
	}

	/// <summary>
	/// Asking twice for the same mode, features, combos and specialization values gives one variant, so materials that
	/// resolve to it share its layouts and pipelines; other features, combos or values give another.
	/// </summary>
	[TestMethod]
	public void EqualVariantsAreShared()
	{
		using var library = new ShaderLibrary( Device, ShaderFileTests.Files );
		var shader = library.Load( "shaders/complex.shader" );
		var features = new int[shader.Features.StaticCombos.Length];

		var first = library.Variant( shader, "Forward", features );
		var second = library.Variant( shader, "Forward", (int[])features.Clone() );
		Assert.AreSame( first, second, "the same features give the same variant" );
		Assert.AreNotSame( first, library.Variant( shader, "Depth", features ), "another mode is another variant" );
		Assert.AreNotSame( first, library.Variant( shader, "Forward", features, attributes: name => name == "ToolsVisMode" ? 10 : null ), "other specialization values are another variant" );

		var alphaTest = shader.FeatureIndex( "F_ALPHA_TEST" );
		if ( alphaTest >= 0 )
		{
			var tested = (int[])features.Clone();
			tested[alphaTest] = 1;
			Assert.AreNotSame( first, library.Variant( shader, "Forward", tested ), "other features are another variant" );
		}

		library.ShareVariants = false;
		Assert.AreNotSame( first, library.Variant( shader, "Forward", features ), "unshared, each request makes its own" );
	}

	static Vector4 Half4( byte[] pixels, int x, int y )
	{
		var i = (y * Size + x) * 8;
		return new Vector4( (float)BitConverter.ToHalf( pixels, i ), (float)BitConverter.ToHalf( pixels, i + 2 ), (float)BitConverter.ToHalf( pixels, i + 4 ), (float)BitConverter.ToHalf( pixels, i + 6 ) );
	}

	/// <summary>
	/// Draw a quad at clip-space depth 0.5 with a variant: identity transforms and view, every binding either one of
	/// these or a default.
	/// </summary>
	void Draw( ShaderLibrary library, ShaderDefaults defaults, ShaderVariant variant, TextureHandle color, TextureHandle depth )
	{
		var depthFormat = Device.InfoOf( depth ).Format;
		Format[] colors = color.IsValid ? [Device.InfoOf( color ).Format] : [];

		// Mesh: one stream with what complex reads, the instance id stream, the default stream
		var vertices = new Vertex[4];
		for ( int i = 0; i < 4; i++ )
		{
			var x = (i & 1) == 0 ? -0.5f : 0.5f;
			var y = (i & 2) == 0 ? -0.5f : 0.5f;
			vertices[i] = new Vertex { Position = new Vector3( x, y, 0.5f ), Uv = new Vector2( x + 0.5f, y + 0.5f ), Normal = new Vector4( 0, 0, 1, 0 ), Tangent = new Vector4( 1, 0, 0, 1 ) };
		}

		var vertexBuffer = Device.CreateBuffer( new BufferInfo { Size = 4 * Marshal.SizeOf<Vertex>(), Usage = BufferUsage.Vertex } );
		var indexBuffer = Device.CreateBuffer( new BufferInfo { Size = 6 * 4, Usage = BufferUsage.Index } );
		var instanceBuffer = Device.CreateBuffer( new BufferInfo { Size = 4, Usage = BufferUsage.Vertex } );
		Device.WriteBuffer<Vertex>( vertexBuffer, vertices );
		Device.WriteBuffer<uint>( indexBuffer, [0, 1, 2, 2, 1, 3] );
		Device.WriteBuffer<uint>( instanceBuffer, [0] );

		// One identity transform: three rows, alpha 1, a white tint
		var transforms = Device.CreateBuffer( new BufferInfo { Size = 64, Usage = BufferUsage.Storage } );
		var entry = new float[16];
		entry[0] = entry[5] = entry[10] = entry[12] = 1;
		var bytes = MemoryMarshal.AsBytes( entry.AsSpan() ).ToArray();
		BitConverter.TryWriteBytes( bytes.AsSpan( 52 ), 0xFFFFFFu );
		Device.WriteBuffer( transforms, bytes );

		var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input => input.Semantic switch
		{
			VertexSemantic.Position => new VertexAttribute( 0, 0, VertexFormat.Float3, 0 ),
			VertexSemantic.TexCoord when input.SemanticIndex == 0 => new VertexAttribute( 0, 0, VertexFormat.Float2, 12 ),
			VertexSemantic.Normal => new VertexAttribute( 0, 0, VertexFormat.Float4, 20 ),
			VertexSemantic.Tangent => new VertexAttribute( 0, 0, VertexFormat.Float4, 36 ),
			VertexSemantic.TexCoord when input.SemanticIndex == 13 => new VertexAttribute( 0, 1, VertexFormat.UInt, 0 ),
			_ => null,
		}, defaultBuffer: 2 );

		VertexBufferLayout[] streams = [new( Marshal.SizeOf<Vertex>() ), new( 4, VertexStepRate.Instance ), new( 0 )];
		var pipeline = Device.CreateGraphicsPipeline( variant.PipelineInfo( streams, attributes, colors, depthFormat, 1, false, null ) );

		Device.SetBindlessBuffer( ShaderAbi.BindlessPipelineTextures, defaults.Storage );
		Device.SetBindlessBuffer( ShaderAbi.BindlessVertexCache, defaults.Storage );

		var list = Device.BeginCommandList( QueueType.Graphics, variant.ToString() );
		if ( color.IsValid ) list.Barrier( TextureBarrier.Transition( color, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.DepthTarget, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetWrite ) );

		// Constants: the view's matrices identity, so the quad's positions are clip space
		var view = new byte[1088];
		var identity = ShaderDefaults.Floats( Matrix4x4.Identity );
		var viewLayout = variant.Vertex.Program.ConstantBuffers.Single( c => c.Name == "PerViewConstantBuffer_t" );
		foreach ( var name in new[] { "g_matWorldToProjection", "g_matWorldToView", "g_matViewToProjection", "g_matProjectionToWorld" } ) ShaderDefaults.Write( viewLayout, view, name, identity );
		var viewUpload = list.AllocateUpload( view.Length, Device.Capabilities.UniformAlignment );
		view.CopyTo( viewUpload.Data );

		list.BeginRendering( color.IsValid ? [ColorTarget.Of( color, new Vector4( 1, 0, 1, 1 ) )] : [], new DepthTarget { Texture = depth, Load = LoadOp.Clear, Store = StoreOp.Store, ClearDepth = 0 } );
		list.SetViewport( 0, 0, Size, Size );
		list.SetScissor( 0, 0, Size, Size );
		list.SetPipeline( pipeline );
		if ( variant.State.StencilTest && depthFormat.HasStencil() ) list.SetStencilReference( (byte)variant.State.StencilReference );

		for ( int set = 0; set < variant.Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;
			var bindings = new List<Binding>();
			foreach ( var resource in variant.Resources.Where( r => r.Set == set ) )
			{
				var stage = set == ShaderAbi.FragmentSet ? variant.Pixel : variant.Vertex;
				switch ( resource.Kind )
				{
					case ShaderResourceKind.MaterialConstants:
						var material = list.AllocateUpload( stage.MaterialBuffer.Size, Device.Capabilities.UniformAlignment );
						stage.WriteMaterialConstants( material.Data[..stage.MaterialBuffer.Size] );
						bindings.Add( Binding.Uniform( resource.Binding, material.Buffer, material.Offset, stage.MaterialBuffer.Size ) );
						break;
					case ShaderResourceKind.ConstantBuffer when resource.Name == "PerViewConstantBuffer_t":
						bindings.Add( Binding.Uniform( resource.Binding, viewUpload.Buffer, viewUpload.Offset, view.Length ) );
						break;
					case ShaderResourceKind.Buffer when resource.Name == "g_TransformBuffer":
						bindings.Add( Binding.Storage( resource.Binding, transforms ) );
						break;
					default:
						bindings.Add( defaults.For( resource ) );
						break;
				}
			}

			list.SetBindings( set, variant.Sets[set], bindings.ToArray() );
		}

		list.SetVertexBuffer( 0, vertexBuffer );
		list.SetVertexBuffer( 1, instanceBuffer );
		list.SetVertexBuffer( 2, defaults.VertexDefaults );
		list.SetIndexBuffer( indexBuffer, IndexFormat.UInt32 );
		list.DrawIndexed( 6 );
		list.EndRendering();
		Device.Wait( Device.Submit( list ) );

		Device.Destroy( pipeline );
		Device.Destroy( vertexBuffer );
		Device.Destroy( indexBuffer );
		Device.Destroy( instanceBuffer );
		Device.Destroy( transforms );
	}
}
