using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;
using Sandbox;
using Sandbox.BorealisLab.Surfaces;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// The Slang material tier: Sandbox.ShaderCompiler's source rewrites and reflection, and new-tier materials drawn on
/// Borealis.Gpu against the same material written as today's .shader. See docs/reference/borealis-slang-materials.md.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SurfaceTests : GpuTest
{
	const int Width = 256, Height = 160;

	static string Core => ContentFiles.FindGame().Roots.FirstOrDefault();

	static string Surfaces => Path.GetFullPath( Path.Combine( Core, "..", "..", "engine", "Launcher", "BorealisLab", "Surfaces" ) );

	/// <summary>
	/// The engine's Slang over the core shaders, with the backend's disk cache (<see cref="Fixtures.SurfaceCache"/>): a surface
	/// compiles once until it or anything it reads changes. Tests of compiling itself make compilers of their own.
	/// </summary>
	static SlangCompiler Compiler()
	{
		if ( Core is null ) Assert.Inconclusive( "No game content" );
		var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
		if ( !File.Exists( Path.Combine( bin, EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );
		return new SlangCompiler( bin, [Path.Combine( Core, "shaders" )] ) { CacheFolder = Fixtures.SurfaceCache };
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

		// As vfx compiles them: a specialization constant, so the pipeline folds the branches it guards, and a constant buffer
		// member of the same name, so the layout matches the compiled shader's
		StringAssert.Contains( text, $"int DDGIEnabled; [vk::constant_id( {VfxIncludes.SpecializationId( "DDGIEnabled" )} )] const int DDGIEnabled__Spec = 0;\n#define DDGIEnabled DDGIEnabled__Spec" );
		Assert.AreEqual( "DDGI_Enabled", annotations["DDGIEnabled"].Attribute );

		var sampler = BackendContext.SurfaceSampler( annotations["ShadowDepthPCFSampler"] );
		Assert.AreEqual( CompareFunction.GreaterEqual, sampler.Compare );
		Assert.AreEqual( AddressMode.Clamp, sampler.AddressU );
		Assert.AreEqual( Filter.Linear, sampler.MipFilter );
	}

	/// <summary>
	/// A sampler without a filter is native's default sampler state (<c>DefaultSamplerStateDesc</c>): anisotropic and
	/// wrapped. blendable.shader samples every layer with such a sampler (<c>g_sDefault</c>), so blendable.slang's
	/// sampler filters anisotropically too, or ground seen at a glancing angle (Construct's grass) blurs along depth.
	/// </summary>
	[TestMethod]
	public void SamplersWithoutAFilterAreAnisotropic()
	{
		var annotations = new Dictionary<string, Annotations>();
		VfxIncludes.Rewrite( "SamplerState Clamped < AddressU( CLAMP ); >;\n"
			+ "SamplerState Trilinear < Filter( TRILINEAR ); MaxAniso( 8 ); >;\n"
			+ "SamplerState Aniso < Filter( ANISOTROPIC ); MaxAniso( 8 ); >;\n", annotations );

		Assert.AreEqual( BackendContext.DefaultAnisotropy, BackendContext.SurfaceSampler( null ).MaxAnisotropy );
		var clamped = BackendContext.SurfaceSampler( annotations["Clamped"] );
		Assert.AreEqual( BackendContext.DefaultAnisotropy, clamped.MaxAnisotropy );
		Assert.AreEqual( AddressMode.Clamp, clamped.AddressU );
		Assert.AreEqual( AddressMode.Wrap, clamped.AddressV );
		Assert.AreEqual( 1, BackendContext.SurfaceSampler( annotations["Trilinear"] ).MaxAnisotropy );
		Assert.AreEqual( 8, BackendContext.SurfaceSampler( annotations["Aniso"] ).MaxAnisotropy );

		var blendable = Compiler().CompileSurface( Path.Combine( Core, "shaders", "blendable.slang" ) );
		var filtering = blendable.Pass( ShaderPasses.Forward ).Pixel.Bindings.First( b => b.Kind == BindingKind.Sampler && b.Variable == "g_BlendableFiltering" );
		var state = BackendContext.SurfaceSampler( filtering.Annotations );
		Assert.AreEqual( BackendContext.DefaultAnisotropy, state.MaxAnisotropy );
		Assert.AreEqual( AddressMode.Wrap, state.AddressU );
	}

	/// <summary>
	/// Material.CommonInputs.hlsl's <c>TextureFiltering</c> takes its filter from the material's <c>F_TEXTURE_FILTERING</c>, as
	/// vfx evaluates annotations with the material's features: anisotropic x8 by default, then bilinear, trilinear, point.
	/// </summary>
	[TestMethod]
	public void TextureFilteringFollowsTheMaterialsFeature()
	{
		var annotations = new Dictionary<string, Annotations>();
		var line = File.ReadAllLines( Path.Combine( Core, "shaders", "common", "utils", "Material.CommonInputs.hlsl" ) ).Single( l => l.Contains( "SamplerState TextureFiltering", StringComparison.Ordinal ) );
		VfxIncludes.Rewrite( line + "\n", annotations );
		var filtering = annotations["TextureFiltering"];
		StringAssert.Contains( filtering.Filter, "F_TEXTURE_FILTERING == 0 ? ANISOTROPIC" );

		SamplerInfo With( int value ) => BackendContext.SurfaceSampler( filtering, name => name == "F_TEXTURE_FILTERING" ? value : null );
		var anisotropic = BackendContext.SurfaceSampler( filtering );
		Assert.AreEqual( 8, anisotropic.MaxAnisotropy, "a feature at its default (0) is anisotropic" );
		Assert.AreEqual( Filter.Linear, anisotropic.MipFilter );
		Assert.AreEqual( 8, With( 0 ).MaxAnisotropy );
		Assert.AreEqual( (1f, Filter.Linear, Filter.Nearest), (With( 1 ).MaxAnisotropy, With( 1 ).MinFilter, With( 1 ).MipFilter), "bilinear" );
		Assert.AreEqual( (1f, Filter.Linear, Filter.Linear), (With( 2 ).MaxAnisotropy, With( 2 ).MinFilter, With( 2 ).MipFilter), "trilinear" );
		Assert.AreEqual( (Filter.Nearest, Filter.Nearest), (With( 3 ).MinFilter, With( 3 ).MagFilter), "point" );
		Assert.AreEqual( (Filter.Nearest, Filter.Nearest), (With( 4 ).MinFilter, With( 4 ).MagFilter), "nearest" );
	}

	/// <summary>
	/// A shader's own shorthand for annotations (macros a package's expanded includes define, as vfx's preprocessor expands
	/// them) reads as what it stands for; macros that aren't annotations are left alone.
	/// </summary>
	[TestMethod]
	public void VfxIncludesExpandAnnotationMacros()
	{
		var annotations = new Dictionary<string, Annotations>();
		VfxIncludes.Rewrite( "#define SrgbRead(bArg) string _SrgbRead = #bArg\n"
			+ "#define SRGB SrgbRead(true)\n"
			+ "#define Address(arg) AddressU(arg); AddressV(arg); AddressW(arg)\n"
			+ "#define TINT 3\n"
			+ "Texture2D Frame < Attribute(\"Frame\"); SRGB; > ;\n"
			+ "SamplerState S < Filter(BILINEAR); Address(CLAMP); > ;\n", annotations );

		Assert.IsTrue( annotations["Frame"].SrgbRead );
		Assert.AreEqual( "Frame", annotations["Frame"].Attribute );
		Assert.AreEqual( "CLAMP", annotations["S"].AddressU );
		Assert.AreEqual( "CLAMP", annotations["S"].AddressW );
		Assert.AreEqual( "BILINEAR", annotations["S"].Filter );

		// A module's active lines, read again per compile, have no #defines: the macros come from its whole text
		var macros = "#define SRGB SrgbRead(true)\n";
		VfxIncludes.Rewrite( "Texture2D Next < Attribute(\"Next\"); SRGB; > ;\n", annotations, macros );
		Assert.IsTrue( annotations["Next"].SrgbRead );
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

		// Opaque depth's vertex stage reads the position alone (and the instance id), so it can draw from positions-only
		// vertices; translucent forward compiles with S_TRANSLUCENT, which keeps opacity in alpha
		var depthInputs = rock.Pass( ShaderPasses.Depth ).Vertex.Inputs.Select( i => i.Semantic + i.SemanticIndex ).ToArray();
		Assert.IsTrue( depthInputs.All( i => i is "POSITION0" or "TEXCOORD13" or "BLENDINDICES0" ), string.Join( ", ", depthInputs ) );
		Assert.IsTrue( rock.Pass( ShaderPasses.Forward ).Vertex.Inputs.Any( i => i.Semantic == "NORMAL" ) );
		Assert.IsNotNull( rock.Pass( ShaderPasses.Forward + "Translucent" )?.Pixel );
		Assert.IsTrue( forward.Bindings.Any( b => b.Kind == BindingKind.MaterialRecords && b.Set == 1 ) );
		Assert.IsTrue( forward.Bindings.Any( b => b.Variable == "g_MaterialTextures2D" && b.Set == 4 && b.Binding == ShaderAbi.BindlessTextures ) );
		Assert.IsTrue( forward.Bindings.Any( b => b.Name == "PerViewConstantBuffer_t" && b.Kind == BindingKind.ConstantBuffer && b.Size > 0 ) );
		Assert.IsTrue( forward.Bindings.All( b => b.Set is 1 or ShaderAbi.BindlessSet or ShaderAbi.ViewSet ) );

		// Per-view lighting and fog come from the view set, as the core shaders read them
		Assert.IsTrue( forward.Bindings.Any( b => b.Set == ShaderAbi.ViewSet && b.Name == ShaderAbi.ViewEnvironment ) );
		Assert.IsTrue( forward.Specializations.Any( s => s.Attribute == "ShadowFilterQuality" ) );
		Assert.IsTrue( forward.Globals.Members.Any( m => m.Name == "UserShadowFilterQuality" ), "the specialization constant keeps its member, as vfx does" );
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

	/// <summary>
	/// A program guesses the parts of the passes its materials noted (SurfaceShaders.NoteMaterial), not every pass it has:
	/// an opaque static material its GPU-driven colour and depth passes where the device draws GPU-driven, its classic
	/// ones otherwise; an alpha-tested skinned one its skinned alpha-tested passes; a blended one its translucent colour
	/// pass alone; two materials' notes aren't crossed.
	/// </summary>
	[TestMethod]
	public void GuessesAreTheNotedMaterialsPasses()
	{
		var rock = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		Assert.IsNull( rock.GpuDrivenError, rock.GpuDrivenError );
		using var backend = new BackendDevice( Device );
		using var program = new SurfaceProgram( backend, rock );
		string[] Names( uint notes, bool gpuDriven ) => SurfacePassNotes.Resolve( notes, program, gpuDriven ).Select( p => p.Pass.Pass.Name ).Order( StringComparer.Ordinal ).ToArray();

		var opaque = SurfaceRenderState.Opaque;
		CollectionAssert.AreEqual( new[] { "DepthGpuDriven", "ForwardGpuDriven" }, Names( SurfacePassNotes.Of( opaque, SurfaceDrawKinds.Static ), true ) );
		CollectionAssert.AreEqual( new[] { "Depth", "Forward" }, Names( SurfacePassNotes.Of( opaque, SurfaceDrawKinds.Static ), false ) );
		CollectionAssert.AreEqual( new[] { "DepthAlphaTestSkinned", "ForwardAlphaTestSkinned" }, Names( SurfacePassNotes.Of( opaque with { AlphaTest = true }, SurfaceDrawKinds.Skinned ), true ) );
		CollectionAssert.AreEqual( new[] { "ForwardTranslucentGpuDriven" }, Names( SurfacePassNotes.Of( opaque with { Blend = SurfaceBlend.Alpha, DepthWrite = false }, SurfaceDrawKinds.Static ), true ) );

		var both = Names( SurfacePassNotes.Of( opaque, SurfaceDrawKinds.Static ) | SurfacePassNotes.Of( opaque with { AlphaTest = true }, SurfaceDrawKinds.Skinned ), true );
		Assert.AreEqual( 4, both.Length, string.Join( ", ", both ) );
		Assert.IsFalse( both.Any( n => n.Contains( "AlphaTestGpuDriven", StringComparison.Ordinal ) ), "an opaque static material and an alpha-tested skinned one make no alpha-tested GPU-driven guesses" );
	}

	/// <summary>
	/// An interpolated surface (blendable.slang) reads its Varyings from the mesh by semantic and passes them to the pixel
	/// stage; its fields say which .vmat value they take.
	/// </summary>
	[TestMethod]
	public void InterpolatedSurfaceReadsItsVaryings()
	{
		var blendable = Compiler().CompileSurface( Path.Combine( Core, "shaders", "blendable.slang" ) );
		var inputs = blendable.Pass( ShaderPasses.Forward ).Vertex.Inputs;
		Assert.IsTrue( inputs.Any( i => i.Semantic == "TEXCOORD" && i.SemanticIndex == 4 && i.Components == 4 ) );
		Assert.IsTrue( inputs.Any( i => i.Semantic == "TEXCOORD" && i.SemanticIndex == 5 && i.Components == 4 ) );
		StringAssert.Contains( blendable.Generated, "SurfaceWithVaryings<Blendable>" );
		Assert.AreEqual( "F_MULTIBLEND", blendable.Material.Field( "ExtraLayers" ).Source );
		Assert.AreEqual( "ShaderVersion", blendable.Material.Field( "Version" ).Source );
		Assert.IsNull( blendable.Material.Field( "ColorA" ).Source );
		Assert.IsTrue( blendable.Pass( ShaderPasses.Forward ).Pixel.Bindings.Any( b => b.Kind == BindingKind.Sampler && b.Variable == "g_BlendableFiltering" ) );
	}

	/// <summary>
	/// An interpolated surface that also moves its vertices (fur.slang, IInterpolatedVertexSurface): every vertex stage reads
	/// its Varyings (the mesh's colour), opaque depth included, and the device knows it displaces, so depth and the ids
	/// prepass don't place its vertices from their positions alone.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void InterpolatedVertexSurfaceDisplacesWithItsVaryings()
	{
		var fur = Compiler().CompileSurface( Path.Combine( Core, "shaders", "fur.slang" ) );
		StringAssert.Contains( fur.Generated, "SurfaceOutputDisplacedWith<Fur>" );
		foreach ( var name in new[] { ShaderPasses.Forward, ShaderPasses.Depth, ShaderPasses.DepthAlphaTest } )
			Assert.IsTrue( fur.Pass( name ).Vertex.Inputs.Any( i => i.Semantic == "COLOR" && i.SemanticIndex == 0 ), $"{name} doesn't read the mesh's colour" );
		Assert.IsNull( fur.GpuDrivenError, fur.GpuDrivenError );
		Assert.IsTrue( fur.Material.RenderState.AlphaTest );

		using var backend = new BackendDevice( Device );
		using var program = new SurfaceProgram( backend, fur );
		Assert.IsTrue( program.Displaces );
	}

	/// <summary>
	/// A surface material's depth draws take the shared depth-only material once its program is known not to move vertices,
	/// while it draws opaque, one-sided and without alpha test; before its program is found it draws its own, and lists
	/// made once from it are made again.
	/// </summary>
	[TestMethod]
	public void OpaqueSurfacesDrawDepthOnlyOnceTheirProgramIsKnown()
	{
		var compiled = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		using var backend = new BackendDevice( Device );
		using var program = new SurfaceProgram( backend, compiled );
		Assert.IsFalse( program.Displaces );

		using var rock = new Rock();
		Assert.IsFalse( rock.DepthOnlyNow, "no program yet" );
		Assert.IsTrue( rock.DepthOnlyPending );

		// Lists made from every material's answer ask again only when the epoch moves, so each change moves it
		var epoch = GpuMaterial.DepthOnlyEpoch;
		void Moved( string what )
		{
			Assert.AreNotEqual( epoch, GpuMaterial.DepthOnlyEpoch, what );
			epoch = GpuMaterial.DepthOnlyEpoch;
		}

		rock.Program = program;
		Assert.IsTrue( rock.DepthOnlyNow );
		Assert.IsFalse( rock.DepthOnlyPending );
		Moved( "program found" );

		rock.AlphaTest = true;
		Assert.IsFalse( rock.DepthOnlyNow, "alpha tested" );
		Moved( "alpha test" );
		rock.AlphaTest = false;

		rock.TwoSided = true;
		Assert.IsFalse( rock.DepthOnlyNow, "two-sided" );
		Moved( "two-sided" );
		rock.TwoSided = false;

		epoch = GpuMaterial.DepthOnlyEpoch;
		program.Stale = true;
		Assert.IsFalse( rock.DepthOnlyNow, "dropped by a hotload" );
		Assert.IsTrue( rock.DepthOnlyPending );
		Moved( "hotload" );
	}

	/// <summary>
	/// Colour after a prepass that wrote its triangles' depth (the visibility-driven forward pass) doesn't write it again: an
	/// opaque surface tests greater-or-equal, an alpha-tested one equal, which keeps early depth testing. Without a prepass
	/// both test greater-or-equal and write.
	/// </summary>
	[TestMethod]
	public void ForwardAfterThePrepassDoesntWriteDepth()
	{
		var compiled = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		using var backend = new BackendDevice( Device );
		using var program = new SurfaceProgram( backend, compiled );
		var pass = program.Pass( ShaderPasses.Forward );
		var opaque = SurfaceRenderState.Opaque;
		var alphaTested = opaque with { AlphaTest = true };
		GraphicsPipelineInfo Info( SurfaceRenderState state, bool prepassed ) => program.Info( pass, backend.PulledLayout, Format.RGBA16Float, Format.D32Float, 4, null, null, state, false, false, prepassed, false );

		var plain = Info( opaque, false );
		Assert.IsTrue( plain.DepthWrite );
		Assert.AreEqual( CompareFunction.GreaterEqual, plain.DepthCompare );

		var prepassed = Info( opaque, true );
		Assert.IsFalse( prepassed.DepthWrite, "opaque after the prepass" );
		Assert.AreEqual( CompareFunction.GreaterEqual, prepassed.DepthCompare, "its prepass may have pulled positions with another shader" );

		var tested = Info( alphaTested, true );
		Assert.IsFalse( tested.DepthWrite, "alpha-tested after the prepass" );
		Assert.AreEqual( CompareFunction.Equal, tested.DepthCompare );
	}

	/// <summary>
	/// A forward pass's pixel module without its outputs past location 0 is made with the program, on the thread that compiles
	/// it: a pipeline into a view with no normals target (a cube capture's face) finds it made, rather than making it on the
	/// thread that records the draw.
	/// </summary>
	[TestMethod]
	public void PixelModuleWithoutNormalsIsMadeWithTheProgram()
	{
		var compiled = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		using var backend = new BackendDevice( Device );
		using var program = new SurfaceProgram( backend, compiled );
		var pass = program.Pass( ShaderPasses.Forward );
		Assert.IsTrue( pass.PixelOutputs?.Any( o => o.Location >= 1 ) == true, "rock's forward pass writes the normals at target 1" );
		Assert.AreEqual( 1, pass.PixelWithoutOutputs.Count, "made with the program" );
		var ahead = pass.PixelWithoutOutputs.Values.Single();
		Assert.IsTrue( ahead.IsValid );

		var info = program.Info( pass, backend.PulledLayout, Format.RGBA16Float, Format.D32Float, 1, null, null, SurfaceRenderState.Opaque, false, false, false, false );
		Assert.AreEqual( ahead, info.FragmentShader, "a view without a normals target draws with the module made ahead" );
		Assert.AreEqual( 1, pass.PixelWithoutOutputs.Count, "none made for the pipeline" );
	}

	/// <summary>
	/// The surface pipeline manifest (<see cref="SurfacePipelineManifest"/>): a pipeline a draw made is recorded by what asked
	/// for it, saved, read back by the next run and made ahead on a worker once the program compiles, so the same request
	/// finds it made without making another. Lines that don't read, or name a pass the program no longer has, are dropped,
	/// and an unreadable file is an empty one.
	/// </summary>
	[TestMethod]
	public void ManifestMakesRecordedPipelinesAhead()
	{
		var rock = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-manifest-{Guid.NewGuid():N}" );
		var file = Path.Combine( folder, "surface-pipelines.txt" );
		try
		{
			using var backend = new BackendDevice( Device );
			var state = new SurfaceRenderState( SurfaceBlend.Opaque, true, true, 0, 0, false, false );
			SurfacePipelineManifest.Request RequestFor( SurfaceProgram program ) => new( program.Pass( ShaderPasses.Forward ), backend.PulledLayout, Format.RGBA16Float, Format.D32Float, 1,
				null, null, 42, state, false, false, false, false );

			// The first run: a draw's pipeline, recorded and saved
			using ( var first = new SurfaceProgram( backend, rock ) { SourceFile = "rock.slang" } )
			{
				var written = new SurfacePipelineManifest( file );
				written.Record( "rock.slang", RequestFor( first ) );
				written.Record( "rock.slang", RequestFor( first ) );
				Assert.AreEqual( 1, written.Recorded, "a request is kept once" );
				Assert.IsTrue( written.Save() );
				Assert.IsFalse( written.Save(), "nothing new to write" );
			}

			var good = File.ReadAllLines( file )[1];
			var noSuchPass = "rock.slang\tNoSuchPass" + good[("rock.slang\t" + ShaderPasses.Forward).Length..];
			File.AppendAllLines( file, ["not a line of the manifest", "rock.slang\t" + string.Join( '\t', Enumerable.Repeat( "x", 22 ) ), noSuchPass] );

			// The next run: made ahead as the program compiles, found made by the draw that asks for it
			using var program = new SurfaceProgram( backend, rock ) { SourceFile = "rock.slang" };
			var read = new SurfacePipelineManifest( file );
			Assert.AreEqual( 3, read.Loaded, "lines without a file aren't read" );
			var requests = read.Take( "rock.slang", program );
			Assert.AreEqual( 1, requests.Count );
			Assert.AreEqual( 2, read.Dropped, "a line that doesn't read and one naming a pass the program lacks are dropped" );
			Assert.IsNull( read.Take( "rock.slang", program ), "taken once" );

			program.Prewarm( requests );
			var deadline = DateTime.UtcNow.AddSeconds( 30 );
			while ( program.Prewarmed == 0 && DateTime.UtcNow < deadline ) System.Threading.Thread.Sleep( 10 );
			Assert.AreEqual( 1, program.Prewarmed, "made ahead on a worker" );

			var made = Device.PipelineCreation.Made;
			var r = RequestFor( program );
			var pipeline = program.Pipeline( r.Pass, r.Layout, r.Color, r.Depth, r.Samples, r.VertexConstants, r.PixelConstants, r.ConstantsKey, r.State );
			Assert.IsTrue( pipeline.IsValid );
			Assert.AreEqual( made, Device.PipelineCreation.Made, "the draw's request found it made" );

			Assert.IsTrue( read.Save(), "the dropped lines are written out" );
			Assert.AreEqual( 2, File.ReadAllLines( file ).Length, "the header and the one good line" );

			File.WriteAllBytes( file, [0xff, 0xfe, 0x00, 0x01] );
			Assert.AreEqual( 0, new SurfacePipelineManifest( file ).Loaded, "an unreadable file is an empty one" );
		}
		finally
		{
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Every pass comes again for lightmapped draws, with the vertex stage that reads the mesh's lightmap uv (TEXCOORD3);
	/// the plain one doesn't read it.
	/// </summary>
	[TestMethod]
	public void LightmappedPassesReadTheLightmapUv()
	{
		var rock = Compiler().CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		static bool ReadsLightmapUv( CompiledStage vertex ) => vertex.Inputs.Any( i => i.Semantic == "TEXCOORD" && i.SemanticIndex == 3 );

		// Opaque depth reads the position alone, lightmapped or not
		Assert.AreSame( rock.Pass( ShaderPasses.Depth ).Vertex, rock.Pass( ShaderPasses.Depth + ShaderPasses.Lightmapped ).Vertex );
		foreach ( var name in new[] { ShaderPasses.Forward, ShaderPasses.Forward + "AlphaTest", ShaderPasses.Forward + "Translucent", ShaderPasses.DepthAlphaTest } )
		{
			var plain = rock.Pass( name );
			var lightmapped = rock.Pass( name + ShaderPasses.Lightmapped );
			Assert.IsFalse( ReadsLightmapUv( plain.Vertex ), name );
			Assert.IsTrue( ReadsLightmapUv( lightmapped.Vertex ), name + ShaderPasses.Lightmapped );
			Assert.AreSame( plain.Pixel, lightmapped.Pixel, "the pixel stage is shared" );
		}
	}

	/// <summary>
	/// Every pass also comes GPU-driven, the translucent forward pass too: its vertex stage pulls its vertices (no vertex
	/// inputs), and neither stage reads the classic records' buffer.
	/// </summary>
	[TestMethod]
	public void SurfacesHaveGpuDrivenPasses()
	{
		foreach ( var path in new[] { Path.Combine( Surfaces, "rock.slang" ), Path.Combine( Core, "shaders", "foliage.slang" ), Path.Combine( Core, "shaders", "blendable.slang" ) } )
		{
			var surface = Compiler().CompileSurface( path );
			Assert.IsNull( surface.GpuDrivenError, surface.GpuDrivenError );
			foreach ( var name in new[] { ShaderPasses.Forward, ShaderPasses.Forward + "AlphaTest", ShaderPasses.Forward + "Translucent", ShaderPasses.Depth, ShaderPasses.DepthAlphaTest } )
			{
				foreach ( var suffix in new[] { ShaderPasses.GpuDriven, ShaderPasses.GpuDriven + ShaderPasses.Lightmapped } )
				{
					var pass = surface.Pass( name + suffix );
					Assert.IsNotNull( pass, $"{Path.GetFileName( path )} {name}{suffix}" );
					Assert.AreEqual( 0, pass.Vertex.Inputs.Count, $"{name}{suffix} reads vertex inputs" );
					foreach ( var stage in new[] { pass.Vertex, pass.Pixel } )
						Assert.IsFalse( stage is not null && stage.Bindings.Any( b => b.Kind == BindingKind.MaterialRecords ), $"{name}{suffix} reads the classic records" );
				}
			}
		}
	}

	/// <summary>
	/// The disk cache: a second compile of an unchanged module comes from it with the same SPIR-V; a change to the module
	/// (or anything it read) compiles again.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void CompileCacheKeepsUnchangedModules()
	{
		Compiler();
		var folder = Path.Combine( Path.GetTempPath(), "borealis-surface-cache-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var module = Path.Combine( folder, "cached_rock.slang" );
			File.WriteAllText( module, File.ReadAllText( Path.Combine( Surfaces, "rock.slang" ) ).Replace( "public struct Rock", "public struct CachedRock", StringComparison.Ordinal ) );
			var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
			var compiler = new SlangCompiler( bin, [Path.Combine( Core, "shaders" )] ) { CacheFolder = Path.Combine( folder, "cache" ) };

			var first = compiler.CompileSurface( module );
			var second = compiler.CompileSurface( module );
			Assert.IsFalse( first.FromCache );
			Assert.IsTrue( second.FromCache );
			Assert.IsTrue( first.Dependencies.Any( d => d.EndsWith( "lighting.slangh", StringComparison.OrdinalIgnoreCase ) ), "the core includes are dependencies" );
			CollectionAssert.AreEquivalent( first.Passes.Keys.ToArray(), second.Passes.Keys.ToArray() );
			foreach ( var (name, pass) in first.Passes )
			{
				CollectionAssert.AreEqual( pass.Vertex.Spirv, second.Pass( name ).Vertex.Spirv, name );
				if ( pass.Pixel is not null ) CollectionAssert.AreEqual( pass.Pixel.Spirv, second.Pass( name ).Pixel.Spirv, name );
			}

			Assert.AreSame( second.Pass( ShaderPasses.Forward ).Pixel, second.Pass( ShaderPasses.Forward + ShaderPasses.Lightmapped ).Pixel, "shared stages stay shared" );
			Assert.AreEqual( first.Material.Size, second.Material.Size );

			File.AppendAllText( module, "\n// changed\n" );
			Assert.IsFalse( compiler.CompileSurface( module ).FromCache, "a changed module compiles again" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// The caches are keyed on what a compile reads and the compiler's <see cref="SlangCompiler.Version"/>, not on this
	/// assembly's build: an engine build, or a Debug build beside a Release one, keeps them.
	/// </summary>
	[TestMethod]
	public void CacheKeyIsNotTheAssemblysBuild()
	{
		var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
		var identity = CompileCache.Identity( bin );
		StringAssert.StartsWith( identity, $"{SlangCompiler.Version}:" );
		Assert.IsFalse( identity.Contains( typeof( SlangCompiler ).Assembly.ManifestModule.ModuleVersionId.ToString(), StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// What the compiler generates for fixed inputs is as it was when <see cref="SlangCompiler.Version"/> was last raised: a
	/// change to it must raise the version, or caches would keep shaders compiled the old way.
	/// </summary>
	[TestMethod]
	public void GeneratedCodeNeedsAVersion()
	{
		var fingerprint = SlangCompiler.Fingerprint();
		Assert.AreEqual( SlangCompiler.GeneratedFingerprint, fingerprint, $"What SlangCompiler generates changed: raise SlangCompiler.Version and set GeneratedFingerprint to \"{fingerprint}\"" );
	}

	/// <summary>
	/// Surfaces compiled at once from several threads (their stages on the compile threads together) come out as each does
	/// alone.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void ParallelCompilesMatchSerialOnes()
	{
		Compiler();
		var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
		var files = new[] { Path.Combine( Surfaces, "rock.slang" ), Path.Combine( Core, "shaders", "glass.slang" ), Path.Combine( Surfaces, "rock_raw.slang" ) };
		var compiler = new SlangCompiler( bin, [Path.Combine( Core, "shaders" )] );
		CompiledShader Compile( string file ) => file.EndsWith( "_raw.slang", StringComparison.Ordinal ) ? compiler.CompileRaw( file ) : compiler.CompileSurface( file );
		var alone = files.Select( Compile ).ToArray();
		var together = files.AsParallel().AsOrdered().WithDegreeOfParallelism( files.Length ).Select( Compile ).ToArray();
		for ( int i = 0; i < files.Length; i++ )
		{
			CollectionAssert.AreEquivalent( alone[i].Passes.Keys.ToArray(), together[i].Passes.Keys.ToArray(), files[i] );
			foreach ( var (name, pass) in alone[i].Passes )
			{
				CollectionAssert.AreEqual( pass.Vertex.Spirv, together[i].Pass( name ).Vertex.Spirv, $"{files[i]} {name}" );
				if ( pass.Pixel is not null ) CollectionAssert.AreEqual( pass.Pixel.Spirv, together[i].Pass( name ).Pixel.Spirv, $"{files[i]} {name}" );
			}

			CollectionAssert.AreEqual( alone[i].Dependencies.ToArray(), together[i].Dependencies.ToArray(), files[i] );
		}
	}

	/// <summary>
	/// The alpha-tested depth pass writes nothing but coverage: it skips the decals (which don't change opacity), for the
	/// plain shading model and the final combiner alike, so neither reads the decal buffer.
	/// </summary>
	[TestMethod]
	public void AlphaTestedDepthSkipsDecals()
	{
		var compiler = Compiler();
		var rock = compiler.CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		var standard = compiler.CompileSurface( Path.Combine( Core, "shaders", "standard.slang" ) );
		foreach ( var (name, shader) in new[] { ("rock", rock), ("standard", standard) } )
		{
			Assert.IsTrue( shader.Pass( ShaderPasses.Forward ).Pixel.Bindings.Any( b => b.Variable == "DecalBuffer" ), $"{name} forward applies decals" );
			Assert.IsFalse( shader.Pass( ShaderPasses.DepthAlphaTest ).Pixel.Bindings.Any( b => b.Variable == "DecalBuffer" ), $"{name} depth" );
			Assert.IsFalse( shader.Pass( ShaderPasses.DepthAlphaTest + ShaderPasses.GpuDriven ).Pixel.Bindings.Any( b => b.Variable == "DecalBuffer" ), $"{name} GPU-driven depth" );
		}
	}

	/// <summary>
	/// vfx's spirv-opt recipe runs over what Slang emits (SpirvOptimizer): code after a constant return is gone, and so is
	/// the binding only it read, as a .shader's compile drops it.
	/// </summary>
	[TestMethod]
	public void OptimizerStripsDeadCodeAndItsBindings()
	{
		Compiler();
		var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
		if ( !SpirvOptimizer.Available( bin ) ) Assert.Inconclusive( "spirv_optimizer isn't built" );

		var folder = Path.Combine( Path.GetTempPath(), "borealis-optimizer-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var path = Path.Combine( folder, "dead_binding.slang" );
			File.WriteAllText( path, """
				// MODES: Default();
				#include "sbox/pass.slangh"
				#include "system.fxc"
				#include "common.fxc"

				struct PS_INPUT { float4 vPositionSs : SV_Position; };

				#if ( PROGRAM == VFX_PROGRAM_PS )
				Texture2D g_tLive < Attribute( "Live" ); >;
				Texture2D g_tDead < Attribute( "Dead" ); >;
				static const bool kEarly = true;

				[shader( "pixel" )] float4 MainPs( PS_INPUT i ) : SV_Target0
				{
					float4 color = g_tLive.Load( int3( i.vPositionSs.xy, 0 ) );
					if ( kEarly ) return color;
					return color + g_tDead.Load( int3( i.vPositionSs.xy, 0 ) );
				}
				#endif
				""" );

			var compiler = new SlangCompiler( bin, [Path.Combine( Core, "shaders" )] );
			var stage = compiler.CompilePass( PassModule.Read( path, [Path.Combine( Core, "shaders" )] ), StageKind.Pixel, new Dictionary<string, int>() ).Stage;
			Assert.IsTrue( stage.Bindings.Any( b => b.Variable == "g_tLive" ) );
			Assert.IsFalse( stage.Bindings.Any( b => b.Variable == "g_tDead" ), "the binding only dead code read is stripped" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// The converter turns pixel.hlsl-style .shaders into surfaces that compile: the template (Material::Init), bark (a test
	/// copy in Data: the game no longer has it)
	/// (Material.CommonInputs, a helper, a feature global, and its vertex sway as Displace with trunk_bending.hlsl's
	/// functions copied) and water_simple (engine-bound textures as module globals, a helper taking the input, and the
	/// refraction after shading as Composite).
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void ConverterTurnsPixelShadersIntoSurfaces()
	{
		Compiler();
		var game = Path.GetDirectoryName( Core );
		var folder = Path.Combine( Path.GetTempPath(), "borealis-surface-convert-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var bin = EngineFolders.NativeIn( game );
			var shaders = Path.Combine( Core, "shaders" );
			var compiler = new SlangCompiler( bin, [shaders] );
			string Include( string path ) => File.Exists( Path.Combine( shaders, path ) ) ? File.ReadAllText( Path.Combine( shaders, path ) ) : null;
			foreach ( var (relative, name) in new[] { (Path.Combine( AppContext.BaseDirectory, "Data", "material.shader" ), "material"), (Path.Combine( AppContext.BaseDirectory, "Data", "bark.shader" ), "bark"), (Path.Combine( AppContext.BaseDirectory, "Data", "water_simple.shader" ), "water_simple") } )
			{
				var result = ShaderConverter.Convert( File.ReadAllText( Path.Combine( game, relative ) ), name, Include );
				Assert.IsNotNull( result.Source, name );
				var path = Path.Combine( folder, name + ".slang" );
				File.WriteAllText( path, result.Source );
				var compiled = compiler.CompileSurface( path );
				Assert.IsNotNull( compiled.Pass( ShaderPasses.Forward ), name );
				if ( name == "bark" )
				{
					StringAssert.Contains( result.Source, "IVertexSurface" );
					StringAssert.Contains( result.Source, "public float3 Displace( VertexSurfaceInput v )" );
					Assert.IsFalse( result.Notes.Any( n => n.Contains( "vertex shader", StringComparison.Ordinal ) ), string.Join( "; ", result.Notes ) );
					Assert.IsTrue( compiled.Generated.Contains( "SurfaceVertexDisplaced", StringComparison.Ordinal ) );
					Assert.IsNotNull( compiled.Material.Field( "SpecularOcclusion" ) );
					Assert.IsNotNull( compiled.Material.Field( "Color" ) );
				}

				if ( name == "water_simple" )
				{
					StringAssert.Contains( result.Source, "ICompositeSurface" );
					Assert.IsTrue( compiled.Generated.Contains( "SurfaceForwardComposite", StringComparison.Ordinal ) );

					// The refraction and reflection textures are bound by attribute, not material fields
					var forward = compiled.Pass( ShaderPasses.Forward ).Pixel;
					Assert.IsTrue( forward.Bindings.Any( b => b.Name == "RefractionTexture" ), "no RefractionTexture binding" );
					Assert.IsTrue( forward.Bindings.Any( b => b.Name == "ReflectionTexture" ), "no ReflectionTexture binding" );
					Assert.IsNull( compiled.Material.Field( "RefractionTexture" ) );
					Assert.IsNotNull( compiled.Material.Field( "BigWaveScale" ) );
				}
			}
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>Hotload: changing a surface's module drops its program, and the next compile is a fresh one.</summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void ChangedSurfacesHotload()
	{
		Compiler();
		var folder = Path.Combine( Path.GetTempPath(), "borealis-surface-hotload-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var module = Path.Combine( folder, "hot_rock.slang" );
			File.WriteAllText( module, File.ReadAllText( Path.Combine( Surfaces, "rock.slang" ) ).Replace( "public struct Rock", "public struct HotRock", StringComparison.Ordinal ) );
			using var backend = new BackendDevice( Device );
			backend.Surfaces.CacheFolder = Path.Combine( folder, "cache" );
			backend.Surfaces.AddFolder( folder );

			var before = backend.Surfaces.Compile( "hot_rock.slang" );
			Assert.IsNotNull( before );
			Assert.AreSame( before, backend.Surfaces.Compile( "hot_rock.slang" ) );

			File.AppendAllText( module, "\n// changed\n" );
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( backend.Surfaces.Hotloads == 0 && clock.Elapsed.TotalSeconds < 10 ) System.Threading.Thread.Sleep( 50 );
			Assert.IsTrue( backend.Surfaces.Hotloads >= 1, "the change wasn't seen" );

			var after = backend.Surfaces.Compile( "hot_rock.slang" );
			Assert.AreNotSame( before, after );
			Assert.IsFalse( after.FromCache );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Surfaces compiled in the background: a material's first draw finds no program and starts its compile instead of
	/// waiting for it (its draws go to the .shader it stands in for meanwhile); its layout comes alone at once; once the
	/// compile is done the program is there, and the device says so (<see cref="GpuDevice.ProgramsVersion"/>).
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void SurfacesCompileInTheBackground()
	{
		Compiler();
		var folder = Path.Combine( Path.GetTempPath(), "borealis-surface-background-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			using var backend = new BackendDevice( Device );
			backend.Surfaces.CacheFolder = Path.Combine( folder, "cache" );
			backend.Surfaces.AddFolder( Surfaces );
			backend.Surfaces.UseOffline = false;
			backend.Surfaces.CompileInBackground = true;

			var layout = backend.Surfaces.Layout( "rock_lit.slang" );
			Assert.IsNotNull( layout );
			Assert.AreEqual( 0, layout.Passes.Count, "the layout comes alone" );

			using var material = new Rock();
			var version = backend.ProgramsVersion;
			var clock = System.Diagnostics.Stopwatch.StartNew();
			Assert.IsNull( backend.Surfaces.For( material ), "a draw doesn't wait for the compile" );
			Assert.IsTrue( clock.Elapsed.TotalSeconds < 1, $"the first draw took {clock.Elapsed.TotalSeconds:F1} s" );
			Assert.IsTrue( backend.Surfaces.Compiling >= 1 );

			Assert.IsTrue( backend.Surfaces.WaitForCompiles( TimeSpan.FromMinutes( 2 ) ), "the compiles didn't finish" );
			Assert.AreEqual( 0, backend.Surfaces.Compiling );
			Assert.IsTrue( backend.ProgramsVersion > version );
			var program = backend.Surfaces.For( material );
			Assert.IsNotNull( program );
			Assert.IsNotNull( program.Pass( ShaderPasses.Forward ) );
			Assert.AreSame( program, backend.Surfaces.For( material ) );
			Assert.AreEqual( layout.Material.Size, backend.Surfaces.Layout( "rock_lit.slang" ).Material.Size, "the program's layout is the one reflected alone" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A pass module keeps what it read from its preprocessed lines (<see cref="PassModule.DerivedCacheFolder"/>): the next
	/// run's module reads the same entry and render states without preprocessing, and one whose include was edited
	/// preprocesses again. Before, every run preprocessed each module once per combo values.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void PassModuleKeepsItsReadsBetweenRuns()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-pass-derived-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		var interval = PassModule.IncludeCheckMs;
		PassModule.IncludeCheckMs = 0;
		try
		{
			var include = Path.Combine( folder, "states.slangh" );
			File.WriteAllText( include, "RenderState( DepthBias, 4 );\n" );
			var path = Path.Combine( folder, "kept.slang" );
			var text = "#include \"states.slangh\"\n[shader( \"pixel\" )] float4 MainPs() : SV_Target0 { return 1; }\n";
			var cache = Path.Combine( folder, "derived" );
			PassModule Run()
			{
				var module = PassModule.FromText( path, text, [folder] );
				module.DerivedCacheFolder = cache;
				return module;
			}

			var values = new Dictionary<string, int>();
			var first = Run();
			CollectionAssert.AreEqual( new[] { ("DepthBias", "4") }, first.RenderStates( values ).ToArray() );
			Assert.IsTrue( first.Has( StageKind.Pixel, values ) );
			first.SaveDerived();

			var before = PassModule.Preprocesses;
			var second = Run();
			CollectionAssert.AreEqual( new[] { ("DepthBias", "4") }, second.RenderStates( values ).ToArray(), "the kept render states" );
			Assert.IsTrue( second.Has( StageKind.Pixel, values ), "the kept entry" );
			Assert.AreEqual( before, PassModule.Preprocesses, "read, not preprocessed" );

			// Edited within the file system's clock tick: the same time, a different length
			var written = File.GetLastWriteTimeUtc( include );
			File.WriteAllText( include, "RenderState( DepthBias, 16 );\n// longer\n" );
			File.SetLastWriteTimeUtc( include, written );
			CollectionAssert.AreEqual( new[] { ("DepthBias", "16") }, Run().RenderStates( values ).ToArray(), "an edited include preprocesses again" );
		}
		finally
		{
			PassModule.IncludeCheckMs = interval;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A pass module keeps what it preprocessed per combo values only while the includes it read are unchanged: an edited
	/// include's render states are read again (hotload), once its check interval has passed (none here).
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void PassModuleRereadsAnEditedInclude()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-pass-include-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		var interval = PassModule.IncludeCheckMs;
		PassModule.IncludeCheckMs = 0;
		try
		{
			var include = Path.Combine( folder, "states.slangh" );
			File.WriteAllText( include, "RenderState( DepthBias, 4 );\n" );
			var path = Path.Combine( folder, "edited.slang" );
			var module = PassModule.FromText( path, "#include \"states.slangh\"\n[shader( \"pixel\" )] float4 MainPs() : SV_Target0 { return 1; }\n", [folder] );
			var values = new Dictionary<string, int>();
			CollectionAssert.AreEqual( new[] { ("DepthBias", "4") }, module.RenderStates( values ).ToArray() );
			CollectionAssert.AreEqual( new[] { ("DepthBias", "4") }, module.RenderStates( values ).ToArray(), "kept" );

			// Edited within the file system's clock tick (a write takes the clock's last tick as its time): the same time, a
			// different length
			var written = File.GetLastWriteTimeUtc( include );
			File.WriteAllText( include, "RenderState( DepthBias, 16 );\n// longer\n" );
			File.SetLastWriteTimeUtc( include, written );
			CollectionAssert.AreEqual( new[] { ("DepthBias", "16") }, module.RenderStates( values ).ToArray(), "the edited include is read again" );
		}
		finally
		{
			PassModule.IncludeCheckMs = interval;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A cache check trusts a file's last look for <see cref="PassModule.IncludeCheckMs"/>: a variant's stages each check every
	/// include their compile read, and a map's first frame spent ~100 ms asking the file system. A watcher's change
	/// (<see cref="SlangCompiler.SourceChanged"/>) looks again at once.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void FileHashLooksAtAFileOncePerInterval()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-file-hash-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		var interval = PassModule.IncludeCheckMs;
		PassModule.IncludeCheckMs = 60_000;
		try
		{
			var include = Path.Combine( folder, "looked.slangh" );
			File.WriteAllText( include, "// one\n" );
			var first = PassCache.FileHash( include );
			var looks = PassCache.Looks;
			for ( int i = 0; i < 100; i++ ) Assert.AreEqual( first, PassCache.FileHash( include ) );
			Assert.AreEqual( looks, PassCache.Looks, "asked again within the interval" );

			File.WriteAllText( include, "// two, longer\n" );
			SlangCompiler.SourceChanged();
			Assert.AreNotEqual( first, PassCache.FileHash( include ), "a watched change is read at once" );
		}
		finally
		{
			PassModule.IncludeCheckMs = interval;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Pass modules compiled in the background: a standalone module has no programs to fall back to, and a skipped compute
	/// dispatch would leave its outputs unwritten, so its variant is waited for (with its offline compile, as shipped, nothing
	/// compiles in game at all). A converted module's would draw its shader's compiled programs meanwhile; none is left in
	/// core to try it with.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void PassModulesCompileInTheBackground()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-pass-background-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			using var backend = new BackendDevice( Device );
			backend.Passes.CacheFolder = Path.Combine( folder, "cache" );
			backend.Passes.UseOffline = false;
			backend.Passes.CompileInBackground = true;
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			using var drawer = new ShaderDrawer( backend );

			// Standalone: waited for, the module's at the first ask
			var standalone = backend.Shaders.Load( "shaders/clutter_cull_cs.shader" );
			Assert.IsTrue( standalone is { ModuleOnly: true }, "clutter_cull_cs is standalone" );
			Assert.IsTrue( drawer.Variant( standalone, new ShaderAttributes() ) is { FromModule: true }, "a standalone module's variant is waited for" );
			Assert.AreEqual( 0, backend.Passes.PendingAsks );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Drawn unseen (a loading scene's view behind the loading screen, <see cref="PassModules.Unseen"/>), a standalone
	/// module's variant isn't waited for: its stages compile on the compile threads, nothing draws meanwhile, and the load can
	/// wait for <see cref="PassModules.Compiling"/>. Once they're done its variant is the module's.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void UnseenDrawsDontWaitForPassModules()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-pass-unseen-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			using var backend = new BackendDevice( Device );
			backend.Passes.CacheFolder = Path.Combine( folder, "cache" );
			backend.Passes.UseOffline = false;
			backend.Passes.CompileInBackground = true;
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			using var drawer = new ShaderDrawer( backend );

			var standalone = backend.Shaders.Load( "shaders/clutter_cull_cs.shader" );
			Assert.IsTrue( standalone is { ModuleOnly: true }, "clutter_cull_cs is standalone" );
			using ( backend.Passes.Unseen() )
			{
				Assert.IsNull( drawer.Variant( standalone, new ShaderAttributes() ), "an unseen draw doesn't wait for a standalone module" );
			}

			Assert.AreEqual( 1, backend.Passes.PendingAsks );
			Assert.IsTrue( backend.Passes.Compiling >= 1, "its stages compile on the compile threads" );

			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( backend.Passes.Compiling > 0 && clock.Elapsed.TotalMinutes < 2 ) System.Threading.Thread.Sleep( 10 );
			Assert.AreEqual( 0, backend.Passes.Compiling, "the compiles didn't finish" );
			Assert.IsTrue( drawer.Variant( standalone, new ShaderAttributes() ) is { FromModule: true }, "once compiled, its variant is the module's" );
			Assert.AreEqual( 1, backend.Passes.PendingAsks, "and isn't pending" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// The stages a run's draws ask for are kept (<see cref="PassModules.StagesFile"/> in the pass cache), and the next run's
	/// <see cref="PassModules.Prewarm"/> reads them ahead on workers: its first draw finds its stages made, so even unseen it
	/// draws. Without <see cref="PassModules.Prewarm"/> (tests, labs) nothing is kept.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void PrewarmReadsTheLastRunsStages()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-pass-prewarm-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var cache = Path.Combine( folder, "cache" );
			using ( var first = new BackendDevice( Device ) )
			{
				first.Passes.CacheFolder = cache;
				first.Passes.UseOffline = false;
				Assert.IsTrue( first.Passes.Prewarm().Wait( TimeSpan.FromMinutes( 1 ) ) );
				Assert.AreEqual( 0, first.Passes.ReadAhead, "nothing kept yet" );
				using var drawer = new ShaderDrawer( first );
				Assert.IsTrue( drawer.Variant( first.Shaders.Load( "shaders/clutter_cull_cs.shader" ), new ShaderAttributes() ) is { FromModule: true } );
				first.Passes.SaveStages();
			}

			var kept = File.ReadAllLines( Path.Combine( cache, PassModules.StagesFile ) );
			Assert.IsTrue( kept.Skip( 1 ).Any( l => l.Contains( "clutter_cull_cs.slang\tCompute\t", StringComparison.Ordinal ) ), string.Join( "\n", kept ) );

			using var second = new BackendDevice( Device );
			second.Passes.CacheFolder = cache;
			second.Passes.UseOffline = false;
			second.Passes.CompileInBackground = true;
			Assert.IsTrue( second.Passes.Prewarm().Wait( TimeSpan.FromMinutes( 2 ) ) );
			Assert.IsTrue( second.Passes.ReadAhead >= 1, "the kept stage is read ahead" );
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( second.Passes.Compiling > 0 && clock.Elapsed.TotalMinutes < 2 ) System.Threading.Thread.Sleep( 10 );
			using var again = new ShaderDrawer( second );
			using ( second.Passes.Unseen() )
			{
				Assert.IsTrue( again.Variant( second.Shaders.Load( "shaders/clutter_cull_cs.shader" ), new ShaderAttributes() ) is { FromModule: true }, "the read-ahead stage draws at once" );
			}

			Assert.AreEqual( 0, second.Passes.PendingAsks );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>A stage no draw asked for in <see cref="PassModules.KeptRuns"/> runs is dropped; a malformed line is skipped.</summary>
	[TestMethod]
	public void KeptStagesAgeOut()
	{
		var path = Path.Combine( Path.GetTempPath(), "borealis-stages-test-" + Guid.NewGuid().ToString( "N" ) + ".txt" );
		try
		{
			var last = 20;
			var stages = new Dictionary<string, int>
			{
				["C:/a.slang\tPixel\tD_X=1;S_MODE_DEPTH=0"] = last,
				["C:/b.slang\tCompute\t"] = last - PassModules.KeptRuns + 1,
				["C:/old.slang\tVertex\tS_MODE_DEPTH=1"] = last - PassModules.KeptRuns,
			};
			File.WriteAllText( path, PassModules.WriteStages( last, stages ) + "garbage line\n" );

			var ahead = PassModules.ReadStages( path, out var run, out var version, out var kept );
			Assert.AreEqual( last + 1, run );
			Assert.AreEqual( SlangCompiler.Version, version );
			CollectionAssert.AreEquivalent( new[] { "C:/a.slang", "C:/b.slang" }, ahead.Select( a => a.Path ).ToArray() );
			var pixel = ahead.Single( a => a.Path == "C:/a.slang" );
			Assert.AreEqual( StageKind.Pixel, pixel.Kind );
			Assert.AreEqual( 1, pixel.Values["D_X"] );
			Assert.AreEqual( 0, pixel.Values["S_MODE_DEPTH"] );
			Assert.AreEqual( 0, ahead.Single( a => a.Path == "C:/b.slang" ).Values.Count );
			Assert.AreEqual( 2, kept.Count );

			Assert.AreEqual( 0, PassModules.ReadStages( path + ".missing", out run, out _, out _ ).Count );
			Assert.AreEqual( 0, run );
		}
		finally
		{
			File.Delete( path );
		}
	}

	/// <summary>
	/// Offline compiles (<see cref="SlangOutput"/>): a surface and a pass module compiled offline are read, not compiled, by
	/// another compiler with an empty cache, the same SPIR-V, wherever the files they read are; not with offline compiles
	/// off, nor once the source changes. An unchanged source isn't compiled offline again.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void OfflineCompilesServeTheGame()
	{
		Compiler();
		var folder = Path.Combine( Path.GetTempPath(), "borealis-offline-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var bin = EngineFolders.NativeIn( Path.GetDirectoryName( Core ) );
			var shaders = Path.Combine( Core, "shaders" );
			var module = Path.Combine( folder, "offline_rock.slang" );
			File.WriteAllText( module, File.ReadAllText( Path.Combine( Surfaces, "rock.slang" ) ).Replace( "public struct Rock", "public struct OfflineRock", StringComparison.Ordinal ) );
			var pass = PassModule.Read( Path.Combine( shaders, "depthresolve.slang" ), [shaders] );
			var log = new List<string>();
			var offline = new SlangCompiler( bin, [folder, shaders] ) { CacheFolder = Path.Combine( folder, "cache1" ), UseOffline = false };
			Assert.AreEqual( OfflineResult.Compiled, offline.CompileOfflineAsync( module, log: log.Add ).GetAwaiter().GetResult(), string.Join( "\n", log ) );
			Assert.AreEqual( OfflineResult.Compiled, offline.CompileOfflineAsync( pass.Path, Path.Combine( folder, "depthresolve.slang_c" ), log.Add ).GetAwaiter().GetResult(), string.Join( "\n", log ) );
			Assert.AreEqual( OfflineResult.UpToDate, offline.CompileOfflineAsync( module ).GetAwaiter().GetResult() );
			Assert.IsTrue( File.Exists( SlangOutput.PathOf( module ) ) );

			var compiled = offline.CompileSurface( module );
			var compiling = new List<string>();
			var game = new SlangCompiler( bin, [folder, shaders] ) { CacheFolder = Path.Combine( folder, "cache2" ), Compiling = compiling.Add };
			var read = game.CompileSurface( module );
			Assert.IsTrue( read.Offline, "the surface came from its offline compile" );
			foreach ( var (name, p) in compiled.Passes )
			{
				CollectionAssert.AreEqual( p.Vertex.Spirv, read.Pass( name ).Vertex.Spirv, name );
				if ( p.Pixel is not null ) CollectionAssert.AreEqual( p.Pixel.Spirv, read.Pass( name ).Pixel.Spirv, name );
			}

			Assert.IsTrue( read.Dependencies.Any( d => d.EndsWith( "lighting.slangh", StringComparison.OrdinalIgnoreCase ) ), "the files it read, found again" );
			var values = pass.Combos( StageKind.Pixel ).ToDictionary( c => c.Name, c => c.Min );
			Assert.IsTrue( game.CompilePass( pass, StageKind.Pixel, values ).Offline, "the pass stage came from its offline compile" );
			Assert.AreEqual( 0, compiling.Count, string.Join( "; ", compiling ) );

			var off = new SlangCompiler( bin, [folder, shaders] ) { CacheFolder = Path.Combine( folder, "cache3" ), UseOffline = false };
			Assert.IsFalse( off.CompileSurface( module ).Offline );

			File.AppendAllText( module, "\n// changed\n" );
			var changed = new SlangCompiler( bin, [folder, shaders] ) { CacheFolder = Path.Combine( folder, "cache4" ), Compiling = compiling.Add };
			Assert.IsFalse( changed.CompileSurface( module ).Offline, "a changed module compiles again" );
			Assert.AreEqual( 1, compiling.Count, "and says so" );
			Assert.AreEqual( OfflineResult.Compiled, offline.CompileOfflineAsync( module ).GetAwaiter().GetResult() );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
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

		// A sun-lit surface (foliage) lights once, outside the loop, and leaves the loop alone
		var sunLit = Compiler().CompileSurface( Path.Combine( Core, "shaders", "foliage.slang" ) );
		StringAssert.Contains( sunLit.Generated, "SurfaceForwardSunLit<Foliage, true>" );
		Assert.IsFalse( sunLit.Generated.Contains( "SURFACE_LIGHT_HOOK", StringComparison.Ordinal ) );
	}
	/// <summary>The generated classes in BorealisLab are what the compiler writes now, and a raw module has the same record.</summary>
	[TestMethod]
	public void GeneratedClassesAreCurrent()
	{
		var compiler = Compiler();
		var rock = compiler.CompileSurface( Path.Combine( Surfaces, "rock.slang" ) );
		var raw = compiler.CompileRaw( Path.Combine( Surfaces, "rock_raw.slang" ) );

		Assert.AreEqual( ShaderTier.Raw, raw.Tier );
		CollectionAssert.AreEqual( rock.Material.Fields.Select( f => (f.Kind, f.Offset) ).ToArray(), raw.Material.Fields.Select( f => (f.Kind, f.Offset) ).ToArray() );
		var lit = compiler.CompileSurface( Path.Combine( Surfaces, "rock_lit.slang" ) );
		var stage = compiler.CompileSurface( Path.Combine( Surfaces, "rock_stage.slang" ) );
		var shaded = compiler.CompileSurface( Path.Combine( Surfaces, "rock_shaded.slang" ) );
		foreach ( var (shader, file) in new[] { (rock, "rock.g.cs"), (raw, "rock_raw.g.cs"), (lit, "rock_lit.g.cs"), (stage, "rock_stage.g.cs"), (shaded, "rock_shaded.g.cs") } )
		{
			var expected = MaterialClassWriter.Write( shader, "Sandbox.BorealisLab.Surfaces" ).Replace( "\r\n", "\n" );
			var actual = File.ReadAllText( Path.Combine( Surfaces, file ) ).Replace( "\r\n", "\n" );
			Assert.AreEqual( expected, actual, $"{file} is stale: run borealislab -surface-parity <folder> -surface-classes" );
		}
	}

	/// <summary>
	/// Material.CommonInputs.hlsl's TextureFiltering (in the USP's glass_scope.shader, a .shader the game still compiles)
	/// takes its filter from an expression on F_TEXTURE_FILTERING, evaluated per material: anisotropic by default, and the
	/// feature's other values pick bilinear, trilinear and point. Materials that differ only in it don't share a variant.
	/// </summary>
	[TestMethod]
	public void ExpressionFilteredSamplersFollowTheirFeature()
	{
		using var backend = new BackendDevice( Device );
		var scope = Core is { } core ? Path.GetFullPath( Path.Combine( core, "..", "addons", "content", "Assets", "weapons", "usp", "materials", "glass_scope.shader_c" ) ) : null;
		if ( scope is null || !File.Exists( scope ) ) Assert.Inconclusive( "glass_scope isn't compiled" );
		var file = Borealis.Source2.Shaders.ShaderFile.Load( scope );
		List<SamplerInfo> Samplers( int filtering )
		{
			using var glass = new BackendMaterial( $"Glass scope {filtering}", file, null, null );
			var feature = file.FeatureIndex( "F_TEXTURE_FILTERING" );
			Assert.IsTrue( feature >= 0, "glass_scope.shader has no F_TEXTURE_FILTERING" );
			glass.Features[feature] = filtering;
			var variant = glass.Variant( backend.Shaders, "Forward", "Forward", _ => null, _ => null );
			return variant.Resources.Where( r => r.Kind == Borealis.Source2.Shaders.ShaderResourceKind.Sampler && r.Set == ShaderAbi.FragmentSet ).Select( r => r.Sampler ).ToList();
		}

		// 0, anisotropic: MaxAniso( 8 ) beside the expression. Unevaluated, it read as point filtering with anisotropy 8
		var anisotropic = Samplers( 0 );
		Assert.IsTrue( anisotropic.Any( s => s.MaxAnisotropy == 8 && s.MinFilter == Filter.Linear && s.MipFilter == Filter.Linear ), "TextureFiltering isn't anisotropic" );
		Assert.IsFalse( anisotropic.Any( s => s.MaxAnisotropy > 1 && s.MinFilter == Filter.Nearest ), "a point sampler with anisotropy" );

		// 1, bilinear: linear within a mip, nearest between them
		Assert.IsTrue( Samplers( 1 ).Any( s => s.MaxAnisotropy == 1 && s.MinFilter == Filter.Linear && s.MagFilter == Filter.Linear && s.MipFilter == Filter.Nearest ), "F_TEXTURE_FILTERING 1 isn't bilinear" );

		// 2, trilinear
		Assert.IsTrue( Samplers( 2 ).Any( s => s.MaxAnisotropy == 1 && s.MinFilter == Filter.Linear && s.MipFilter == Filter.Linear ), "F_TEXTURE_FILTERING 2 isn't trilinear" );

		// 3, point
		var point = Samplers( 3 );
		Assert.IsTrue( point.Any( s => s.MaxAnisotropy == 1 && s.MinFilter == Filter.Nearest && s.MagFilter == Filter.Nearest ), "F_TEXTURE_FILTERING 3 isn't point" );
		Assert.IsFalse( point.Any( s => s.MaxAnisotropy == 8 ), "F_TEXTURE_FILTERING 3 kept the anisotropic sampler (shared the variant)" );
	}

	/// <summary>
	/// Render states given by expressions are evaluated per material too: tools_selection_overlay's CullMode follows
	/// F_NO_CULLING, and materials that differ in it don't share a variant.
	/// </summary>
	[TestMethod]
	public void ExpressionRenderStatesFollowTheirFeature()
	{
		using var backend = new BackendDevice( Device );
		CullMode Cull( int noCulling )
		{
			var overlay = (BackendMaterial)((GpuDevice)backend).CreateMaterial( $"Overlay {noCulling}", "shaders/tools_selection_overlay.shader" );
			if ( overlay.Shader is null ) Assert.Inconclusive( "tools_selection_overlay isn't compiled" );
			var feature = overlay.Shader.FeatureIndex( "F_NO_CULLING" );
			Assert.IsTrue( feature >= 0, "tools_selection_overlay has no F_NO_CULLING" );
			overlay.Features[feature] = noCulling;
			return overlay.Variant( backend.Shaders, "Default", "Default", _ => null, _ => null ).State.Cull;
		}

		Assert.AreEqual( CullMode.Back, Cull( 0 ) );
		Assert.AreEqual( CullMode.None, Cull( 1 ) );
	}

	/// <summary>
	/// The expression evaluator runs the stack machine as native's VfxEval does: literals, feature loads, comparisons and
	/// conditional jumps, swizzles and builtins; reads it can't answer fail rather than guess.
	/// </summary>
	[TestMethod]
	public void ShaderExpressionsEvaluateAsNativeDoes()
	{
		static byte[] Literal( float value ) => [7, .. BitConverter.GetBytes( value )];
		var context = new Borealis.Source2.Shaders.ShaderExpressionContext { Features = [0, 3] };
		static float Evaluate( byte[] code, Borealis.Source2.Shaders.ShaderExpressionContext context ) =>
			Borealis.Source2.Shaders.ShaderExpression.TryEvaluate( code, context, out var value ) ? value.X : float.NaN;

		// F_1 == 3 ? 85 : 20, laid out as the builder lays out a ternary: test, conditional jump, else, jump, then
		byte[] ternary = [26, 1, .. Literal( 3 ), 13, 4, 13, 0, 21, 0, .. Literal( 85 ), 2, 26, 0, .. Literal( 20 ), 0];
		Assert.AreEqual( 85f, Evaluate( ternary, context ) );
		Assert.AreEqual( 20f, Evaluate( ternary, new Borealis.Source2.Shaders.ShaderExpressionContext { Features = [0, 1] } ) );

		// clamp( 2 * 3, 0, 5 ) + max( 1, -1 ): builtins by table index (clamp 7, max 29)
		byte[] math = [.. Literal( 2 ), .. Literal( 3 ), 21, .. Literal( 0 ), .. Literal( 5 ), 6, 7, 0, .. Literal( 1 ), .. Literal( -1 ), 6, 29, 0, 19, 0];
		Assert.AreEqual( 6f, Evaluate( math, context ) );

		// float2( 1, 2 ).y via a swizzle (yyyy)
		byte[] swizzle = [.. Literal( 1 ), .. Literal( 2 ), 6, 26, 0, 30, 0b01_01_01_01, 0];
		Assert.AreEqual( 2f, Evaluate( swizzle, context ) );

		// A render attribute with nothing to answer it fails; exists() of it is 0
		Assert.IsTrue( float.IsNaN( Evaluate( [25, 1, 2, 3, 4, 0], context ) ) );
		Assert.AreEqual( 0f, Evaluate( [31, 1, 2, 3, 4, 0], context ) );
	}

	/// <summary>
	/// The surface and the raw module draw what the same material written as a .shader draws, set from C# through
	/// their generated classes; changing a field from C# changes the picture.
	/// </summary>
	[TestMethod]
	public void SurfaceAndRawDrawAsTheShaderDoes()
	{
		Compiler();
		using var backend = new BackendDevice( Device, Fixtures.WithCompiledShaders() ) { TimeDraws = true };
		backend.Surfaces.AddFolder( Surfaces );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var shader = (BackendMaterial)((GpuDevice)backend).CreateMaterial( "Rock (.shader)", "shaders/surface_test_rock.shader" );
			if ( shader.Shader?.Name.Contains( "surface_test_rock", StringComparison.OrdinalIgnoreCase ) != true ) Assert.Inconclusive( "surface_test_rock.shader isn't compiled" );

			var color = backend.LoadTexture( Fixtures.Rope.Color );
			var normal = backend.LoadTexture( Fixtures.Rope.Normal );
			var rma = backend.LoadTexture( Fixtures.Rope.Rma );
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

			// The record offset passed on per pixel instead of read per draw (a classic draw's default) draws the same
			Assert.IsFalse( backend.Surfaces.MaterialOffsetPerPixel, "Classic draws should read their record per draw" );
			backend.Surfaces.MaterialOffsetPerPixel = true;
			var perPixel = Draw( backend, surface );
			backend.Surfaces.MaterialOffsetPerPixel = false;
			Assert.IsTrue( Mean( reference, perPixel ) < 0.5, $"per pixel differs by {Mean( reference, perPixel ):F3}" );

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

	/// <summary>
	/// A material of complex's that sets both cloth shading and the metalness texture (Needlestack's gloves) draws as complex
	/// draws it: its rule Allow1( F_CLOTH_SHADING, F_METALNESS_TEXTURE ) keeps cloth, so its colour's alpha isn't metalness and
	/// standard draws it as the same material without them, not black as metal.
	/// </summary>
	[TestMethod]
	public void ClothKeepsComplexsMetalnessTextureOff()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			static MaterialFile Complex( string name, string color, params (string Feature, int Value)[] features ) => new()
			{
				Name = name,
				Shader = "shaders/complex.shader",
				Ints = features.ToDictionary( f => f.Feature, f => f.Value, StringComparer.OrdinalIgnoreCase ),
				Floats = new Dictionary<string, float>(),
				Vectors = new Dictionary<string, float[]>(),
				Textures = new Dictionary<string, string> { ["g_tColor"] = color },
				IntAttributes = new Dictionary<string, int>(),
			};

			// A colour whose alpha is opaque, as the gloves' is: the metalness texture alone then reads it as metal
			string[] colors = ["materials/default/white_vmat_g_tcolor_b051cf30.generated.vtex", "materials/default/default_color_tga_4a03ed87.generated.vtex",
				"materials/default/default_color_tga_380558b.generated.vtex", Fixtures.Rope.Color];
			foreach ( var color in colors.Where( c => backend.Content.Find( c ) is not null ) )
			{
				using var plain = backend.LoadMaterial( $"materials/test_glove_plain_{color.GetHashCode():x}.vmat", Complex( "plain", color ) );
				using var metal = backend.LoadMaterial( $"materials/test_glove_metal_{color.GetHashCode():x}.vmat", Complex( "metal", color, ("F_METALNESS_TEXTURE", 1) ) );
				var fromPlain = Draw( backend, plain );
				if ( Mean( fromPlain, Draw( backend, metal ) ) < 2 ) continue;

				using var gloves = backend.LoadMaterial( $"materials/test_glove_cloth_{color.GetHashCode():x}.vmat", Complex( "gloves", color, ("F_CLOTH_SHADING", 1), ("F_METALNESS_TEXTURE", 1) ) );
				Assert.IsInstanceOfType<SurfaceMaterial>( gloves, "complex draws as standard" );
				var fromGloves = Draw( backend, gloves );
				SaveRgb( fromGloves, "surface-cloth-metalness" );
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				Assert.IsTrue( Mean( fromPlain, fromGloves ) < 0.5, $"with cloth it isn't metal: {Mean( fromPlain, fromGloves ):F3} from the plain material ({color})" );
				return;
			}

			Assert.Inconclusive( "no colour texture with an opaque alpha is built" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A surface that owns its vertex stage (rock_stage.slang, IVertexStageSurface): its vertex input is its own struct's,
	/// read in every pass, with the instance transform; its Varyings' float fields get derivatives and its integer ones
	/// don't; its clip distance is in the depth stage too; it has no skinned or GPU-driven passes, so it draws classic.
	/// </summary>
	[TestMethod]
	public void VertexStageSurfaceOwnsItsPasses()
	{
		var stage = Compiler().CompileSurface( Path.Combine( Surfaces, "rock_stage.slang" ) );
		Assert.IsNull( stage.GpuDrivenError, stage.GpuDrivenError );
		StringAssert.Contains( stage.Generated, "SurfaceStageVertex<RockStage>" );
		StringAssert.Contains( stage.Generated, "#define SURFACE_CLIP_DISTANCES 1" );
		StringAssert.Contains( stage.Generated, "d.Ddx.Uv = ddx( v.Uv );" );
		Assert.IsFalse( stage.Generated.Contains( "d.Ddx.Instance", StringComparison.Ordinal ), "an integer field got a derivative" );

		foreach ( var name in new[] { ShaderPasses.Forward, ShaderPasses.Forward + "AlphaTest", ShaderPasses.Forward + "Translucent", ShaderPasses.Depth, ShaderPasses.DepthAlphaTest } )
		{
			foreach ( var suffix in new[] { "", ShaderPasses.Lightmapped } )
			{
				var pass = stage.Pass( name + suffix );
				Assert.IsNotNull( pass, name + suffix );
				var inputs = pass.Vertex.Inputs.Select( i => i.Semantic + i.SemanticIndex ).ToArray();
				foreach ( var semantic in new[] { "POSITION0", "TEXCOORD13" } ) CollectionAssert.Contains( inputs, semantic, $"{name}{suffix} doesn't read {semantic}" );
				Assert.IsTrue( WritesClipDistance( pass.Vertex.Spirv ), $"{name}{suffix}'s vertex stage writes no clip distance" );
			}

			Assert.IsNull( stage.Pass( name + ShaderPasses.Skinned ), $"{name} has a skinned pass" );
			Assert.IsNull( stage.Pass( name + ShaderPasses.GpuDriven ), $"{name} has a GPU-driven pass" );
		}

		// The forward vertex stage reads what the surface's vertex input declares, nothing of the engine's VertexInput
		var forward = stage.Pass( ShaderPasses.Forward ).Vertex.Inputs.Select( i => i.Semantic + i.SemanticIndex ).ToArray();
		CollectionAssert.AreEquivalent( new[] { "POSITION0", "NORMAL0", "TEXCOORD0", "TANGENT0", "TEXCOORD13" }, forward, string.Join( ", ", forward ) );

		using var backend = new BackendDevice( Device );
		using var program = new SurfaceProgram( backend, stage );
		Assert.IsTrue( program.Displaces );
		Assert.IsNull( program.GpuDrivenPass( false, false, false ) );

		// A shaded surface (rock_shaded.slang, IShadedSurface) is lit by its own Shade
		var shaded = Compiler().CompileSurface( Path.Combine( Surfaces, "rock_shaded.slang" ) );
		StringAssert.Contains( shaded.Generated, "SurfaceForwardShaded<RockShaded, false>" );
		Assert.IsNull( shaded.GpuDrivenError, shaded.GpuDrivenError );
	}

	/// <summary>
	/// terrain_surface.slang (terrain.shader as a surface that owns its vertex stage) compiles: its vertex stage reads the
	/// meshlet mesh's position alone, every pass clips holes, and the layers sample with the local position's derivatives.
	/// </summary>
	[TestMethod]
	public void TerrainSurfaceCompiles()
	{
		var terrain = Compiler().CompileSurface( Path.Combine( Core, "shaders", "terrain_surface.slang" ) );
		StringAssert.Contains( terrain.Generated, "d.Ddx.LocalPosition = ddx( v.LocalPosition );" );
		foreach ( var name in new[] { ShaderPasses.Forward, ShaderPasses.Depth } )
		{
			var pass = terrain.Pass( name );
			var inputs = pass.Vertex.Inputs.Select( i => i.Semantic + i.SemanticIndex ).ToArray();
			Assert.IsTrue( inputs.Contains( "POSITION0" ) && inputs.All( i => i is "POSITION0" or "TEXCOORD13" ), string.Join( ", ", inputs ) );
			Assert.IsTrue( WritesClipDistance( pass.Vertex.Spirv ), $"{name} doesn't clip holes" );
			Assert.IsTrue( pass.Vertex.Bindings.Any( b => b.Name == "TerrainMeshlets" ), $"{name} doesn't read the meshlets" );
		}

		// The grid is the D_GRID combo, a specialization constant in both stages
		Assert.IsTrue( terrain.Pass( ShaderPasses.Forward ).Pixel.Specializations.Any( s => s.Attribute == "D_GRID" ) );
	}

	/// <summary>
	/// water_surface.slang (water.shader as a surface that owns its vertex stage and shades itself) compiles: its vertex stage
	/// reads the clipmap mesh's cells and levels, its forward pass lights two-sided ([Lighting( "S_DIFFUSE_TWO_SIDED" )])
	/// and shades itself, and its material takes the .vmat's values by name.
	/// </summary>
	[TestMethod]
	public void WaterSurfaceCompiles()
	{
		var water = Compiler().CompileSurface( Path.Combine( Core, "shaders", "water_surface.slang" ) );
		StringAssert.Contains( water.Generated, "#define S_DIFFUSE_TWO_SIDED 1" );
		StringAssert.Contains( water.Generated, "SurfaceForwardShaded<SurfaceWithStageShaded<Water, Water>, false>" );
		StringAssert.Contains( water.Generated, "d.Ddx.Rest = ddx( v.Rest );" );
		var inputs = water.Pass( ShaderPasses.Forward ).Vertex.Inputs.Select( i => i.Semantic + i.SemanticIndex ).ToArray();
		foreach ( var semantic in new[] { "TEXCOORD0", "TEXCOORD1" } ) CollectionAssert.Contains( inputs, semantic, string.Join( ", ", inputs ) );
		Assert.AreEqual( "g_fRoughness", water.Material.Field( "Roughness" ).Source );
		Assert.IsTrue( water.Pass( ShaderPasses.Forward ).Pixel.Bindings.Any( b => b.Name == "RefractionTexture" ) );
	}

	/// <summary>Whether SPIR-V declares the ClipDistance capability (OpCapability 32): a stage that writes clip distances.</summary>
	static bool WritesClipDistance( byte[] spirv )
	{
		for ( int i = 20; i + 8 <= spirv.Length; i += 4 )
		{
			if ( BitConverter.ToUInt32( spirv, i ) == 0x00020011u && BitConverter.ToUInt32( spirv, i + 4 ) == 32u ) return true;
		}

		return false;
	}

	/// <summary>
	/// rock.slang written with a vertex stage of its own (rock_stage.slang: the engine's transform, its uv a Varying read with
	/// its derivatives) and shading itself (rock_shaded.slang: the engine's lighting and fog) draws as rock.slang does; the
	/// vertex stage's clip distance cuts the box below its middle.
	/// </summary>
	[TestMethod]
	public void VertexStageAndShadedSurfacesDrawAsTheSurfaceDoes()
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
			var color = backend.LoadTexture( Fixtures.Rope.Color );
			var normal = backend.LoadTexture( Fixtures.Rope.Normal );
			var rma = backend.LoadTexture( Fixtures.Rope.Rma );
			var tint = new Vector3( 1, 0.8f, 0.6f );
			using var surface = new Rock { Color = color, Normal = normal, Rma = rma, Tint = tint };
			using var stage = new RockStage { Color = color, Normal = normal, Rma = rma, Tint = tint };
			using var shaded = new RockShaded { Color = color, Normal = normal, Rma = rma, Tint = tint };

			var reference = Draw( backend, surface );
			var fromStage = Draw( backend, stage );
			var fromShaded = Draw( backend, shaded );
			stage.ClipHeight = 0;
			var clipped = Draw( backend, stage );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
			Assert.AreEqual( 0, backend.DrawTimes.Failed.Count, "Draws failed: " + string.Join( ", ", backend.DrawTimes.Failed.Select( f => $"{f.Key} x{f.Value}" ) ) );
			SaveRgb( reference, "stage-surface" );
			SaveRgb( fromStage, "stage-stage" );
			SaveRgb( fromShaded, "stage-shaded" );
			SaveRgb( clipped, "stage-clipped" );
			Assert.IsTrue( Lit( reference ) > Width * Height / 4, "The surface drew too little to compare" );
			Assert.IsTrue( Mean( reference, fromStage ) < 0.5, $"the own vertex stage differs by {Mean( reference, fromStage ):F3}" );
			Assert.IsTrue( Mean( reference, fromShaded ) < 0.5, $"the shaded surface differs by {Mean( reference, fromShaded ):F3}" );
			Assert.IsTrue( Mean( reference, clipped ) > 2, $"the clip distance cut nothing: {Mean( reference, clipped ):F3} from the whole box" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The converter turns a .shader of the final combiner shape (simple.shader, kept under Data: the standard processing,
	/// lit by the final combiner) into a combiner surface with its features as fields, colour-alpha occlusion included;
	/// shaders that do more than that shape stay as they are. simple's materials load as standard's surface by default.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void ConverterTurnsStandardCombinerShadersIntoSurfaces()
	{
		var compiler = Compiler();
		var shaders = Path.Combine( Core, "shaders" );
		string Convert( string name, out IReadOnlyList<string> notes )
		{
			// static_overlay.shader, generic.shader and simple.shader are gone (surfaces stand in alone): the test keeps copies
			var data = Path.Combine( AppContext.BaseDirectory, "Data", name + ".shader" );
			var result = ShaderConverter.Convert( File.ReadAllText( File.Exists( data ) ? data : Path.Combine( shaders, name + ".shader" ) ), name );
			notes = result.Notes;
			return result.Source;
		}

		// Shaders that do more than the standard processing: their notes say why
		Assert.IsNull( Convert( "static_overlay", out var notes ), "static_overlay converted" );
		StringAssert.Contains( notes[0], "MainPs works on" );
		Assert.IsNull( Convert( "generic", out notes ), "generic converted" );
		StringAssert.Contains( notes[0], "FinalCombiner" );

		var folder = Path.Combine( Path.GetTempPath(), "borealis-surface-combiner-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		var previousHost = RenderHost.Current;
		try
		{
			var source = Convert( "simple", out notes );
			Assert.IsNotNull( source, string.Join( "; ", notes ) );
			StringAssert.Contains( source, "ICombinerSurface" );
			File.WriteAllText( Path.Combine( folder, "simple.slang" ), source );
			var compiled = compiler.CompileSurface( Path.Combine( folder, "simple.slang" ) );
			Assert.IsTrue( compiled.Generated.Contains( "SurfaceForwardCombiner", StringComparison.Ordinal ) );
			foreach ( var field in new[] { "UseMetalnessTexture", "UseAmbientOcclusionTexture", "Metalness", "MetalnessMap", "AmbientOcclusion", "Color", "Normal" } )
				Assert.IsNotNull( compiled.Material.Field( field ), field );

			// simple.shader doesn't do these: no fields for them
			Assert.IsNull( compiled.Material.Field( "TintMask" ) );
			Assert.IsNull( compiled.Material.Field( "AlphaTestReference" ) );

			// Its render state: opaque, back faces drawn with F_RENDER_BACKFACES (the standard processing's S_RENDER_BACKFACES)
			StringAssert.Contains( source, "[Material]" );
			StringAssert.Contains( source, "Source( \"F_RENDER_BACKFACES\" ), RenderState( \"TwoSided: true\" )" );
			Assert.AreEqual( SurfaceRenderDefaults.None, compiled.Material.RenderState );

			// By default simple's and complex's materials load as standard's surface (they're its names)
			using var defaults = new BackendDevice( Device );
			RenderHost.Current = new BackendRenderHost( defaults );
			Assert.IsTrue( defaults.Surfaces.ConvertShaders );
			Assert.IsInstanceOfType<SurfaceMaterial>( ((GpuDevice)defaults).LoadMaterial( "materials/dev/simple/floor_simple_128.vmat" ), "simple isn't a surface by default" );
			Assert.IsInstanceOfType<SurfaceMaterial>( ((GpuDevice)defaults).LoadMaterial( "materials/default.vmat" ), "complex isn't a surface by default" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// standard takes complex, simple and generic materials, each with its shader's packing (the engine's own name standard,
	/// complex's), and a pipeline specialised for a material's features (its [Specialize] fields as constants) draws what the
	/// unspecialised one does.
	/// </summary>
	[TestMethod]
	public void StandardSurfaceSpecializesForItsMaterials()
	{
		var compiled = Compiler().CompileSurface( Path.Combine( Core, "shaders", "standard.slang" ) );
		Assert.AreEqual( "UseUnlit", string.Join( ", ", compiled.Material.Features.Select( f => f.Name ) ), "only [Specialize] fields are specialised for" );
		StringAssert.Contains( compiled.Generated, "SurfaceSpecialize( m );" );

		// Materials an older engine compiled for simple or generic keep their packing
		foreach ( var (shaderName, expected) in new[] { ("shaders/standard.shader", 0), ("shaders/complex.shader", 0), ("shaders/simple.shader", 1), ("shaders/generic.shader", 2) } )
		{
			var file = new Borealis.Source2.Materials.MaterialFile { Name = "packing", Shader = shaderName, Ints = new Dictionary<string, int>(), Floats = new Dictionary<string, float>(), Vectors = new Dictionary<string, float[]>(), Textures = new Dictionary<string, string>(), IntAttributes = new Dictionary<string, int>() };
			using var made = ConvertedMaterial.Create( compiled, file, "packing", _ => null );
			var bytes = new byte[made.RecordSize];
			made.WriteRecord( bytes, ( _, _ ) => 0 );
			Assert.AreEqual( expected, BitConverter.ToInt32( bytes, compiled.Material.Field( "Packing" ).Offset ), shaderName );
		}

		var previousHost = RenderHost.Current;
		try
		{
			byte[] DrawMaterial( string path, bool specialize, out SurfaceMaterial loaded, out int specialized )
			{
				using var backend = new BackendDevice( Device );
				var errors = new List<string>();
				backend.Surfaces.Error = errors.Add;
				backend.Surfaces.SpecializeFeatures = specialize;
				backend.Surfaces.SpecializeInBackground = false;
				RenderHost.Current = new BackendRenderHost( backend );
				var material = ((GpuDevice)backend).LoadMaterial( path );
				if ( material is not SurfaceMaterial ) Assert.Inconclusive( $"{path} isn't compiled, or isn't a surface" );
				loaded = (SurfaceMaterial)material;
				var picture = Draw( backend, loaded );
				specialized = loaded.Program?.Specialized.Made ?? 0;
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				return picture;
			}

			foreach ( var (path, packing) in new[] { ("materials/default.vmat", 0), ("materials/dev/simple/floor_simple_128.vmat", 0), ("materials/dev/primary_blue.vmat", 0), ("materials/dev/sky_helper.vmat", 0) } )
			{
				var plain = DrawMaterial( path, false, out _, out var none );
				var specialized = DrawMaterial( path, true, out var material, out var made );
				StringAssert.EndsWith( material.Shader, "standard.slang", path );
				var record = new byte[material.RecordSize];
				material.WriteRecord( record, ( _, _ ) => 0 );
				Assert.AreEqual( packing, BitConverter.ToInt32( record, material.Program.Compiled.Material.Field( "Packing" ).Offset ), $"{path}: packing" );
				Assert.AreEqual( 0, none, $"{path}: specialised with specialization off" );
				Assert.IsTrue( made > 0, $"{path}: no specialised pipeline" );
				var name = Path.GetFileNameWithoutExtension( path );
				SaveRgb( plain, $"standard-{name}-plain" );
				SaveRgb( specialized, $"standard-{name}-specialized" );
				Assert.IsTrue( Mean( plain, specialized ) < 0.05, $"{path}: specialised differs by {Mean( plain, specialized ):F3}" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A material's specialised pipeline, made on a worker, waits while it's held (by default: while a pipeline a draw skips
	/// is pending, <c>SurfaceShaders.SpecializeSlots</c>), the unspecialised one drawing meanwhile, and is made once it isn't.
	/// </summary>
	[TestMethod]
	public void SpecializedPipelinesWaitWhileHeld()
	{
		var previousHost = RenderHost.Current;
		try
		{
			using var backend = new BackendDevice( Device );
			var errors = new List<string>();
			backend.Surfaces.Error = errors.Add;
			backend.Surfaces.SpecializeFeatures = true;
			backend.Surfaces.SpecializeInBackground = true;
			var held = 1;
			backend.Surfaces.SpecializeHeld = () => System.Threading.Volatile.Read( ref held ) == 1;
			RenderHost.Current = new BackendRenderHost( backend );
			if ( ((GpuDevice)backend).LoadMaterial( "materials/default.vmat" ) is not SurfaceMaterial material )
			{
				Assert.Inconclusive( "default.vmat isn't compiled, or isn't a surface" );
				return;
			}

			// Its program compiled first, so the draw asks for the specialised pipeline
			Draw( backend, material );
			Assert.IsTrue( backend.Surfaces.WaitForCompiles( TimeSpan.FromSeconds( 120 ) ), "compiled" );
			Draw( backend, material );
			System.Threading.Thread.Sleep( 300 );
			var program = material.Program;
			Assert.IsNotNull( program, "a surface program" );
			Assert.AreEqual( 0, program.Specialized.Made, "made while held" );
			Assert.IsTrue( program.Specialized.Building > 0, "a specialised pipeline asked for" );

			System.Threading.Volatile.Write( ref held, 0 );
			var deadline = Environment.TickCount64 + 60_000;
			while ( program.Specialized.Building > 0 && Environment.TickCount64 < deadline ) System.Threading.Thread.Sleep( 10 );
			Assert.IsTrue( program.Specialized.Made > 0, "made once no longer held" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A water_simple material loads as its surface (water_simple has no .shader: the surface is the whole shader), its
	/// fields from the material's values whatever vfx prefix they carry (<c>g_fRoughness</c> for <c>Roughness</c>).
	/// </summary>
	[TestMethod]
	public void SurfaceAloneLoadsItsMaterialsValues()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		if ( ((GpuDevice)backend).LoadMaterial( "materials/water/water_canal.vmat" ) is not SurfaceMaterial material )
		{
			Assert.Inconclusive( "water_canal.vmat isn't compiled, or isn't a surface" );
			return;
		}

		StringAssert.EndsWith( material.Shader, "water_simple.slang" );
		var compiled = backend.Surfaces.Layout( material.Shader );
		var record = new byte[material.RecordSize];
		material.WriteRecord( record, ( _, _ ) => 0 );
		float Value( string field ) => BitConverter.ToSingle( record, compiled.Material.Field( field ).Offset );
		Assert.AreEqual( 0.014f, Value( "Roughness" ), 1e-4f, "g_fRoughness" );
		Assert.AreEqual( 0.764f, Value( "Metalness" ), 1e-4f, "g_fMetalness" );
		Assert.AreEqual( 1f, Value( "AmbientOcclusion" ), 1e-4f, "g_fAmbientOcclusion" );
		Assert.AreEqual( 0.13f, Value( "NormalScale" ), 1e-4f, "NormalScale" );
		Assert.IsNull( material.Fallback, "no .shader to fall back to" );

		// Material.FromShader of a surface alone is the surface with its defaults (the terrain material preview's)
		var preview = backend.MaterialFromShader( "shaders/terrain_preview.shader" ) as SurfaceMaterial;
		Assert.IsNotNull( preview, "terrain_preview from its shader" );
		StringAssert.EndsWith( preview.Shader, "terrain_preview.slang" );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
	}

	/// <summary>
	/// Surfaces bind the frame's view set themselves: with enough draws, layers record on command lists of their own, a
	/// surface is the first draw of its list, and without the view set it would light and fog as no view does. They draw
	/// what the same frame recorded on one list draws.
	/// </summary>
	[TestMethod]
	public void SurfacesBindTheViewSetOnTheirOwnLists()
	{
		Compiler();
		var previousHost = RenderHost.Current;
		try
		{
			// The reference records on one list, where the frame's first draws bind the view set for the surface
			byte[] DrawMaterial( string path, bool parallel, out GpuMaterial loaded )
			{
				// Drawn the classic way, so opaque draws record on the worker segments' lists
				using var backend = new BackendDevice( Device ) { GpuDriven = false };
				RenderHost.Current = new BackendRenderHost( backend );
				loaded = ((GpuDevice)backend).LoadMaterial( path );
				return Draw( backend, loaded, boxes: 144, parallel: parallel );
			}

			foreach ( var path in new[] { "materials/dev/primary_white_trans.vmat", "materials/dev/gray_25.vmat" } )
			{
				if ( ContentFiles.FindGame().Find( path ) is null ) Assert.Inconclusive( $"{path} isn't compiled" );
				var reference = DrawMaterial( path, false, out var serial );
				var converted = DrawMaterial( path, true, out var surface );
				Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{path} isn't a surface by default" );
				var name = Path.GetFileNameWithoutExtension( path );
				SaveRgb( reference, $"viewset-{name}-one-list" );
				SaveRgb( converted, $"viewset-{name}-own-lists" );
				Assert.IsTrue( Mean( reference, converted ) < 0.5, $"{path}: on its own lists the surface differs by {Mean( reference, converted ):F3}" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A surface's render state: the struct's attributes are every material's defaults, [RenderState] fields change them
	/// per material (in field order), the generated class carries both, and a material resolves its state from its record.
	/// </summary>
	[TestMethod]
	public void SurfacesDeclareTheirRenderState()
	{
		var folder = Fixtures.Folder( "surface-state" );
		try
		{
			var path = Path.Combine( folder, "decal_state.slang" );
			File.WriteAllText( path, "import sbox.surface;\n[Material, NoDepthWrite, DepthBias( 250, 0.5 )]\npublic struct DecalState : ISurface\n{\n"
				+ "\t[RenderState( \"Blend\" )] int Blending;\n"
				+ "\t[Source( \"F_TRANSLUCENT\" ), RenderState( \"Blend: Alpha\" )] bool Translucent;\n"
				+ "\t[RenderState( \"Blend: Opaque, Alpha, Opaque, Mod2x; AlphaTest: false, false, true\" )] int Mode;\n"
				+ "\t[RenderState( \"TwoSided\" )] bool Sides;\n"
				+ "\tpublic Material Evaluate( SurfaceInput i ) { return Material.Init( i ); }\n}\n" );
			var compiled = Compiler().CompileSurface( path );
			var layout = compiled.Material;
			Assert.AreEqual( new SurfaceRenderDefaults( SurfaceBlend.Opaque, false, true, 250, 0.5f, false, false ), layout.RenderState );
			Assert.AreEqual( 5, layout.States().Fields.Count );

			SurfaceRenderState Resolve( int translucent, int mode, int sides, int blending )
			{
				var record = new byte[layout.Size];
				BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "Translucent" ).Offset ), translucent );
				BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "Mode" ).Offset ), mode );
				BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "Sides" ).Offset ), sides );
				BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "Blending" ).Offset ), blending );
				return layout.States().Resolve( record );
			}

			Assert.AreEqual( new SurfaceRenderState( SurfaceBlend.Opaque, false, true, 250, 0.5f, false, false ), Resolve( 0, 0, 0, 0 ) );
			Assert.AreEqual( SurfaceBlend.Alpha, Resolve( 1, 9, 0, 0 ).Blend );
			Assert.AreEqual( SurfaceBlend.Opaque, Resolve( 1, 0, 0, 0 ).Blend, "later fields win, a by-value list in every value it lists" );
			Assert.AreEqual( SurfaceBlend.Mod2x, Resolve( 1, 3, 0, 0 ).Blend );
			Assert.IsTrue( Resolve( 0, 2, 0, 0 ) is { Blend: SurfaceBlend.Opaque, AlphaTest: true } );
			Assert.IsTrue( Resolve( 0, 9, 1, 0 ) is { Blend: SurfaceBlend.Opaque, TwoSided: true }, "values past the list leave the state" );
			Assert.AreEqual( SurfaceBlend.Additive, Resolve( 0, 9, 0, (int)SurfaceBlend.Additive ).Blend );
			Assert.AreEqual( SurfaceBlend.Alpha, Resolve( 1, 9, 0, (int)SurfaceBlend.Additive ).Blend, "fields apply in order" );

			// The generated class carries the state and types a blend-mode field as one
			var source = MaterialClassWriter.Write( compiled, "Test" );
			StringAssert.Contains( source, "RenderStates = new( new global::Sandbox.ShaderCompiler.SurfaceRenderDefaults( global::Sandbox.ShaderCompiler.SurfaceBlend.Opaque, false, true, 250.0f, 0.5f, false, false )" );
			StringAssert.Contains( source, ", RenderStates )" );
			StringAssert.Contains( source, "public global::Sandbox.ShaderCompiler.SurfaceBlend Blending {" );

			// A material resolves its state from its record, and a blended one is translucent
			using var backend = new BackendDevice( Device );
			var material = (SurfaceMaterial)backend.Surfaces.CreateMaterial( path, null, new Dictionary<string, float[]> { ["F_TRANSLUCENT"] = [1], ["Mode"] = [9] } );
			Assert.IsTrue( material.RenderState is { Blend: SurfaceBlend.Alpha, DepthWrite: false } );
			Assert.IsTrue( material.IsTranslucent );

			// A misspelt state is a compile error
			File.WriteAllText( path, File.ReadAllText( path ).Replace( "Blend: Alpha\"", "Blend: Alpah\"" ) );
			Assert.ThrowsException<ShaderCompileException>( () => Compiler().CompileSurface( path ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// glass.slang: every glass material draws in the translucent layer whatever its quality, as glass.shader defines
	/// S_TRANSLUCENT itself: refractive glass unblended, simple glass blended by opacity, layered glass premultiplied, all
	/// writing depth as glass.shader does; its depth and shadows dithered by an opacity of its own (IShadowSurface). Its
	/// forward pass reads the frame-buffer copy, the depth chain and the BRDF lookup by attribute; a surface that doesn't
	/// read the view inputs (complex) doesn't bind the copy.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void GlassDrawsTranslucentFromTheFrameBufferCopy()
	{
		var glass = Compiler().CompileSurface( Path.Combine( Core, "shaders", "glass.slang" ) );
		var layout = glass.Material;
		Assert.AreEqual( true, layout.RenderState.DepthWrite );

		SurfaceRenderState Resolve( int quality )
		{
			var record = new byte[layout.Size];
			BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "Quality" ).Offset ), quality );
			return layout.States().Resolve( record );
		}

		Assert.IsTrue( Resolve( 0 ) is { Blend: SurfaceBlend.Opaque, Translucent: true, DepthWrite: true, DrawsTranslucent: true } );
		Assert.IsTrue( Resolve( 1 ) is { Blend: SurfaceBlend.Alpha, DepthWrite: true, DrawsTranslucent: true } );
		Assert.IsTrue( Resolve( 2 ) is { Blend: SurfaceBlend.Premultiplied, DrawsTranslucent: true } );

		var forward = glass.Pass( ShaderPasses.Forward + "Translucent" ).Pixel;
		foreach ( var name in new[] { "FrameBufferCopyTexture", "DepthChainDownsample", "BRDFLookup" } )
			Assert.IsTrue( forward.Bindings.Any( b => b.Name == name ), name );
		Assert.IsNotNull( glass.Pass( ShaderPasses.Forward + "Translucent" + ShaderPasses.GpuDriven ), glass.GpuDrivenError );
		foreach ( var suffix in new[] { "", ShaderPasses.Lightmapped, ShaderPasses.Skinned } )
			Assert.IsTrue( glass.Pass( ShaderPasses.Depth + "Translucent" + suffix ) is { Pixel: { } dithered } && dithered.Bindings.Any( b => b.Name == "BRDFLookup" ), suffix );

		var complex = Compiler().CompileSurface( Path.Combine( Core, "shaders", "standard.slang" ) ).Pass( ShaderPasses.Forward ).Pixel;
		Assert.IsFalse( complex.Bindings.Any( b => b.Name == "FrameBufferCopyTexture" ), string.Join( ", ", complex.Bindings.Select( b => b.Name ) ) );

		// A glass material is translucent without F_TRANSLUCENT
		using var backend = new BackendDevice( Device );
		var material = (SurfaceMaterial)backend.Surfaces.CreateMaterial( Path.Combine( Core, "shaders", "glass.slang" ), null, new Dictionary<string, float[]>() );
		Assert.IsTrue( material.IsTranslucent );
		Assert.IsTrue( material.RenderState is { Blend: SurfaceBlend.Opaque, Translucent: true } );
	}

	/// <summary>
	/// A mode a shader leaves to another (<c>Depth( "depth_only.shader" )</c>, as a package shader with only VrForward has)
	/// draws with the material's features carried over by name, as native's: an alpha-tested material's depth is
	/// depth_only's alpha-tested variant, whose pixel shader cuts the depth where the colour pass clips (Jumper's plant
	/// leaves left their quads' depth in the prepass without it); one that doesn't alpha test has no cut.
	/// </summary>
	[TestMethod]
	public void ModeFallbacksTakeTheMaterialsFeatures()
	{
		using var backend = new BackendDevice( Device );
		var library = backend.Shaders;
		Assert.IsNotNull( library.Load( "shaders/depth_only.shader" ), "no depth_only.shader" );

		var leaves = ShaderFile.FromModule( "leaves",
			[new ShaderMode { Name = "VrForward", Fallback = "", Settings = [] }, new ShaderMode { Name = "Depth", Fallback = "depth_only.shader", Settings = [] }],
			[new ShaderCombo { Name = "F_TEXTURE_FILTERING", Min = 0, Max = 4, FeatureIndex = 0, Strings = [] }, new ShaderCombo { Name = "F_ALPHA_TEST", Min = 0, Max = 1, FeatureIndex = 1, Strings = [] }],
			[] );

		ShaderVariant DepthVariant( int alphaTest )
		{
			var until = DateTime.UtcNow + TimeSpan.FromSeconds( 120 );
			while ( true )
			{
				var variant = library.Variant( leaves, "Depth", [0, alphaTest], null, null, false, out var pending );
				if ( !pending || DateTime.UtcNow > until ) return variant;
				System.Threading.Thread.Sleep( 100 );
			}
		}

		var cut = DepthVariant( 1 );
		Assert.IsNotNull( cut, "no depth variant" );
		Assert.IsTrue( cut.Pixel?.Reflection is { Discards: true }, "an alpha-tested material's fallback depth doesn't cut" );
		Assert.IsFalse( DepthVariant( 0 )?.Pixel?.Reflection is { Discards: true }, "an opaque material's fallback depth cuts" );
	}

	/// <summary>
	/// A translucency feature a material's shader declares but never reads doesn't make it blend, as on native: blendable.shader
	/// had common/features.hlsl's <c>F_ADDITIVE_BLEND</c> and no combo following it, so a blendable material that kept the
	/// value from another shader (Unicycle Frenzy's start pad trim) was opaque there, and package shaders still declare it so.
	/// Such a material is opaque and converts to a surface that doesn't read the feature; one whose shader's static combo
	/// follows the feature (<c>S_ADDITIVE_BLEND</c>) still blends.
	/// </summary>
	[TestMethod]
	public void TranslucencyFeaturesTheShaderDoesntReadDontBlend()
	{
		static MaterialFile MaterialOf( string shader, Dictionary<string, int> ints ) => new()
		{
			Name = "trim",
			Shader = shader,
			Ints = ints,
			Floats = new Dictionary<string, float>(),
			Vectors = new Dictionary<string, float[]>(),
			Textures = new Dictionary<string, string>(),
			IntAttributes = new Dictionary<string, int>(),
		};

		// F_MULTIBLEND picks code; F_ADDITIVE_BLEND only where the shader has S_ADDITIVE_BLEND
		static ShaderFile Described( bool readsAdditive )
		{
			ShaderCombo[] features = [new() { Name = "F_MULTIBLEND", Min = 0, Max = 3, FeatureIndex = 0, Strings = [] }, new() { Name = "F_ADDITIVE_BLEND", Min = 0, Max = 1, FeatureIndex = 1, Strings = [] }];
			var statics = new List<ShaderCombo> { new() { Name = "S_MULTIBLEND", Min = 0, Max = 3, FeatureIndex = 0, Strings = [] } };
			if ( readsAdditive ) statics.Add( new() { Name = "S_ADDITIVE_BLEND", Min = 0, Max = 1, FeatureIndex = 1, Strings = [] } );
			return ShaderFile.FromModule( readsAdditive ? "reads" : "declares", [new ShaderMode { Name = "Forward", Fallback = "", Settings = [] }], features,
				[(ShaderProgramType.Vertex, [], []), (ShaderProgramType.Pixel, [.. statics], [])] );
		}

		var declares = Described( false );
		var reads = Described( true );
		Assert.IsTrue( declares.FeatureIndex( "F_ADDITIVE_BLEND" ) >= 0 );
		Assert.IsFalse( declares.FeatureSelectsCode( declares.FeatureIndex( "F_ADDITIVE_BLEND" ) ) );
		Assert.IsTrue( declares.FeatureSelectsCode( declares.FeatureIndex( "F_MULTIBLEND" ) ) );
		Assert.IsTrue( reads.FeatureSelectsCode( reads.FeatureIndex( "F_ADDITIVE_BLEND" ) ) );

		var trim = MaterialOf( "shaders/blendable.shader", new() { ["F_MULTIBLEND"] = 1, ["F_ADDITIVE_BLEND"] = 1 } );
		using var trimMaterial = new BackendMaterial( "trim", declares, trim, null );
		Assert.IsFalse( trimMaterial.IsTranslucent, "a material blends by a feature its shader doesn't read" );
		var surface = Compiler().CompileSurface( Path.Combine( Core, "shaders", "blendable.slang" ) );
		Assert.IsTrue( ConvertedMaterial.Accepts( surface, trim, declares ), "blendable.slang doesn't take the material" );

		using var additive = new BackendMaterial( "additive", reads, trim, null );
		Assert.IsTrue( additive.IsTranslucent, "a material whose shader reads F_ADDITIVE_BLEND is opaque" );
	}

	/// <summary>
	/// A glass material draws translucent, so the glass of an object that is partly opaque casts no shadow, as on native: a
	/// slab with glass top and bottom and opaque sides, out of the game layers (it only casts), leaves the floor under it
	/// lit by the sun, where an opaque slab shadows it.
	/// </summary>
	[TestMethod]
	public void GlassInAnOpaqueObjectCastsNoShadow()
	{
		Compiler();
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var grey = backend.CreateMaterial( "Glass shadow grey", Color.Gray );
			var glass = backend.Surfaces.CreateMaterial( Path.Combine( Core, "shaders", "glass.slang" ), null, new Dictionary<string, float[]>() );
			Assert.IsTrue( glass.IsTranslucent, "a glass material without F_TRANSLUCENT is translucent" );

			var none = FloorUnder( backend, grey, null );
			var opaque = FloorUnder( backend, grey, grey );
			var glassy = FloorUnder( backend, grey, glass );
			TestContext.WriteLine( $"floor under the slab: none {none:F3}, opaque {opaque:F3}, glass {glassy:F3}" );
			Assert.IsTrue( opaque < none * 0.5f, $"an opaque slab shadows the floor ({opaque:F3} against {none:F3})" );
			Assert.IsTrue( glassy > none * 0.9f, $"the slab's glass casts no shadow ({glassy:F3} against {none:F3})" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Glass's own shadow (IShadowSurface): an object all of glass casts a shadow dithered by glass.shader's depth-mode
	/// opacity, so the floor under it is darker than in the open and lighter than under an opaque slab.
	/// </summary>
	[TestMethod]
	public void GlassCastsADitheredShadow()
	{
		Compiler();
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var grey = backend.CreateMaterial( "Glass shadow grey", Color.Gray );
			var glass = backend.Surfaces.CreateMaterial( Path.Combine( Core, "shaders", "glass.slang" ), null, new Dictionary<string, float[]>() );

			var none = FloorUnder( backend, grey, null );
			var opaque = FloorUnder( backend, grey, grey );
			var glassy = FloorUnder( backend, glass, glass, grey );
			TestContext.WriteLine( $"floor under the slab: none {none:F3}, opaque {opaque:F3}, all glass {glassy:F3}" );
			Assert.IsTrue( glassy < none * 0.9f, $"the glass casts a shadow ({glassy:F3} against {none:F3})" );
			Assert.IsTrue( glassy > opaque * 1.2f, $"the glass's shadow is lighter than an opaque one ({glassy:F3} against {opaque:F3})" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The sun-lit floor's mean brightness at the frame's centre, under a slab whose top and bottom are
	/// <paramref name="faces"/> and sides <paramref name="sides"/> (or none, when <paramref name="faces"/> is null); the floor
	/// is <paramref name="floor"/>, or the sides' material.
	/// </summary>
	float FloorUnder( BackendDevice backend, GpuMaterial sides, GpuMaterial faces, GpuMaterial floor = null )
	{
		const int Size = 128;
		using var system = new RenderSystem { ParallelRecording = false };
		using var target = ViewTarget.Create( new Vector2Int( Size, Size ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Glass shadow test" );
		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( 0.1f, 0.05f, -1 ).Normal;
		world.Lighting.SunColor = new Color( 4, 4, 4 );
		world.Lighting.AmbientColor = new Color( 0.05f, 0.05f, 0.05f, 1 );
		world.Lighting.SunShadowCascades = 1;
		world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, floor ?? sides ), new Transform( new Vector3( 0, 0, -52.5f ), Rotation.Identity, new Vector3( 20, 20, 0.05f ) ) ) );

		if ( faces is not null )
		{
			// BoxMesh's faces: four sides, then top and bottom
			var box = ObjectAttributeTests.BoxMesh( backend, sides );
			var slab = new RenderMesh( box.Geometry, box.Bounds, [[
				new RenderMesh.Draw( 0, 0, 0, 24, 24, 0, sides, Translucent: sides.IsTranslucent ),
				new RenderMesh.Draw( 0, 1, 24, 12, 24, 0, faces, Translucent: faces.IsTranslucent ),
			]] );
			world.Add( new MeshObject( slab, new Transform( new Vector3( 0, 0, 100 ), Rotation.Identity, new Vector3( 3, 3, 0.1f ) ) ) { GameLayers = false } );
		}

		var view = new RenderView { Name = "Glass shadow test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Size, Size ) };
		view.Rotation = Rotation.From( 60, 90, 0 );
		view.Position = new Vector3( 0, 0, -50 ) - view.Rotation.Forward * 450;

		for ( int frame = 0; frame < 2; frame++ )
		{
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		Device.WaitIdle();
		var (texture, layout) = BackendDevice.Unwrap( target.Color );
		var halfs = ReadBack( texture, layout );
		BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
		var rgba = new byte[Size * Size * 4];
		for ( int i = 0; i < rgba.Length; i++ ) rgba[i] = (byte)Math.Clamp( (float)BitConverter.ToHalf( halfs, i * 2 ) * 255, 0, 255 );
		Save( rgba, Size, Size, $"glass-shadow-{(faces is null ? "none" : sides.IsTranslucent ? "all-glass" : faces.IsTranslucent ? "glass" : "opaque")}" );

		float sum = 0;
		var count = 0;
		for ( int y = Size / 2 - 16; y < Size / 2 + 16; y++ )
		{
			for ( int x = Size / 2 - 16; x < Size / 2 + 16; x++ )
			{
				sum += (float)BitConverter.ToHalf( halfs, (y * Size + x) * 8 );
				count++;
			}
		}

		return sum / count;
	}

	/// <summary>
	/// The converter's render state: a .shader's RenderState lines evaluated per feature value become the struct's
	/// attributes and rules on the features' fields, as static_overlay.slang and standard.slang declare them by hand.
	/// </summary>
	[TestMethod]
	public void ConverterTranslatesRenderStates()
	{
		if ( Core is null ) Assert.Inconclusive( "No game content" );
		static ShaderConverter.RenderStates Translate( string name )
		{
			// static_overlay.shader and generic.shader are gone (surfaces stand in alone): the test keeps copies
			var data = Path.Combine( AppContext.BaseDirectory, "Data", name + ".shader" );
			var text = File.ReadAllText( File.Exists( data ) ? data : Path.Combine( Core, "shaders", name + ".shader" ) );
			var combos = System.Text.RegularExpressions.Regex.Matches( text, @"StaticCombo\(\s*(S_\w+)\s*,\s*(F_\w+)" ).DistinctBy( m => m.Groups[1].Value ).ToDictionary( m => m.Groups[1].Value, m => m.Groups[2].Value );
			var features = System.Text.RegularExpressions.Regex.Matches( text, @"Feature\(\s*(F_\w+)\s*,\s*(\d+)\s*\.\.\s*(\d+)" ).ToDictionary( m => m.Groups[1].Value, m => (int.Parse( m.Groups[2].Value ), int.Parse( m.Groups[3].Value )) );
			features.TryAdd( "F_RENDER_BACKFACES", (0, 1) );
			return ShaderConverter.TranslateRenderState( text, combos, features );
		}

		var overlay = Translate( "static_overlay" );
		var overlayRules = string.Join( "\n", overlay.Rules.Select( r => $"{r.Feature}: {r.Rule}" ) );
		Assert.AreEqual( "NoDepthWrite, DepthBias( 250, 0.5 )", overlay.Attributes, overlayRules );
		Assert.AreEqual( "F_BLEND_MODE: Blend: Opaque, Alpha, Opaque, Mod2x, Additive, Multiply, ModulateAdd; AlphaTest: false, false, true, false, false, false, false", overlayRules, string.Join( "\n", overlay.Notes ) );

		var generic = Translate( "generic" );
		var genericRules = string.Join( "\n", generic.Rules.Select( r => $"{r.Feature}: {r.Rule}" ) );
		Assert.AreEqual( "", generic.Attributes, genericRules );
		Assert.AreEqual( "F_TRANSLUCENT: Blend: Alpha", genericRules, string.Join( "\n", generic.Notes ) );

		// Additive blending needs translucency on as well in generic.shader: a combination its own rule can't give alone
		Assert.IsTrue( generic.Notes.Any( n => n.Contains( "F_ADDITIVE_BLEND", StringComparison.Ordinal ) ), string.Join( "\n", generic.Notes ) );
	}

	/// <summary>
	/// The surface material records grow while a frame records (more materials than the first buffer holds, drawn at
	/// once, the classic way: GPU-driven draws read their own table and write no records): draws recorded before the growth
	/// keep the buffer they bound until the GPU is done with it, so validation stays clean.
	/// </summary>
	[TestMethod]
	public void SurfaceRecordsGrowWhileDrawing()
	{
		Compiler();
		using var backend = new BackendDevice( Device ) { GpuDriven = false };
		backend.Surfaces.AddFolder( Surfaces );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var materials = new List<Rock>();
		try
		{
			// 64 KiB holds 2048 of Rock's 32-byte records. One box drawn once per rock (a box mesh each took most of the test's
			// time), in a grid of 48 by 48
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.5f, -0.75f ).Normal;
			for ( int i = 0; i < 2304; i++ ) materials.Add( new Rock { Tint = new Vector3( (i % 48) / 48f, (i / 48) / 48f, 0.5f ) } );
			using var first = new Rock();
			var box = ObjectAttributeTests.BoxMesh( backend, first );
			var draw = box.DrawsForLod( 0 )[0];
			world.Add( new MeshObject( new RenderMesh( box.Geometry, box.Bounds, [[.. materials.Select( m => draw with { Material = m } )]], [] ), new Transform( Vector3.Zero, Rotation.Identity, 2 ) ) );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface records test" );
			var view = new RenderView { Name = "Surface records test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -150, 0, 700 );
			view.Rotation = Rotation.LookAt( -view.Position );
			for ( int frame = 0; frame < 3; frame++ )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
			}

			Device.WaitIdle();
			Assert.IsTrue( backend.Surfaces.Records.Grows > 0 && backend.Surfaces.Records.Use.Size > 64 << 10, $"the records grew past the first buffer: {backend.Surfaces.Records.Use}" );
		}
		finally
		{
			foreach ( var material in materials ) material.Dispose();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A closed map's materials' records go: once most of the records buffer holds records no draw has used for a while, the
	/// records start again in a buffer sized for those still drawn, the materials behind the rest are let go, and the
	/// buffers they moved out of are destroyed. Before, the buffer and every one it grew out of stayed, a map's worth more
	/// each session.
	/// </summary>
	[TestMethod]
	public void UnusedSurfaceRecordsGo()
	{
		using var records = new SurfaceRecords( Device );
		using var kept = new Rock();
		var map = new List<Rock>();
		try
		{
			for ( int i = 0; i < 12000; i++ )
			{
				var rock = new Rock { Tint = new Vector3( i / 12000f, 0.5f, 0.5f ) };
				map.Add( rock );
				records.Offset( rock );
			}

			records.Offset( kept );
			var grown = records.Use.Size;
			Assert.IsTrue( grown >= 256 * 1024, $"{grown} bytes" );
			Assert.IsTrue( records.Retired > 0 );

			for ( int frame = 0; frame < SurfaceRecords.IdleFrames + 120; frame++ )
			{
				records.BeginFrame();
				records.Offset( kept );
			}

			Assert.AreEqual( 1, records.Restarts );
			Assert.AreEqual( 1, records.Materials, "only the material still drawn has a record" );
			Assert.AreEqual( 64 * 1024, records.Use.Size );
			Assert.AreEqual( 0, records.Retired, "the old buffers are destroyed" );
			Assert.AreEqual( 0u, records.Offset( kept ), "the kept material's record was written again at the start" );
		}
		finally
		{
			foreach ( var rock in map ) rock.Dispose();
		}
	}

	/// <summary>
	/// A draw whose material's record grows the records buffer binds the grown buffer, though earlier draws of the same pass
	/// state bound the old one. Bound as the pass state first resolved it, the draws after the growth read past the old
	/// buffer's end (a GPU page fault where it ended its memory block, a wrong material otherwise).
	/// </summary>
	[TestMethod]
	public void DrawsAfterRecordsGrowReadTheirOwnRecords()
	{
		Compiler();
		using var backend = new BackendDevice( Device ) { GpuDriven = false };
		backend.Surfaces.AddFolder( Surfaces );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var materials = new List<Rock>();
		try
		{
			// Small boxes in a grid, a rock material each, drawn the classic way (GPU-driven draws read their own table): their
			// draws share a pass state (no attributes of their own)
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.5f, -0.75f ).Normal;
			world.Lighting.SunShadowCascades = 0;
			// One box drawn once per rock, all one object's draws. No depth prepass: its draws would write the records first, and
			// it binds none
			using var first = new Rock();
			var box = ObjectAttributeTests.BoxMesh( backend, first );
			var draw = box.DrawsForLod( 0 )[0];
			MeshObject boxes = null;
			void AddRocks( int from, int to )
			{
				for ( int i = from; i < to; i++ ) materials.Add( new Rock { Tint = new Vector3( (i % 7) / 7f, (i % 5) / 5f, 0.5f ) } );
				if ( boxes is not null ) world.Remove( boxes );
				boxes = new MeshObject( new RenderMesh( box.Geometry, box.Bounds, [[.. materials.Select( m => draw with { Material = m } )]], [] ), new Transform( Vector3.Zero, Rotation.Identity, 2 ) );
				world.Add( boxes );
			}

			using var system = new RenderSystem { ParallelRecording = false, DepthPrepass = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface records growth" );
			var view = new RenderView { Name = "Surface records growth", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -220, 60, 90 );
			view.Rotation = Rotation.LookAt( -view.Position );
			void Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
			}

			// 2000 settle, then 4000 more grow the records buffer while a frame draws them
			AddRocks( 0, 2000 );
			for ( int frame = 0; frame < 3; frame++ ) Frame();
			Assert.IsNotNull( backend.Surfaces.For( materials[0] ), $"rock draws as a surface: {string.Join( "; ", errors )}" );
			var grows = backend.Surfaces.Records.Grows;
			var stale = backend.StaleRecordBindings;
			AddRocks( 2000, 6000 );
			Frame();
			Device.WaitIdle();
			Assert.IsTrue( backend.Surfaces.Records.Grows > grows, $"the records grew while the frame drew ({grows} before)" );
			Assert.AreEqual( stale, backend.StaleRecordBindings, "every draw bound the buffer its record is in" );
		}
		finally
		{
			Device.WaitIdle();
			foreach ( var material in materials ) material.Dispose();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// Surfaces draw skinned meshes: the citizen, whose many-weight meshes skin through the vertex cache and the rest in the
	/// vertex stage, drawn with a surface as the same material written as a .shader draws it (the surface has no .shader
	/// to fall back to, so a skinned draw it can't make is missing from the picture).
	/// </summary>
	[TestMethod]
	public void SurfacesDrawSkinnedMeshes()
	{
		Compiler();
		using var backend = new BackendDevice( Device, Fixtures.WithCompiledShaders() ) { TimeDraws = true };
		backend.Surfaces.AddFolder( Surfaces );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var citizen = backend.LoadModel( "models/citizen/citizen.vmdl" );
			if ( citizen is null ) Assert.Inconclusive( "models/citizen/citizen.vmdl isn't built" );
			Assert.IsTrue( Enumerable.Range( 0, citizen.ModelMeshCount ).Any( m => citizen.SkinFor( m ) is { UsesVertexCache: true } ), "the citizen skins through the vertex cache" );

			var shader = (BackendMaterial)((GpuDevice)backend).CreateMaterial( "Rock (.shader)", "shaders/surface_test_rock.shader" );
			if ( shader.Shader?.Name.Contains( "surface_test_rock", StringComparison.OrdinalIgnoreCase ) != true ) Assert.Inconclusive( "surface_test_rock.shader isn't compiled" );

			var color = backend.LoadTexture( Fixtures.Rope.Color );
			var normal = backend.LoadTexture( Fixtures.Rope.Normal );
			var rma = backend.LoadTexture( Fixtures.Rope.Rma );
			shader.SetTexture( "g_tColor", (BackendTexture)color );
			shader.SetTexture( "g_tNormal", (BackendTexture)normal );
			shader.SetTexture( "g_tRma", (BackendTexture)rma );
			shader.Set( "g_vTint", [1, 0.8f, 0.6f] );
			using var surface = new Rock { Color = color, Normal = normal, Rma = rma, Tint = new Vector3( 1, 0.8f, 0.6f ) };

			var reference = Draw( backend, shader, model: citizen );
			var fromSurface = Draw( backend, surface, model: citizen );
			SaveRgb( reference, "surface-skinned-shader" );
			SaveRgb( fromSurface, "surface-skinned-surface" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
			Assert.AreEqual( 0, backend.DrawTimes.Failed.Count, "Draws failed: " + string.Join( ", ", backend.DrawTimes.Failed.Select( f => $"{f.Key} x{f.Value}" ) ) );
			Assert.IsTrue( Lit( reference ) > Width * Height / 10, "The .shader drew too little to compare" );
			Assert.IsTrue( Mean( reference, fromSurface ) < 0.5, $"the skinned surface differs by {Mean( reference, fromSurface ):F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// A box lit by the sun, filling most of the frame, as 8-bit sRGB-ish bytes (red channel of RGBA16F); or a model in its
	/// bind pose with every draw in the material.
	/// </summary>
	byte[] Draw( BackendDevice backend, GpuMaterial material, Color? tint = null, int boxes = 1, RenderMesh model = null, ShaderParameters attributes = null, bool? parallel = null )
	{
		using var system = new RenderSystem { ParallelRecording = parallel ?? boxes > 1 };
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface test" );
		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( -0.4f, 0.5f, -0.75f ).Normal;
		world.Lighting.SunShadowCascades = 1;
		if ( model is not null ) world.Add( new MeshObject( model, new Transform( model.Bounds.Center * -(160 / model.Bounds.Size.Length), Rotation.Identity, 160 / model.Bounds.Size.Length ) ) { MaterialOverride = material, Attributes = attributes } );
		else if ( boxes == 1 ) world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), new Transform( Vector3.Zero, Rotation.FromYaw( 35 ), 2 ) ) { Tint = tint ?? Color.White } );

		// Or a grid of small ones apart, enough draws that each layer records on lists of its own
		var side = (int)MathF.Ceiling( MathF.Sqrt( boxes ) );
		for ( int i = 0; boxes > 1 && i < boxes; i++ )
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), new Transform( new Vector3( 0, (i % side - side / 2) * 18, (i / side - side / 2) * 18 ), Rotation.FromYaw( 35 ), 0.1f ) ) { Tint = tint ?? Color.White } );

		var view = new RenderView { Name = "Surface test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.2f, 0.2f, 0.2f ), Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -220, 60, 90 );
		view.Rotation = Rotation.LookAt( -view.Position );

		for ( int frame = 0; frame < 3; frame++ )
		{
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );

			// The pictures compared are the optimised links': fast-linked ones may draw some surfaces differently until then
			if ( frame == 0 ) Assert.IsTrue( Device.WaitForOptimisedPipelines( TimeSpan.FromSeconds( 60 ) ), "the optimised pipeline links finish" );
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

	/// <summary>Pixels neither black nor the clear colour (54 in these bytes): what a model drew lit.</summary>
	static int Bright( byte[] rgb )
	{
		var bright = 0;
		for ( int i = 0; i < rgb.Length; i += 3 )
		{
			if ( rgb[i] + rgb[i + 1] + rgb[i + 2] > 30 && Math.Abs( rgb[i] - 54 ) + Math.Abs( rgb[i + 1] - 54 ) + Math.Abs( rgb[i + 2] - 54 ) > 6 ) bright++;
		}

		return bright;
	}

	static int Lit( byte[] rgb )
	{
		var lit = 0;
		for ( int i = 0; i < rgb.Length; i += 3 ) if ( Math.Abs( rgb[i] - 124 ) + Math.Abs( rgb[i + 1] - 124 ) + Math.Abs( rgb[i + 2] - 124 ) > 6 ) lit++;
		return lit;
	}
}
