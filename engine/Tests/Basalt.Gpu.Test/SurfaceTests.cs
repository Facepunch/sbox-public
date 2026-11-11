using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Sandbox;
using Sandbox.BasaltLab.Surfaces;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BasaltGpuTests;

/// <summary>
/// The Slang material tier: Sandbox.ShaderCompiler's source rewrites and reflection, and new-tier materials drawn on
/// Basalt.Gpu against the same material written as today's .shader. See docs/managed/shader-format-replacement.md.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SurfaceTests : GpuTest
{
	const int Width = 256, Height = 160;

	static string Core => ContentFiles.FindGame().Roots.FirstOrDefault();

	static string Surfaces => Path.GetFullPath( Path.Combine( Core, "..", "..", "engine", "Launcher", "BasaltLab", "Surfaces" ) );

	static SlangCompiler Compiler()
	{
		if ( Core is null ) Assert.Inconclusive( "No game content" );
		var bin = Path.Combine( Path.GetDirectoryName( Core ), "bin", "win64" );
		if ( !File.Exists( Path.Combine( bin, OperatingSystem.IsWindows() ? "slang.dll" : "libslang.so" ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );
		return new SlangCompiler( bin, [Path.Combine( Core, "shaders" )] );
	}

	/// <summary>vfx's include rewrite: static sets become defines, annotations are recorded per variable.</summary>
	[TestMethod]
	public void VfxIncludesRewriteSetsAndReadAnnotations()
	{
		var annotations = new Dictionary<string, Annotations>();
		var text = VfxIncludes.Rewrite( "ExternalDescriptorSet g_viewSet Slot 2;\n"
			+ "float3 FogSunColor < Attribute( \"FogSunColor\" ); Default3( 0.5, 1, 0 ); >;\n"
			+ "CreateTexture3DWithoutSampler( g_tFogVolume ) : register( t11 ) < Attribute( \"FogVolume\" ); SrgbRead( false ); > ;\n"
			+ "StructuredBuffer< TransformBufferData_t > g_TransformData  < Attribute( \"g_TransformBuffer\" ); >;\n"
			+ "SamplerComparisonState ShadowDepthPCFSampler < Filter( COMPARISON_MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); ComparisonFunc( GREATER_EQUAL ); >;\n"
			+ "int DDGIEnabled < Attribute( \"DDGI_Enabled\" ); Default( 0 ); SpecializedConstant(); >;\n", annotations );

		Assert.IsTrue( text.StartsWith( "#define g_viewSet 5\n", StringComparison.Ordinal ), text );
		Assert.AreEqual( "FogSunColor", annotations["FogSunColor"].Attribute );
		CollectionAssert.AreEqual( new[] { 0.5f, 1, 0 }, annotations["FogSunColor"].Default );
		Assert.AreEqual( "FogVolume", annotations["g_tFogVolume"].Attribute );
		Assert.AreEqual( "g_TransformBuffer", annotations["g_TransformData"].Attribute );

		// As vfx compiles them: a specialization constant, so the pipeline folds the branches it guards
		StringAssert.Contains( text, $"[vk::constant_id( {VfxIncludes.SpecializationId( "DDGIEnabled" )} )] const int DDGIEnabled = 0;" );
		Assert.AreEqual( "DDGI_Enabled", annotations["DDGIEnabled"].Attribute );

		var sampler = BackendContext.SurfaceSampler( annotations["ShadowDepthPCFSampler"] );
		Assert.AreEqual( CompareFunction.GreaterEqual, sampler.Compare );
		Assert.AreEqual( AddressMode.Clamp, sampler.AddressU );
		Assert.AreEqual( Filter.Linear, sampler.MipFilter );
	}

	/// <summary>Texture2D fields of [Material] structs become bindless slots; other Texture2Ds and lines stay.</summary>
	[TestMethod]
	public void MaterialStructTexturesBecomeSlots()
	{
		var source = "Texture2D Outside;\n[Material]\npublic struct Leaf : ISurface\n{\n\t[Group( \"Color\" ), Srgb] Texture2D Color;\n\tTexture2D<float4> Mask;\n\tfloat3 Tint;\n\tMaterial Evaluate( SurfaceInput i ) { Texture2D t; return {}; }\n}\n";
		var text = MaterialSource.Rewrite( source, out var materials );

		Assert.AreEqual( 1, materials.Count );
		Assert.AreEqual( "Leaf", materials[0].Name );
		CollectionAssert.AreEqual( new[] { "ISurface" }, materials[0].Interfaces );
		StringAssert.Contains( text, "[Group( \"Color\" ), Srgb] public MaterialTexture2D Color;" );
		StringAssert.Contains( text, "\tpublic MaterialTexture2D Mask;" );
		StringAssert.Contains( text, "\tpublic float3 Tint;" );
		StringAssert.Contains( text, "Texture2D Outside;" );
		StringAssert.Contains( text, "{ Texture2D t; return {}; }" );
		Assert.AreEqual( source.Count( c => c == '\n' ), text.Count( c => c == '\n' ) );
	}

	/// <summary>
	/// A surface compiles into forward, alpha-tested forward, depth and alpha-tested depth passes; its record is the
	/// struct in order with textures as four-byte slots; the alpha-tested depth pass reads only what opacity needs.
	/// </summary>
	[TestMethod]
	public void SurfaceCompilesEveryPass()
	{
		var rock = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );

		Assert.AreEqual( ShaderTier.Surface, rock.Tier );
		Assert.AreEqual( 28, rock.Material.Size );
		CollectionAssert.AreEqual( new[] { "Color@0", "Normal@4", "Rma@8", "Tint@12", "RoughnessScale@24" }, rock.Material.Fields.Select( f => $"{f.Name}@{f.Offset}" ).ToArray() );
		Assert.IsTrue( rock.Material.Field( "Color" ).Srgb );
		Assert.AreEqual( 2f, rock.Material.Field( "RoughnessScale" ).Max );

		Assert.IsNull( rock.Pass( ShaderPasses.Depth ).Pixel );
		var forward = rock.Pass( ShaderPasses.Forward ).Pixel;
		Assert.IsTrue( forward.Bindings.Any( b => b.Kind == BindingKind.MaterialRecords && b.Set == 1 ) );
		Assert.IsTrue( forward.Bindings.Any( b => b.Variable == "g_MaterialTextures2D" && b.Set == 4 && b.Binding == ShaderAbi.BindlessTextures ) );
		Assert.IsTrue( forward.Bindings.Any( b => b.Name == "PerViewConstantBuffer_t" && b.Kind == BindingKind.ConstantBuffer && b.Size > 0 ) );
		Assert.IsTrue( forward.Bindings.All( b => b.Set is 1 or ShaderAbi.BindlessSet or ShaderAbi.ViewSet ) );

		// Per-view lighting and fog come from the view set, as the core shaders read them
		Assert.IsTrue( forward.Bindings.Any( b => b.Set == ShaderAbi.ViewSet && b.Name == "DirectionalLightCB" ) );
		Assert.IsTrue( forward.Specializations.Any( s => s.Attribute == "ShadowFilterQuality" ) );
		Assert.IsFalse( forward.Globals.Members.Any( m => m.Name == "UserShadowFilterQuality" ) );
		Assert.IsTrue( rock.Pass( ShaderPasses.Forward ).Vertex.Bindings.All( b => b.Set is 0 or ShaderAbi.BindlessSet or ShaderAbi.ViewSet ) );

		// Alpha-tested passes compile with today's alpha test (S_ALPHA_TEST: its cutoff and edge strength are globals), the
		// depth one in Depth mode as well (S_MODE_DEPTH), where Shade returns the coverage before lighting
		var alphaTested = rock.Pass( ShaderPasses.Forward + "AlphaTest" ).Pixel;
		Assert.IsTrue( alphaTested.Globals.Members.Any( m => m.Name == "g_flAlphaTestReference" ) );
		Assert.IsFalse( forward.Globals.Members.Any( m => m.Name == "g_flAlphaTestReference" ) );
		var depth = rock.Pass( ShaderPasses.DepthAlphaTest ).Pixel;
		Assert.IsTrue( depth.Globals.Members.Any( m => m.Name == "g_flAlphaTestReference" ) );
		StringAssert.Contains( rock.Generated, "SurfaceForward<Rock, true>( i.Pixel, m, m.Color.Slot )" );

		// Material textures are read with the module's sampler, its state from its annotations
		var sampler = forward.Bindings.Single( b => b.Kind == BindingKind.Sampler && b.Variable == "g_MaterialFiltering" );
		Assert.AreEqual( "ANISOTROPIC", sampler.Annotations?.Filter );
		Assert.IsFalse( rock.Generated.Contains( "SURFACE_LIGHT_HOOK", StringComparison.Ordinal ) );
	}

	/// <summary>A lit surface turns on the light loop's hook and hands it the material; an unlit one leaves the loop alone.</summary>
	[TestMethod]
	public void LitSurfaceHooksTheLightLoop()
	{
		var lit = Compiler().CompileSurface( Path.Combine( Surfaces, "rock_lit.slang" ) );
		StringAssert.Contains( lit.Generated, "#define SURFACE_LIGHT_HOOK 1" );
		StringAssert.Contains( lit.Generated, "s_LitSurface = m;" );
		StringAssert.Contains( lit.Generated, "s_LitSurface.Light( SurfaceLitMaterial(), SurfaceLitInput(), light )" );
		Assert.IsNotNull( lit.Pass( ShaderPasses.Forward ).Pixel.Spirv );
	}
	/// <summary>The generated classes in BasaltLab are what the compiler writes now, and a raw module has the same record.</summary>
	[TestMethod]
	public void GeneratedClassesAreCurrent()
	{
		var compiler = Compiler();
		var rock = compiler.CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		var raw = compiler.CompileRaw( Path.Combine( Surfaces, "rock_raw.slang" ) );

		Assert.AreEqual( ShaderTier.Raw, raw.Tier );
		CollectionAssert.AreEqual( rock.Material.Fields.Select( f => (f.Kind, f.Offset) ).ToArray(), raw.Material.Fields.Select( f => (f.Kind, f.Offset) ).ToArray() );
		var lit = compiler.CompileSurface( Path.Combine( Surfaces, "rock_lit.slang" ) );
		foreach ( var (shader, file) in new[] { (rock, "rock.g.cs"), (raw, "rock_raw.g.cs"), (lit, "rock_lit.g.cs") } )
		{
			var expected = MaterialClassWriter.Write( shader, "Sandbox.BasaltLab.Surfaces" ).Replace( "\r\n", "\n" );
			var actual = File.ReadAllText( Path.Combine( Surfaces, file ) ).Replace( "\r\n", "\n" );
			Assert.AreEqual( expected, actual, $"{file} is stale: run basaltlab -surface-parity <folder> -surface-classes" );
		}
	}

	/// <summary>
	/// The surface and the raw module draw what the same material written as a .shader draws, set from C# through
	/// their generated classes; changing a field from C# changes the picture.
	/// </summary>
	[TestMethod]
	public void SurfaceAndRawDrawAsTheShaderDoes()
	{
		Compiler();
		using var backend = new BackendDevice( Device ) { TimeDraws = true };
		backend.Surfaces.AddFolder( Surfaces );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var shader = (BackendMaterial)((GpuDevice)backend).CreateMaterial( "Rock (.shader)", "shaders/surface_test_rock.shader" );
			if ( shader.Shader?.Name.Contains( "surface_test_rock", StringComparison.OrdinalIgnoreCase ) != true ) Assert.Inconclusive( "surface_test_rock.shader isn't compiled" );

			var color = backend.LoadTexture( "materials/default/rope_color_png_95bee33c.generated.vtex" );
			var normal = backend.LoadTexture( "materials/default/rope_normal_png_b8960e44.generated.vtex" );
			var rma = backend.LoadTexture( "materials/default/rope01_vmat_g_trma_bbf6a2a.generated.vtex" );
			shader.SetTexture( "g_tColor", (BackendTexture)color );
			shader.SetTexture( "g_tNormal", (BackendTexture)normal );
			shader.SetTexture( "g_tRma", (BackendTexture)rma );
			shader.Set( "g_vTint", [1, 0.8f, 0.6f] );

			using var surface = new Rock { Color = color, Normal = normal, Rma = rma, Tint = new Vector3( 1, 0.8f, 0.6f ) };
			using var raw = new RockRaw { Color = color, Normal = normal, Rma = rma, Tint = new Vector3( 1, 0.8f, 0.6f ) };

			var reference = Draw( backend, shader );
			var fromSurface = Draw( backend, surface );
			var fromRaw = Draw( backend, raw );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
			Assert.AreEqual( 0, backend.DrawTimes.Failed.Count, "Draws failed: " + string.Join( ", ", backend.DrawTimes.Failed.Select( f => $"{f.Key} x{f.Value}" ) ) );
			SaveRgb( reference, "surface-shader" );
			SaveRgb( fromSurface, "surface-surface" );
			SaveRgb( fromRaw, "surface-raw" );
			Assert.IsTrue( Lit( reference ) > Width * Height / 4, "The .shader drew too little to compare" );
			Assert.IsTrue( Mean( reference, fromSurface ) < 0.5, $"surface differs by {Mean( reference, fromSurface ):F3}" );
			Assert.IsTrue( Mean( reference, fromRaw ) < 0.5, $"raw differs by {Mean( reference, fromRaw ):F3}" );

			// The record offset read per draw instead of passed on per pixel draws the same
			backend.Surfaces.MaterialOffsetPerPixel = false;
			var perDraw = Draw( backend, surface );
			backend.Surfaces.MaterialOffsetPerPixel = true;
			Assert.IsTrue( Mean( reference, perDraw ) < 0.5, $"per draw differs by {Mean( reference, perDraw ):F3}" );

			// A field set from C# rewrites the record: a blue tint is a different picture
			surface.Tint = new Vector3( 0.2f, 0.3f, 1 );
			Assert.IsTrue( Mean( reference, Draw( backend, surface ) ) > 2, "Changing Tint didn't change the picture" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>A box lit by the sun, filling most of the frame, as 8-bit sRGB-ish bytes (red channel of RGBA16F).</summary>
	byte[] Draw( BackendDevice backend, GpuMaterial material )
	{
		using var system = new RenderSystem { ParallelRecording = false };
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface test" );
		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( -0.4f, 0.5f, -0.75f ).Normal;
		world.Lighting.SunShadowCascades = 1;
		world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), new Transform( Vector3.Zero, Rotation.FromYaw( 35 ), 2 ) ) );

		var view = new RenderView { Name = "Surface test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.2f, 0.2f, 0.2f ), Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -220, 60, 90 );
		view.Rotation = Rotation.LookAt( -view.Position );

		for ( int frame = 0; frame < 3; frame++ )
		{
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		Device.WaitIdle();
		var (texture, layout) = BackendDevice.Unwrap( target.Color );
		var pixels = ReadBack( texture, layout );
		BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );

		var bytes = new byte[Width * Height * 3];
		for ( int i = 0; i < bytes.Length; i++ ) bytes[i] = (byte)Math.Clamp( MathF.Pow( Math.Max( (float)BitConverter.ToHalf( pixels, (i / 3 * 4 + i % 3) * 2 ), 0 ), 1 / 2.2f ) * 255 + 0.5f, 0, 255 );
		return bytes;
	}

	void SaveRgb( byte[] rgb, string name )
	{
		var rgba = new byte[Width * Height * 4];
		for ( int i = 0; i < Width * Height; i++ )
		{
			rgba[i * 4] = rgb[i * 3];
			rgba[i * 4 + 1] = rgb[i * 3 + 1];
			rgba[i * 4 + 2] = rgb[i * 3 + 2];
			rgba[i * 4 + 3] = 255;
		}

		Save( rgba, Width, Height, name );
	}

	static double Mean( byte[] a, byte[] b )
	{
		long total = 0;
		for ( int i = 0; i < a.Length; i++ ) total += Math.Abs( a[i] - b[i] );
		return total / (double)a.Length;
	}

	static int Lit( byte[] rgb )
	{
		var lit = 0;
		for ( int i = 0; i < rgb.Length; i += 3 ) if ( Math.Abs( rgb[i] - 124 ) + Math.Abs( rgb[i + 1] - 124 ) + Math.Abs( rgb[i + 2] - 124 ) > 6 ) lit++;
		return lit;
	}
}
