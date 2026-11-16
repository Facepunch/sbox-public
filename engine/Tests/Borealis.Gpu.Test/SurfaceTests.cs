using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Sandbox;
using Sandbox.BorealisLab.Surfaces;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// The Slang material tier: Sandbox.ShaderCompiler's source rewrites and reflection, and new-tier materials drawn on
/// Borealis.Gpu against the same material written as today's .shader. See docs/managed/shader-format-replacement.md.
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
			Assert.IsTrue( first.Dependencies.Any( d => d.EndsWith( "vr_lighting.fxc", StringComparison.OrdinalIgnoreCase ) ), "the core includes are dependencies" );
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
	/// The converter turns pixel.hlsl-style .shaders into surfaces that compile: the template (Material::Init), bark
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
			foreach ( var (relative, name) in new[] { ("templates/material.shader", "material"), ("core/shaders/bark.shader", "bark"), ("core/shaders/water_simple.shader", "water_simple") } )
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
		foreach ( var (shader, file) in new[] { (rock, "rock.g.cs"), (raw, "rock_raw.g.cs"), (lit, "rock_lit.g.cs") } )
		{
			var expected = MaterialClassWriter.Write( shader, "Sandbox.BorealisLab.Surfaces" ).Replace( "\r\n", "\n" );
			var actual = File.ReadAllText( Path.Combine( Surfaces, file ) ).Replace( "\r\n", "\n" );
			Assert.AreEqual( expected, actual, $"{file} is stale: run borealislab -surface-parity <folder> -surface-classes" );
		}
	}

	/// <summary>
	/// Material.CommonInputs.hlsl's TextureFiltering takes its filter from an expression on F_TEXTURE_FILTERING, evaluated
	/// per material: anisotropic by default, and the feature's other values pick bilinear, trilinear and point. Materials
	/// that differ only in it don't share a variant.
	/// </summary>
	[TestMethod]
	public void ExpressionFilteredSamplersFollowTheirFeature()
	{
		using var backend = new BackendDevice( Device );
		List<SamplerInfo> Samplers( int filtering )
		{
			var foliage = (BackendMaterial)((GpuDevice)backend).CreateMaterial( $"Foliage {filtering}", "shaders/foliage.shader" );
			if ( foliage.Shader is null ) Assert.Inconclusive( "foliage.shader isn't compiled" );
			var feature = foliage.Shader.FeatureIndex( "F_TEXTURE_FILTERING" );
			Assert.IsTrue( feature >= 0, "foliage.shader has no F_TEXTURE_FILTERING" );
			foliage.Features[feature] = filtering;
			var variant = foliage.Variant( backend.Shaders, "Forward", "Forward", _ => null, _ => null );
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
	/// Render states given by expressions are evaluated per material too: complex.shader's CullMode follows
	/// F_RENDER_BACKFACES, and materials that differ in it don't share a variant.
	/// </summary>
	[TestMethod]
	public void ExpressionRenderStatesFollowTheirFeature()
	{
		using var backend = new BackendDevice( Device );
		CullMode Cull( int backfaces )
		{
			var complex = (BackendMaterial)((GpuDevice)backend).CreateMaterial( $"Complex {backfaces}", "shaders/complex.shader" );
			if ( complex.Shader is null ) Assert.Inconclusive( "complex.shader isn't compiled" );
			var feature = complex.Shader.FeatureIndex( "F_RENDER_BACKFACES" );
			Assert.IsTrue( feature >= 0, "complex.shader has no F_RENDER_BACKFACES" );
			complex.Features[feature] = backfaces;
			return complex.Variant( backend.Shaders, "Forward", "Forward", _ => null, _ => null ).State.Cull;
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

	/// <summary>
	/// standard.slang stands in for complex.shader's materials and draws them as complex.shader does, through the final
	/// combiner; a material using a feature it doesn't do (translucency) stays on complex.shader.
	/// </summary>
	[TestMethod]
	public void ComplexSurfaceDrawsComplexMaterials()
	{
		Compiler();
		var previousHost = RenderHost.Current;
		try
		{
			byte[] DrawMaterial( string path, bool convert, out GpuMaterial loaded, Color? tint = null )
			{
				using var backend = new BackendDevice( Device );
				var errors = new List<string>();
				backend.Surfaces.Error = errors.Add;
				RenderHost.Current = new BackendRenderHost( backend );

				// The reference: the .shader route, with the core shaders' default surfaces off
				backend.Surfaces.ConvertShaders = false;
				if ( convert )
				{
					backend.Surfaces.AddFolder( Path.Combine( Core, "shaders" ) );
					backend.Surfaces.Convert( "shaders/complex.shader", "standard.slang" );
				}

				loaded = ((GpuDevice)backend).LoadMaterial( path );
				var picture = Draw( backend, loaded, tint );
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				return picture;
			}

			// A fading object (tint alpha under 1) dithers by its opacity, as complex.shader's D_OPAQUE_FADE does
			var faded = new Color( 1, 1, 1, 0.5f );
			var solid = DrawMaterial( "materials/default.vmat", true, out _ );
			var fadedShader = DrawMaterial( "materials/default.vmat", false, out var fadedClassic, faded );
			if ( fadedClassic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( "materials/default.vmat isn't compiled" );
			var fadedSurface = DrawMaterial( "materials/default.vmat", true, out _, faded );
			SaveRgb( fadedShader, "complex-faded-shader" );
			SaveRgb( fadedSurface, "complex-faded-surface" );
			Assert.IsTrue( Mean( solid, fadedSurface ) > 2, "a fading surface drew solid" );
			Assert.IsTrue( Mean( fadedShader, fadedSurface ) < 0.5, $"a fading surface differs by {Mean( fadedShader, fadedSurface ):F3}" );

			foreach ( var path in new[] { "materials/default.vmat", "dev/helper/testgrid.vmat" } )
			{
				var reference = DrawMaterial( path, false, out var classic );
				if ( classic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( $"{path} isn't compiled" );
				var converted = DrawMaterial( path, true, out var surface );
				Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{path} wasn't converted" );

				// Lit as complex.shader is: from a probe volume's baked light when its object isn't lightmapped
				Assert.IsTrue( surface.NeedsLightProbe, "a surface doesn't ask for its object's light probe" );
				SaveRgb( reference, $"complex-{Path.GetFileNameWithoutExtension( path )}-shader" );
				SaveRgb( converted, $"complex-{Path.GetFileNameWithoutExtension( path )}-surface" );
				Assert.IsTrue( Mean( reference, converted ) < 0.5, $"{path}: the surface differs by {Mean( reference, converted ):F3}" );
			}

			// Translucent complex materials: CombinerSurfacesDrawAsTheirShaders
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// The converter turns a .shader of the final combiner shape (simple.shader: the standard processing, lit by the final
	/// combiner) into a combiner surface with its features as fields, which draws simple's materials as simple.shader
	/// does, colour-alpha occlusion included; shaders that do more than that shape stay as they are.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void ConverterTurnsStandardCombinerShadersIntoSurfaces()
	{
		var compiler = Compiler();
		var shaders = Path.Combine( Core, "shaders" );
		string Convert( string name, out IReadOnlyList<string> notes )
		{
			var result = ShaderConverter.Convert( File.ReadAllText( Path.Combine( shaders, name + ".shader" ) ), name );
			notes = result.Notes;
			return result.Source;
		}

		// Shaders that do more than the standard processing: their notes say why
		Assert.IsNull( Convert( "vertex_color", out var notes ), "vertex_color converted" );
		StringAssert.Contains( notes[0], "vertex shader" );
		Assert.IsNull( Convert( "static_overlay", out notes ), "static_overlay converted" );
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

			byte[] DrawMaterial( string path, bool convert, out GpuMaterial loaded )
			{
				using var backend = new BackendDevice( Device );
				var errors = new List<string>();
				backend.Surfaces.Error = errors.Add;
				RenderHost.Current = new BackendRenderHost( backend );

				// The reference: the .shader route, with the core shaders' default surfaces off
				backend.Surfaces.ConvertShaders = false;
				if ( convert )
				{
					backend.Surfaces.AddFolder( folder );
					backend.Surfaces.Convert( "shaders/simple.shader", "simple.slang" );
				}

				loaded = ((GpuDevice)backend).LoadMaterial( path );
				var picture = Draw( backend, loaded );
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				return picture;
			}

			// A plain one, colour-alpha occlusion (F_AMBIENT_OCCLUSION_TEXTURE) and a metal
			foreach ( var path in new[] { "materials/dev/simple/floor_simple_128.vmat", "materials/black_matte.vmat", "materials/editor/env_cubemap_model.vmat" } )
			{
				var reference = DrawMaterial( path, false, out var classic );
				if ( classic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( $"{path} isn't compiled" );
				var converted = DrawMaterial( path, true, out var surface );
				Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{path} wasn't converted" );
				SaveRgb( reference, $"simple-{Path.GetFileNameWithoutExtension( path )}-shader" );
				SaveRgb( converted, $"simple-{Path.GetFileNameWithoutExtension( path )}-surface" );
				Assert.IsTrue( Mean( reference, converted ) < 0.5, $"{path}: the surface differs by {Mean( reference, converted ):F3}" );
			}

			// By default the core shaders' materials load as their surfaces: complex's written by hand, simple's the converter's
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
	/// The final combiner family's other surfaces, on by default: standard.slang with generic's materials (plain, specular on, self-illuminated,
	/// two-sided, unlit, alpha-tested, translucent) and vertex_color.slang (the mesh's colour as Varyings) draw their
	/// materials as their .shader does, and so do complex's translucent ones; a material using a feature a surface
	/// doesn't do (generic's vertex paint) stays on the .shader.
	/// </summary>
	[TestMethod]
	public void CombinerSurfacesDrawAsTheirShaders()
	{
		Compiler();
		var previousHost = RenderHost.Current;
		try
		{
			byte[] DrawMaterial( string path, bool surfaces, out GpuMaterial loaded )
			{
				using var backend = new BackendDevice( Device );
				var errors = new List<string>();
				backend.Surfaces.Error = errors.Add;
				backend.Surfaces.ConvertShaders = surfaces;
				RenderHost.Current = new BackendRenderHost( backend );
				loaded = ((GpuDevice)backend).LoadMaterial( path );
				var picture = Draw( backend, loaded );
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				return picture;
			}

			foreach ( var path in new[] { "materials/dev/gray_25.vmat", "materials/dev/roomwalls_white.vmat", "materials/dev/bright_10.vmat", "materials/tools/hlmv_ground.vmat", "materials/dev/sky_camera.vmat", "models/vr/autochap_hand.vmat", "materials/default/vertex_color.vmat" } )
			{
				var reference = DrawMaterial( path, false, out var classic );
				if ( classic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( $"{path} isn't compiled" );
				var converted = DrawMaterial( path, true, out var surface );
				Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{path} isn't a surface by default" );
				var name = Path.GetFileNameWithoutExtension( path );
				SaveRgb( reference, $"combiner-{name}-shader" );
				SaveRgb( converted, $"combiner-{name}-surface" );
				Assert.IsTrue( Mean( reference, converted ) < 0.5, $"{path}: the surface differs by {Mean( reference, converted ):F3}" );
			}

			// Translucent: blended with the .shader's render state, in the translucent layer
			foreach ( var path in new[] { "materials/tools/toolsclip.vmat", "materials/tools/toolsgeneric.vmat", "materials/dev/primary_green_additive.vmat", "materials/dev/primary_white_trans.vmat" } )
			{
				var reference = DrawMaterial( path, false, out var classic );
				if ( classic is not BackendMaterial { Shader: not null, IsTranslucent: true } ) Assert.Inconclusive( $"{path} isn't compiled, or isn't translucent" );
				var converted = DrawMaterial( path, true, out var surface );
				Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{path} isn't a surface by default" );
				Assert.IsTrue( surface.IsTranslucent, $"{path} isn't translucent as a surface" );
				var name = Path.GetFileNameWithoutExtension( path );
				SaveRgb( reference, $"combiner-{name}-shader" );
				SaveRgb( converted, $"combiner-{name}-surface" );
				Assert.IsTrue( Mean( reference, converted ) < 0.5, $"{path}: the surface differs by {Mean( reference, converted ):F3}" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// standard takes complex, simple and generic materials, each with its shader's packing, and a pipeline specialised for
	/// a material's features (its [Specialize] fields as constants) draws what the unspecialised one does.
	/// </summary>
	[TestMethod]
	public void StandardSurfaceSpecializesForItsMaterials()
	{
		var compiled = Compiler().CompileSurface( Path.Combine( Core, "shaders", "standard.slang" ) );
		Assert.AreEqual( "UseUnlit", string.Join( ", ", compiled.Material.Features.Select( f => f.Name ) ), "only [Specialize] fields are specialised for" );
		StringAssert.Contains( compiled.Generated, "SurfaceSpecialize( m );" );

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

			foreach ( var (path, packing) in new[] { ("materials/default.vmat", 0), ("materials/dev/simple/floor_simple_128.vmat", 1), ("materials/dev/primary_blue.vmat", 2), ("materials/dev/sky_helper.vmat", 2) } )
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
	/// Surfaces bind the frame's view set themselves: with enough draws, layers record on command lists of their own, a
	/// surface is the first draw of its list, and without the view set it would light and fog as no view does.
	/// </summary>
	[TestMethod]
	public void SurfacesBindTheViewSetOnTheirOwnLists()
	{
		Compiler();
		var previousHost = RenderHost.Current;
		try
		{
			byte[] DrawMaterial( string path, bool surfaces, out GpuMaterial loaded )
			{
				// Drawn the classic way, so opaque draws record on the worker segments' lists
				using var backend = new BackendDevice( Device ) { GpuDriven = false };
				backend.Surfaces.ConvertShaders = surfaces;
				RenderHost.Current = new BackendRenderHost( backend );
				loaded = ((GpuDevice)backend).LoadMaterial( path );
				return Draw( backend, loaded, boxes: 144 );
			}

			foreach ( var path in new[] { "materials/dev/primary_white_trans.vmat", "materials/dev/gray_25.vmat" } )
			{
				var reference = DrawMaterial( path, false, out var classic );
				if ( classic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( $"{path} isn't compiled" );
				var converted = DrawMaterial( path, true, out var surface );
				Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{path} isn't a surface by default" );
				var name = Path.GetFileNameWithoutExtension( path );
				SaveRgb( reference, $"viewset-{name}-shader" );
				SaveRgb( converted, $"viewset-{name}-surface" );
				Assert.IsTrue( Mean( reference, converted ) < 0.5, $"{path}: the surface differs by {Mean( reference, converted ):F3}" );
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
	/// glass.slang: F_TRANSLUCENT puts a glass material in the translucent layer whatever its quality, refractive glass
	/// unblended, simple glass blended by opacity, layered glass premultiplied, all writing depth as glass.shader does; with
	/// no depth passes of its own. Its forward pass reads the frame-buffer copy, the depth chain and the BRDF lookup by
	/// attribute; a surface that doesn't read the view inputs (complex) doesn't bind the copy.
	/// </summary>
	[TestMethod]
	public void GlassDrawsTranslucentFromTheFrameBufferCopy()
	{
		var glass = Compiler().CompileSurface( Path.Combine( Core, "shaders", "glass.slang" ) );
		var layout = glass.Material;
		Assert.AreEqual( true, layout.RenderState.DepthWrite );

		SurfaceRenderState Resolve( int translucent, int quality )
		{
			var record = new byte[layout.Size];
			BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "UseTranslucent" ).Offset ), translucent );
			BitConverter.TryWriteBytes( record.AsSpan( layout.Field( "Quality" ).Offset ), quality );
			return layout.States().Resolve( record );
		}

		Assert.IsTrue( Resolve( 1, 0 ) is { Blend: SurfaceBlend.Opaque, Translucent: true, DepthWrite: true, DrawsTranslucent: true } );
		Assert.IsTrue( Resolve( 1, 1 ) is { Blend: SurfaceBlend.Alpha, DepthWrite: true, DrawsTranslucent: true } );
		Assert.IsTrue( Resolve( 1, 2 ) is { Blend: SurfaceBlend.Premultiplied, DrawsTranslucent: true } );
		Assert.IsTrue( Resolve( 0, 0 ) is { DrawsTranslucent: false } );

		var forward = glass.Pass( ShaderPasses.Forward + "Translucent" ).Pixel;
		foreach ( var name in new[] { "FrameBufferCopyTexture", "DepthChainDownsample", "BRDFLookup" } )
			Assert.IsTrue( forward.Bindings.Any( b => b.Name == name ), name );
		Assert.IsNotNull( glass.Pass( ShaderPasses.Forward + "Translucent" + ShaderPasses.GpuDriven ), glass.GpuDrivenError );

		var complex = Compiler().CompileSurface( Path.Combine( Core, "shaders", "standard.slang" ) ).Pass( ShaderPasses.Forward ).Pixel;
		Assert.IsFalse( complex.Bindings.Any( b => b.Name == "FrameBufferCopyTexture" ), string.Join( ", ", complex.Bindings.Select( b => b.Name ) ) );

		// A translucent glass material is translucent and has no depth passes: its .shader's draw its shadows
		using var backend = new BackendDevice( Device );
		var material = (SurfaceMaterial)backend.Surfaces.CreateMaterial( Path.Combine( Core, "shaders", "glass.slang" ), null, new Dictionary<string, float[]> { ["F_TRANSLUCENT"] = [1] } );
		Assert.IsTrue( material.IsTranslucent );
		Assert.IsTrue( material.RenderState is { Blend: SurfaceBlend.Opaque, Translucent: true } );
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
			var text = File.ReadAllText( Path.Combine( Core, "shaders", name + ".shader" ) );
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
	/// Surfaces own their render state: every core material of a shader with a surface resolves, as that surface's
	/// material, to the state its .shader's forward variant has (blending, depth test and write, culling, alpha to
	/// coverage as the alpha test).
	/// </summary>
	[TestMethod]
	public void SurfaceRenderStatesMatchTheirShaders()
	{
		Compiler();
		var shaders = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "complex", "generic", "simple", "vertex_color", "static_overlay", "glass", "foliage", "bark", "terrain_preview", "water_simple" };
		var paths = Directory.EnumerateFiles( Path.Combine( Core, "materials" ), "*.vmat", SearchOption.AllDirectories )
			.Concat( Directory.EnumerateFiles( Path.Combine( Core, "models" ), "*.vmat", SearchOption.AllDirectories ) )
			.Where( p => System.Text.RegularExpressions.Regex.Match( File.ReadAllText( p ), @"""shader""\s+""shaders/(\w+)\.shader" ) is { Success: true } m && shaders.Contains( m.Groups[1].Value ) )
			.Select( p => Path.GetRelativePath( Core, p ).Replace( '\\', '/' ) )
			.ToList();

		using var classicDevice = new BackendDevice( Device );
		classicDevice.Surfaces.ConvertShaders = false;
		using var surfaceDevice = new BackendDevice( Device );
		surfaceDevice.Surfaces.Convert( "shaders/static_overlay.shader", "static_overlay.slang" );
		var errors = new List<string>();
		surfaceDevice.Surfaces.Error = errors.Add;

		var compared = 0;
		var mismatches = new List<string>();
		foreach ( var path in paths )
		{
			if ( ((GpuDevice)classicDevice).LoadMaterial( path ) is not BackendMaterial { Shader: not null } classic ) continue;
			if ( ((GpuDevice)surfaceDevice).LoadMaterial( path ) is not SurfaceMaterial surface ) continue;
			var variant = classic.Variant( classicDevice.Shaders, "Forward", "Forward", _ => null, _ => null );
			if ( variant?.State is not { } expected ) continue;

			var state = surface.RenderState;
			var blend = SurfaceProgram.Blend( state.Blend );
			var classicBlend = expected.Blend[0];
			var differences = new List<string>();
			if ( classicBlend.Enable != blend.Enable || (blend.Enable && (classicBlend.SourceColor, classicBlend.DestinationColor) != (blend.SourceColor, blend.DestinationColor)) )
				differences.Add( $"blend {classicBlend.SourceColor}/{classicBlend.DestinationColor} ({(classicBlend.Enable ? "on" : "off")}) vs {state.Blend}" );
			// foliage.shader's colour pass tests for equality after the prepass without writing; its surface writes the same depth
			var foliage = surface.Shader.EndsWith( "foliage.slang", StringComparison.OrdinalIgnoreCase );
			if ( expected.DepthWrite != state.DepthWrite && !foliage ) differences.Add( $"depth write {expected.DepthWrite} vs {state.DepthWrite}" );
			if ( expected.DepthTest != state.DepthTest ) differences.Add( $"depth test {expected.DepthTest} vs {state.DepthTest}" );
			if ( (expected.Cull == CullMode.None) != state.TwoSided ) differences.Add( $"cull {expected.Cull} vs two-sided {state.TwoSided}" );
			// generic.shader clips its alpha test hard, without alpha to coverage; its surface's alpha-tested pass uses it, with a
			// hard coverage (a known small difference at multisampled edges)
			var hardClip = !expected.AlphaToCoverage && state.AlphaTest && classic.Shader.Name.Contains( "generic", StringComparison.OrdinalIgnoreCase );
			if ( !state.Blends && !hardClip && expected.AlphaToCoverage != state.AlphaTest ) differences.Add( $"alpha to coverage {expected.AlphaToCoverage} vs alpha test {state.AlphaTest}" );
			if ( differences.Count > 0 ) mismatches.Add( $"{path}: {string.Join( ", ", differences )}" );
			compared++;
		}

		Console.WriteLine( $"{compared} materials compared of {paths.Count}" );
		if ( compared == 0 ) Assert.Inconclusive( "No core material of a converted shader is compiled" );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		Assert.AreEqual( 0, mismatches.Count, string.Join( "\n", mismatches ) );
	}

	/// <summary>
	/// A converted material answers the scene's attributes as its .shader material does (<c>overlay</c> puts a map's
	/// decals in the static overlay layer, <c>NoZPrepass</c> keeps an object out of the prepass, and so on), whatever the
	/// default asked with: the lab's map loader reads these for its objects' layers and flags.
	/// </summary>
	[TestMethod]
	public void SurfaceSceneAttributesMatchTheirShaders()
	{
		Compiler();
		using var classicDevice = new BackendDevice( Device );
		classicDevice.Surfaces.ConvertShaders = false;
		using var surfaceDevice = new BackendDevice( Device );

		var compared = 0;
		var set = 0;
		var mismatches = new List<string>();
		foreach ( var path in new[] { "materials/dev/static_overlay_test.vmat", "materials/tools/toolsclip.vmat", "materials/dev/primary_green_additive.vmat", "materials/default.vmat" } )
		{
			if ( ((GpuDevice)classicDevice).LoadMaterial( path ) is not BackendMaterial { Shader: not null } classic ) continue;
			if ( ((GpuDevice)surfaceDevice).LoadMaterial( path ) is not SurfaceMaterial surface ) continue;
			foreach ( var name in new[] { "overlay", "NoZPrepass", "DoNotCastShadows", "bWantsFBCopyTexture", "NeedsLightProbe", "SupportsLightmapping" } )
			{
				foreach ( var fallback in new[] { false, true } )
				{
					var expected = classic.Attribute( name, fallback );
					if ( expected != fallback ) set++;
					if ( expected != surface.Attribute( name, fallback ) ) mismatches.Add( $"{path} {name} (default {fallback}): {expected} vs {surface.Attribute( name, fallback )}" );
				}
			}

			compared++;
		}

		if ( compared == 0 ) Assert.Inconclusive( "No test material of a converted shader is compiled" );
		Assert.AreEqual( 0, mismatches.Count, string.Join( "\n", mismatches ) );
		Assert.IsTrue( set > 0, "the shaders set none of the attributes" );
	}

	/// <summary>
	/// The surface material records grow while a frame records (more materials than the first buffer holds, drawn at
	/// once): draws recorded before the growth keep the buffer they bound until the GPU is done with it, so validation
	/// stays clean.
	/// </summary>
	[TestMethod]
	public void SurfaceRecordsGrowWhileDrawing()
	{
		Compiler();
		using var backend = new BackendDevice( Device );
		backend.Surfaces.AddFolder( Surfaces );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		var materials = new List<Rock>();
		try
		{
			// 64 KiB holds 2048 of Rock's 32-byte records
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.5f, -0.75f ).Normal;
			for ( int i = 0; i < 2304; i++ )
			{
				var rock = new Rock { Tint = new Vector3( (i % 48) / 48f, (i / 48) / 48f, 0.5f ) };
				materials.Add( rock );
				world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, rock ), new Transform( new Vector3( (i % 48) * 12 - 288, (i / 48) * 12 - 288, 0 ), Rotation.Identity, 0.1f ) ) );
			}

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
		}
		finally
		{
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
		using var backend = new BackendDevice( Device ) { TimeDraws = true };
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

			var color = backend.LoadTexture( "materials/default/rope_color_png_95bee33c.generated.vtex" );
			var normal = backend.LoadTexture( "materials/default/rope_normal_png_b8960e44.generated.vtex" );
			var rma = backend.LoadTexture( "materials/default/rope01_vmat_g_trma_bbf6a2a.generated.vtex" );
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
	/// skin.slang lights through the final combiner with skin.shader's lighting features (its [Combiner] defines, ahead of
	/// the engine's includes) and draws skin materials as skin.shader does.
	/// </summary>
	[TestMethod]
	public void SkinSurfaceDrawsSkinMaterials()
	{
		var skin = Compiler().CompileSurface( Path.Combine( Core, "shaders", "skin.slang" ) );
		StringAssert.Contains( skin.Generated, "#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_PREINTEGRATED" );
		StringAssert.Contains( skin.Generated, "#define S_TWO_LOBE_SPECULAR 1" );
		Assert.IsTrue( skin.Generated.IndexOf( "S_TRANSMISSIVE", StringComparison.Ordinal ) < skin.Generated.IndexOf( "surface_passes.slangh", StringComparison.Ordinal ) );

		var previousHost = RenderHost.Current;
		try
		{
			byte[] DrawMaterial( string path, bool convert, out GpuMaterial loaded )
			{
				using var backend = new BackendDevice( Device );
				var errors = new List<string>();
				backend.Surfaces.Error = errors.Add;
				RenderHost.Current = new BackendRenderHost( backend );
				backend.Surfaces.ConvertShaders = false;
				if ( convert )
				{
					backend.Surfaces.AddFolder( Path.Combine( Core, "shaders" ) );
					backend.Surfaces.Convert( "shaders/skin.shader", "skin.slang" );
				}

				loaded = ((GpuDevice)backend).LoadMaterial( path );
				var picture = Draw( backend, loaded );
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				return picture;
			}

			const string head = "models/citizen_human/heads/citizen_human_malehead01/citizen_human_malehead01.vmat";
			var reference = DrawMaterial( head, false, out var classic );
			if ( classic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( $"{head} isn't compiled" );
			var converted = DrawMaterial( head, true, out var surface );
			Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{head} wasn't converted" );
			SaveRgb( reference, "skin-shader" );
			SaveRgb( converted, "skin-surface" );
			Assert.IsTrue( Mean( reference, converted ) < 0.5, $"the skin surface differs by {Mean( reference, converted ):F3}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>
	/// eyeball_surface.slang lights itself in two layers through the engine's lighting (ILayeredSurface) and draws eyeball
	/// materials as eyeball.shader's compiled programs and its pass module do.
	/// </summary>
	[TestMethod]
	[TestCategory( "Slow" )]
	public void EyeballSurfaceDrawsEyeballMaterials()
	{
		var eyeball = Compiler().CompileSurface( Path.Combine( Core, "shaders", "eyeball_surface.slang" ) );
		StringAssert.Contains( eyeball.Generated, "SurfaceForwardLayered<Eyeball, false>" );
		StringAssert.Contains( eyeball.Generated, "#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_WRAP" );
		var skin = Compiler().CompileSurface( Path.Combine( Core, "shaders", "skin.slang" ) );
		Assert.IsFalse( skin.Generated.Contains( "SurfaceForwardLayered", StringComparison.Ordinal ), "only layered surfaces shade through their own Shade" );

		var previousHost = RenderHost.Current;
		try
		{
			byte[] DrawMaterial( string path, bool slangPasses, bool convert, out GpuMaterial loaded )
			{
				using var backend = new BackendDevice( Device ) { SlangPasses = slangPasses };
				var errors = new List<string>();
				backend.Surfaces.Error = errors.Add;
				backend.Passes.Error = errors.Add;
				RenderHost.Current = new BackendRenderHost( backend );
				backend.Surfaces.ConvertShaders = false;
				if ( convert )
				{
					backend.Surfaces.AddFolder( Path.Combine( Core, "shaders" ) );
					backend.Surfaces.Convert( "shaders/eyeball.shader", "eyeball_surface.slang" );
				}

				loaded = ((GpuDevice)backend).LoadMaterial( path );
				// On the citizen, whose uvs spread the eye's textures over the frame
				var citizen = backend.LoadModel( "models/citizen/citizen.vmdl" );
				if ( citizen is null ) Assert.Inconclusive( "models/citizen/citizen.vmdl isn't built" );
				// The eye occlusion's projection, which the game sets on eyes, across the model: without it the occlusion
				// texture is read at its centre for every pixel. Not symmetric, so a transposed matrix would show
				var attributes = backend.CreateParameters();
				attributes.Set( "EyeOcclusionViewProj", (Matrix)new System.Numerics.Matrix4x4( 0, 0.012f, 0, 0, 0, 0, 0.012f, 0, 0.012f, 0, 0, 0, 0, 0, 0, 1 ) );
				var picture = Draw( backend, loaded, model: citizen, attributes: attributes );
				Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
				return picture;
			}

			const string eyes = "models/humans/misc/playermodeleyes_light.vmat";
			var reference = DrawMaterial( eyes, false, false, out var classic );
			if ( classic is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( $"{eyes} isn't compiled" );
			var module = DrawMaterial( eyes, true, false, out _ );
			var converted = DrawMaterial( eyes, true, true, out var surface );
			Assert.IsInstanceOfType<SurfaceMaterial>( surface, $"{eyes} wasn't converted" );
			SaveRgb( reference, "eyeball-shader" );
			SaveRgb( module, "eyeball-module" );
			SaveRgb( converted, "eyeball-surface" );
			TestContext.WriteLine( $"surface vs shader {Mean( reference, converted ):F3}, vs module {Mean( module, converted ):F3}; module vs shader {Mean( reference, module ):F3}" );
			Assert.IsTrue( Lit( reference ) > Width * Height / 10, "The .shader drew too little to compare" );
			// Lit, not black: the eye occlusion's projection (a matrix attribute) reaches the shaders
			Assert.IsTrue( Bright( reference ) > Width * Height / 100, $"eyeball.shader drew {Bright( reference )} lit pixels: is the occlusion projection bound?" );
			Assert.IsTrue( Bright( converted ) > Width * Height / 100, $"the surface drew {Bright( converted )} lit pixels: is the occlusion projection bound?" );
			Assert.IsTrue( Mean( reference, converted ) < 0.5, $"the eyeball surface differs from eyeball.shader by {Mean( reference, converted ):F3}" );
			Assert.IsTrue( Mean( module, converted ) < 0.5, $"the eyeball surface differs from its pass module by {Mean( module, converted ):F3}" );
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
	byte[] Draw( BackendDevice backend, GpuMaterial material, Color? tint = null, int boxes = 1, RenderMesh model = null, ShaderParameters attributes = null )
	{
		using var system = new RenderSystem { ParallelRecording = boxes > 1 };
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
