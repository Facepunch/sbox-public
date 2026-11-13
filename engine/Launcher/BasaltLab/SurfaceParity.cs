using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Sandbox.BasaltLab.Surfaces;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Text.RegularExpressions;
using GpuDevice = Basalt.Gpu.Device;

namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-surface-parity folder [-validation] [-frames N] [-tolerance N] [-surface-classes]</c>: the Slang material tier
/// against today's shaders, on a Basalt.Gpu device of its own with nothing of the engine started. The same material is
/// drawn three ways: as a <c>.shader</c> (<c>shaders/surface_test_rock.shader</c>, compiled by vfx), as a surface
/// (<c>Surfaces/rock.slang</c>) and as a raw module (<c>Surfaces/rock_raw.slang</c>), the last two compiled by
/// Sandbox.ShaderCompiler and set from C# through their generated classes. Each picture is compared with the
/// <c>.shader</c>'s; the run writes <c>shader.png</c>, <c>surface.png</c>, <c>raw.png</c> and a diff of each, and exits 1
/// when a mean difference is over <c>-tolerance</c> (default 0.5, in 8-bit steps) or validation reported errors.
/// <c>-surface-classes</c> regenerates <c>Surfaces/*.g.cs</c> first.
/// </summary>
internal static class SurfaceParity
{
	const int Width = 640, Height = 400;

	public static bool Requested => Args.String( "-surface-parity" ) is not null;

	/// <summary>The lab's Slang materials, in the checkout beside the content.</summary>
	static string Folder( BackendDevice device ) => Args.String( "-surfaces" ) ?? Path.GetFullPath( Path.Combine( device.Content.Roots[0], "..", "..", "engine", "Launcher", "BasaltLab", "Surfaces" ) );

	public static int Run()
	{
		var folder = Path.GetFullPath( Args.String( "-surface-parity" ) );
		var tolerance = double.TryParse( Args.String( "-tolerance" ), System.Globalization.CultureInfo.InvariantCulture, out var t ) ? t : 0.5;
		Directory.CreateDirectory( folder );

		var errors = new List<string>();
		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = "Basalt Lab surface parity",
			Validation = Args.Has( "-validation" ),
			Presentation = false,
			Message = m =>
			{
				if ( m.Severity == MessageSeverity.Error ) lock ( errors ) errors.Add( m.Text );
				Console.WriteLine( $"Basalt.Gpu: {m.Severity} {m.Text}" );
			},
		} );

		using var device = new BackendDevice( gpu ) { TimeDraws = true };

		// The references are the .shader route: no core shader's materials load as surfaces unless converted below
		device.Surfaces.ConvertShaders = false;
		var surfaces = Folder( device );
		device.Surfaces.AddFolder( surfaces );
		device.Surfaces.Error = m => Console.WriteLine( $"Shader: {m}" );

		// Compiled up front, so the times are the compiler's and a failure is reported once
		var lines = new List<string>();
		foreach ( var file in new[] { "rock.slang", "rock_raw.slang", "rock_lit.slang", "foliage.slang" } )
		{
			if ( device.Surfaces.Compile( file ) is not { } compiled ) return Fail( $"{file} didn't compile" );
			lines.Add( $"{file}: {compiled.Tier} {compiled.Material.Type}, {compiled.Passes.Count} passes, record {compiled.Material.Size} bytes, compiled in {compiled.Milliseconds:F0} ms" );
			if ( Args.Has( "-surface-classes" ) )
				File.WriteAllText( Path.Combine( surfaces, Path.ChangeExtension( file, ".g.cs" ) ), MaterialClassWriter.Write( compiled, "Sandbox.BasaltLab.Surfaces" ).Replace( "\n", "\r\n" ) );
		}

		var previous = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( device );
		var failed = 0;
		try
		{
			var color = device.LoadTexture( "materials/default/rope_color_png_95bee33c.generated.vtex" );
			var normal = device.LoadTexture( "materials/default/rope_normal_png_b8960e44.generated.vtex" );
			var rma = device.LoadTexture( "materials/default/rope01_vmat_g_trma_bbf6a2a.generated.vtex" );
			var tint = new Vector3( 1.0f, 0.85f, 0.7f );
			const float roughnessScale = 0.8f;

			// Today's .shader, its parameters set by variable name
			var shader = (BackendMaterial)((Basalt.GpuDevice)device).CreateMaterial( "Rock (.shader)", "shaders/surface_test_rock.shader" );
			if ( shader.Shader?.Name.Contains( "surface_test_rock", StringComparison.OrdinalIgnoreCase ) != true ) return Fail( "shaders/surface_test_rock.shader isn't compiled: build shaders first" );
			shader.SetTexture( "g_tColor", (BackendTexture)color );
			shader.SetTexture( "g_tNormal", (BackendTexture)normal );
			shader.SetTexture( "g_tRma", (BackendTexture)rma );
			shader.Set( "g_vTint", [tint.x, tint.y, tint.z] );
			shader.Set( "g_flRoughnessScale", [roughnessScale] );

			// The surface and the raw module, through their generated classes
			using var surface = new Rock { Color = color, Normal = normal, Rma = rma, Tint = tint, RoughnessScale = roughnessScale, DisplayName = "Rock (surface)" };
			using var raw = new RockRaw { Color = color, Normal = normal, Rma = rma, Tint = tint, RoughnessScale = roughnessScale, DisplayName = "Rock (raw)" };

			var reference = Render( device, shader );
			Save( reference, Path.Combine( folder, "shader.png" ) );

			// The surface twice: its record offset passed on per pixel (the default) and read per draw
			foreach ( var (name, material, perPixel) in new (string, GpuMaterial, bool)[] { ("surface", surface, true), ("surface-perdraw", surface, false), ("raw", raw, true) } )
			{
				device.Surfaces.MaterialOffsetPerPixel = perPixel;
				var pixels = Render( device, material );
				Save( pixels, Path.Combine( folder, $"{name}.png" ) );
				var (mean, max, over, diff) = Compare( reference, pixels );
				Save( diff, Path.Combine( folder, $"{name}-diff.png" ) );
				var ok = mean <= tolerance;
				if ( !ok ) failed++;
				lines.Add( $"{name} against .shader: mean difference {mean:F3}, max {max}, {over} pixels over 8{(ok ? "" : " FAIL")}" );
			}

			device.Surfaces.MaterialOffsetPerPixel = true;

			// The light loop's hook (ILitSurface.Light): with no light term of its own a lit surface draws as Rock does, so
			// standard lighting is untouched; with one, every light adds to it, the point light alone too
			using var lit = new RockLit { Color = color, Normal = normal, Rma = rma, Tint = tint, RoughnessScale = roughnessScale, WrapStrength = 0, DisplayName = "Rock (lit surface)" };
			{
				var pixels = Render( device, lit );
				Save( pixels, Path.Combine( folder, "lit.png" ) );
				var (mean, max, over, diff) = Compare( reference, pixels );
				Save( diff, Path.Combine( folder, "lit-diff.png" ) );
				var ok = mean <= tolerance;
				if ( !ok ) failed++;
				lines.Add( $"lit surface, no light term, against .shader: mean difference {mean:F3}, max {max}, {over} pixels over 8{(ok ? "" : " FAIL")}" );

				lit.WrapStrength = 1;
				foreach ( var sun in new[] { true, false } )
				{
					var name = sun ? "lit-wrap" : "lit-wrap-pointonly";
					var before = sun ? reference : Render( device, shader, sun: false );
					var after = Render( device, lit, sun: sun );
					Save( after, Path.Combine( folder, $"{name}.png" ) );
					var (wmean, wmax, wover, wdiff) = Compare( before, after );
					Save( wdiff, Path.Combine( folder, $"{name}-diff.png" ) );
					var changed = Changed( before, after );
					lines.Add( $"lit surface, wrap term, {(sun ? "sun and point light" : "point light alone")}, against .shader: mean difference {wmean:F3}, max {wmax}, {changed} pixels changed (reported: the term it adds)" );
				}
			}

			// The forward pixel shaders, for register counts (amdllpc)
			var variant = shader.Variant( device.Shaders, "Forward", "Forward", _ => null, _ => null );
			if ( variant?.Pixel?.Bytecode is { } bytecode && (bytecode.Spirv ?? bytecode.Shared?.Spirv) is { } classic ) File.WriteAllBytes( Path.Combine( folder, "shader_ps.spv" ), classic );
			File.WriteAllBytes( Path.Combine( folder, "surface_ps.spv" ), device.Surfaces.Compile( "rock.slang" ).Pass( ShaderPasses.Forward ).Pixel.Spirv );
			File.WriteAllBytes( Path.Combine( folder, "surface_depth_alphatest_ps.spv" ), device.Surfaces.Compile( "rock.slang" ).Pass( ShaderPasses.DepthAlphaTest ).Pixel.Spirv );
			File.WriteAllBytes( Path.Combine( folder, "raw_ps.spv" ), device.Surfaces.Compile( "rock_raw.slang" ).Pass( ShaderPasses.Forward ).Pixel.Spirv );
			File.WriteAllBytes( Path.Combine( folder, "lit_ps.spv" ), device.Surfaces.Compile( "rock_lit.slang" ).Pass( ShaderPasses.Forward ).Pixel.Spirv );

			// foliage.shader (alpha test, translucency, grazing fade and wind animation on; sway off, so the picture doesn't depend on
			// the time) against the same material converted by hand to a surface
			var cutOut = (BackendMaterial)device.LoadMaterial( "materials/dev/simple/simple_rail_32.vmat" );
			cutOut.Textures.TryGetValue( "g_tColor", out var leaf );
			// A material's features and values are read when it first draws, so each setup gets a material of its own
			BackendMaterial FoliageShader( bool alphaTest = true, bool transmissive = true, bool grazing = true, float variation = 0.1f, float bending = 0.1f )
			{
				var m = (BackendMaterial)((Basalt.GpuDevice)device).CreateMaterial( "Foliage (.shader)", "shaders/foliage.shader" );
				if ( m.Shader is null ) return m;
				foreach ( var (feature, on) in new[] { ("F_ALPHA_TEST", alphaTest), ("F_TRANSMISSIVE", transmissive), ("F_FOLIAGE_ANIMATION", true), ("F_GRAZING_FADE", grazing) } ) m.Features[m.Shader.FeatureIndex( feature )] = on ? 1 : 0;
				m.SetTexture( "g_tColor", leaf );
				m.SetTexture( "g_tNormal", (BackendTexture)normal );
				m.SetTexture( "g_tRma", (BackendTexture)rma );
				m.SetTexture( "g_tTransmissiveColor", leaf );
				m.Set( "g_flSwayStrength", [0] );
				m.Set( "g_flTransmissionScale", [1] );
				m.Set( "g_flNormalVariation", [variation] );
				return m;
			}

			var foliageShader = FoliageShader();
			if ( leaf is null || foliageShader.Shader?.Name.Contains( "foliage", StringComparison.OrdinalIgnoreCase ) != true ) return Fail( "foliage.shader or simple_rail_32.vmat isn't compiled" );

			using var foliage = new Foliage { Color = leaf, Normal = normal, Rma = rma, Transmissive = true, TransmissiveColor = leaf, TransmissionScale = 1, SwayStrength = 0, GrazingFade = true, AlphaTest = true, DisplayName = "Foliage (surface)" };
			// Without multisampling the alpha test is a hard cut; with 4x it is alpha to coverage, which is what Construct draws
			foreach ( var samples in new[] { 1, 4 } )
			{
				var suffix = samples > 1 ? $"-{samples}x" : "";
				var foliageReference = Render( device, foliageShader, alphaTest: true, samples );
				Save( foliageReference, Path.Combine( folder, $"foliage-shader{suffix}.png" ) );
				var foliagePixels = Render( device, foliage, alphaTest: true, samples );
				Save( foliagePixels, Path.Combine( folder, $"foliage-surface{suffix}.png" ) );
				var (fmean, fmax, fover, fdiff) = Compare( foliageReference, foliagePixels );
				Save( fdiff, Path.Combine( folder, $"foliage-surface{suffix}-diff.png" ) );
				var fok = fmean <= tolerance;
				if ( !fok ) failed++;
				lines.Add( $"foliage surface against foliage.shader, {samples}x: mean difference {fmean:F3}, max {fmax}, {fover} pixels over 8{(fok ? "" : " FAIL")}" );
			}

			// -foliage-terms: the same at 4x with one feature off on both sides at a time, to say where a difference comes from
			if ( Args.Has( "-foliage-terms" ) )
			{
				foreach ( var (name, transmissive, grazing, alphaTest, variation, bending) in new[] { ("as before", true, true, true, true, true), ("no normal variation", true, true, true, false, true), ("no translucency", false, true, true, true, true), ("no grazing fade", true, false, true, true, true), ("no alpha test", true, true, false, true, true), ("no wind bending", true, true, true, true, false), ("none of them", false, false, false, false, false) } )
				{
					var termShader = FoliageShader( alphaTest, transmissive, grazing, variation ? 0.1f : 0, bending ? 0.1f : 0 );
					// A surface's fields are read at every draw, so the one material does
					foliage.Transmissive = transmissive;
					foliage.GrazingFade = grazing;
					foliage.AlphaTest = alphaTest;
					foliage.NormalVariation = variation ? 0.1f : 0;
					var a = Render( device, termShader, alphaTest, 4 );
					var b = Render( device, foliage, alphaTest, 4 );
					var (tmean, tmax, tover, tdiff) = Compare( a, b );
					Save( tdiff, Path.Combine( folder, $"foliage-{name.Replace( ' ', '-' )}-4x-diff.png" ) );
					Save( a, Path.Combine( folder, $"foliage-{name.Replace( ' ', '-' )}-4x-shader.png" ) );
					Save( b, Path.Combine( folder, $"foliage-{name.Replace( ' ', '-' )}-4x-surface.png" ) );
					lines.Add( $"foliage surface against foliage.shader, 4x, {name}: mean difference {tmean:F3}, max {tmax}, {tover} pixels over 8 (reported, not gated)" );
				}

				// The .shader against itself, as a second material: what the same setup varies by between two draws
				{
					var a = Render( device, FoliageShader( false, false, false, 0, 0 ), false, 4 );
					var b = Render( device, FoliageShader( false, false, false, 0, 0 ), false, 4 );
					var (smean, smax, sover, _) = Compare( a, b );
					lines.Add( $"foliage.shader against itself, 4x, none of them: mean difference {smean:F3}, max {smax}, {sover} pixels over 8" );
				}
			}
			File.WriteAllBytes( Path.Combine( folder, "foliage_surface_ps.spv" ), device.Surfaces.Compile( "foliage.slang" ).Pass( ShaderPasses.Forward + "AlphaTest" ).Pixel.Spirv );
			var foliageVariant = foliageShader.Variant( device.Shaders, "Forward", "Forward;F", _ => null, _ => null );
			if ( foliageVariant?.Pixel?.Bytecode is { } fb && (fb.Spirv ?? fb.Shared?.Spirv) is { } fs ) File.WriteAllBytes( Path.Combine( folder, "foliage_shader_ps.spv" ), fs );

			// blendable.slang against blendable.shader with four layers (F_MULTIBLEND 3), for register counts; their pictures
			// are compared on Construct (-construct -real -surface-convert blendable)
			if ( device.Surfaces.Compile( Path.Combine( device.Content.Roots[0], "shaders", "blendable.slang" ) ) is { } blendable )
			{
				File.WriteAllBytes( Path.Combine( folder, "blendable_surface_ps.spv" ), blendable.Pass( ShaderPasses.Forward ).Pixel.Spirv );
				var blendableShader = (BackendMaterial)((Basalt.GpuDevice)device).CreateMaterial( "Blendable (.shader)", "shaders/blendable.shader" );
				if ( blendableShader.Shader is { } shaderFile )
				{
					blendableShader.Features[shaderFile.FeatureIndex( "F_MULTIBLEND" )] = 3;
					var forwardMode = shaderFile.Modes.FirstOrDefault( m => m.Name.Contains( "Forward", StringComparison.OrdinalIgnoreCase ) )?.Name ?? "Forward";
					var blendableVariant = blendableShader.Variant( device.Shaders, forwardMode, "Forward;B3", _ => null, _ => null );
					if ( blendableVariant?.Pixel?.Bytecode is { } bb && (bb.Spirv ?? bb.Shared?.Spirv) is { } bs ) File.WriteAllBytes( Path.Combine( folder, "blendable_shader_ps.spv" ), bs );
					else lines.Add( $"blendable.shader: no forward pixel shader to count ({(blendableVariant is null ? "no variant" : blendableVariant.Pixel is null ? "no pixel stage" : "no SPIR-V")} in mode {forwardMode} of {string.Join( ", ", shaderFile.Modes.Select( m => m.Name ) )})" );
				}
			}

			if ( Args.Has( "-convert-shaders" ) ) ConvertShaders( device, folder, lines );
			if ( Args.String( "-convert-materials" ) is { } materials ) ConvertMaterials( gpu, materials, folder, lines );
		}
		finally
		{
			gpu.WaitIdle();
			RenderHost.Current = previous;
		}

		// Surfaces aren't in GPU-driven buckets yet: with it on (the default; -no-gpudriven turns it off) they draw the classic way
		lines.Add( $"GPU-driven {(device.GpuDriven ? "on" : "off")}: {device.GpuDrivenReport}" );
		lines.Add( $"Surface parity: {(failed == 0 ? "pass" : $"{failed} failed")} ({folder}); Basalt.Gpu validation errors {gpu.ValidationErrors}" );
		foreach ( var line in lines ) Console.WriteLine( line );
		File.WriteAllLines( Path.Combine( folder, "results.txt" ), lines );
		return failed == 0 && gpu.ValidationErrors == 0 ? 0 : 1;
	}

	/// <summary>The shaders written the common/pixel.hlsl way that convert as they are, by their path under the game folder.</summary>
	static readonly string[] Convertible =
	[
		"templates/material.shader", "core/shaders/water_simple.shader", "core/shaders/terrain_preview.shader", "core/shaders/bark.shader", "core/shaders/foliage.shader",
		"mount/quake/Assets/shaders/simple_color.shader", "mount/quake/Assets/shaders/quake_sky.shader", "mount/quake/Assets/shaders/quake_water.shader",
		"mount/goldsrc/Assets/shaders/goldsrc.shader", "mount/se3/Assets/shaders/se3.shader",
	];

	/// <summary>
	/// <c>-convert-shaders</c>: each convertible <c>.shader</c> through <see cref="ShaderConverter"/> into
	/// <c>converted/</c> in the results folder, compiled, and drawn beside the <c>.shader</c> (where the device can load it)
	/// with the rope's textures in every texture and both sides' defaults. A converted surface that compiles is also
	/// written beside its <c>.shader</c> with <c>-convert-shaders-write</c>.
	/// </summary>
	static void ConvertShaders( BackendDevice device, string folder, List<string> lines )
	{
		var game = Path.GetDirectoryName( device.Content.Roots[0] );
		var output = Path.Combine( folder, "converted" );
		Directory.CreateDirectory( output );

		// -convert-shaders-all: every .shader in core/shaders as well
		var candidates = Convertible.ToList();
		if ( Args.Has( "-convert-shaders-all" ) )
		{
			foreach ( var file in Directory.GetFiles( Path.Combine( game, "core", "shaders" ), "*.shader", SearchOption.AllDirectories ).Order( StringComparer.OrdinalIgnoreCase ) )
			{
				var relative = Path.GetRelativePath( game, file ).Replace( '\\', '/' );
				if ( !candidates.Contains( relative, StringComparer.OrdinalIgnoreCase ) ) candidates.Add( relative );
			}
		}

		foreach ( var relative in candidates )
		{
			try
			{
				ConvertShader( device, game, output, relative, lines );
			}
			catch ( Exception e )
			{
				lines.Add( $"convert {Path.GetFileNameWithoutExtension( relative )}: failed: {e.GetType().Name}: {e.Message.Split( '\n' )[0]}" );
			}
		}
	}

	/// <summary>
	/// <c>-convert-materials complex:materials/a.vmat,materials/b.vmat</c>: each <c>.vmat</c> drawn as its <c>.shader</c> and
	/// through the surface standing in for that shader (<c>shaders/complex.slang</c>), each on a device of its own, into
	/// <c>materials/</c> in the results folder. <c>-convert-content</c> adds a package folder the materials are in.
	/// </summary>
	static void ConvertMaterials( Basalt.Gpu.Device gpu, string spec, string folder, List<string> lines )
	{
		var shader = spec.Split( ':', 2 )[0];
		var output = Path.Combine( folder, "materials" );
		Directory.CreateDirectory( output );
		var content = Args.String( "-convert-content" );

		Color32[] Draw( string path, bool convert, out bool converted )
		{
			using var device = new BackendDevice( gpu );
			if ( content is not null ) device.Content.Add( Path.GetFullPath( content ) );
			device.Surfaces.Error = m => Console.WriteLine( $"Shader: {m}" );
			device.Surfaces.ConvertShaders = false;   // the reference is the .shader route
			if ( convert )
			{
				device.Surfaces.AddFolder( Path.Combine( device.Content.Roots[0], "shaders" ) );
				device.Surfaces.Convert( $"shaders/{shader}.shader", $"{shader}.slang" );
			}

			RenderHost.Current = new BackendRenderHost( device );

			// A model (.vmdl): its own meshes and materials, in a field of instances seen at a grazing angle
			if ( path.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) )
			{
				var model = device.LoadModel( path ) ?? throw new FileNotFoundException( $"{path} isn't in the content" );
				converted = model.Draws.Any( d => d.Material is SurfaceMaterial );
				return RenderModel( device, model );
			}

			var material = ((Basalt.GpuDevice)device).LoadMaterial( path );
			converted = material is SurfaceMaterial;

			// Alpha-tested materials' draws flagged as a compiled model's are, so depth passes keep the pixel shader that clips
			return Render( device, material, alphaTest: material.IsAlphaTest );
		}

		foreach ( var path in spec.Split( ':', 2 )[1].Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
		{
			var name = Path.GetFileNameWithoutExtension( path );
			var a = Draw( path, false, out _ );
			var b = Draw( path, true, out var converted );
			if ( !converted )
			{
				lines.Add( $"material {path}: not converted (a feature {shader}.slang doesn't do)" );
				continue;
			}

			var (mean, max, over, diff) = Compare( a, b );
			Save( a, Path.Combine( output, name + "-shader.png" ) );
			Save( b, Path.Combine( output, name + "-surface.png" ) );
			Save( diff, Path.Combine( output, name + "-diff.png" ) );
			lines.Add( $"material {path}: surface against .shader: mean difference {mean:F3}, max {max}, {over} pixels over 8" );
		}
	}

	/// <summary>
	/// A model's instances in a 7 by 7 field on a grey ground, under the sun, seen low and from afar, as Construct's
	/// scattered props are; varied in yaw, some mirrored.
	/// </summary>
	static Color32[] RenderModel( BackendDevice device, RenderMesh model )
	{
		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( -0.45f, 0.55f, -0.7f ).Normal;
		world.Lighting.SunColor = new Color( 1.3f, 1.2f, 1.05f );
		world.Lighting.AmbientColor = new Color( 0.15f, 0.17f, 0.2f, 1 );
		world.Lighting.SunShadowCascades = 2;

		var ground = ((Basalt.GpuDevice)device).CreateMaterial( "Ground", "shaders/complex.shader" );
		world.Add( new MeshObject( Box( device, new Vector3( 1200, 1200, 8 ), ground, "Ground", uvScale: 1 / 100f ), new Transform( new Vector3( 0, 0, -4 ) ) ) );
		var size = Math.Max( model.Bounds.Size.Length, 1 );
		for ( int i = 0; i < 49; i++ )
		{
			var scale = 60 / size;
			var mirror = i % 3 == 0 ? -1 : 1;
			var transform = new Transform( new Vector3( (i % 7 - 3) * 70, (i / 7 - 3) * 70, 0 ), Rotation.FromYaw( i * 47 ), new Vector3( scale * mirror, scale, scale ) );
			world.Add( new MeshObject( model, transform ) );
		}

		var view = new RenderView { Name = "Surface parity model", Time = 12.5f, FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.1f, 0.12f, 0.16f ), Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -420, 0, 60 );
		view.Rotation = Rotation.LookAt( new Vector3( 0, 0, 0 ) - view.Position );

		using var system = new RenderSystem { ParallelRecording = false };
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface parity model" );
		for ( int frame = 0; frame < Frames; frame++ )
		{
			device.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		return ModelParity.ReadBack( device, target, Width, Height );
	}

	static void ConvertShader( BackendDevice device, string game, string output, string relative, List<string> lines )
	{
		const string color = "materials/default/rope_color_png_95bee33c.generated.vtex";
		const string normal = "materials/default/rope_normal_png_b8960e44.generated.vtex";
		const string rma = "materials/default/rope01_vmat_g_trma_bbf6a2a.generated.vtex";
		var source = Path.Combine( game, relative );
		var name = Path.GetFileNameWithoutExtension( relative );
		if ( !File.Exists( source ) )
		{
			lines.Add( $"convert {name}: no {relative}" );
			return;
		}

		var includes = Path.Combine( device.Content.Roots[0], "shaders" );
		var result = ShaderConverter.Convert( File.ReadAllText( source ), name, include => File.Exists( Path.Combine( includes, include ) ) ? File.ReadAllText( Path.Combine( includes, include ) ) : null );
		if ( result.Source is null )
		{
			lines.Add( $"convert {name}: not converted ({string.Join( "; ", result.Notes )})" );
			return;
		}

		var converted = Path.Combine( output, name + ".slang" );
		File.WriteAllText( converted, result.Source.Replace( "\n", "\r\n" ) );
		var compiled = device.Surfaces.Compile( converted );
		if ( compiled is null )
		{
			lines.Add( $"convert {name}: doesn't compile (see the log); notes: {string.Join( "; ", result.Notes )}" );
			return;
		}

		if ( Args.Has( "-convert-shaders-write" ) ) File.WriteAllText( Path.ChangeExtension( source, ".slang" ), result.Source.Replace( "\n", "\r\n" ) );
		File.WriteAllBytes( Path.Combine( output, name + "_surface_ps.spv" ), compiled.Pass( ShaderPasses.Forward ).Pixel.Spirv );

		// Every texture the rope's, both sides' values their defaults
		var textures = new Dictionary<string, string>();
		foreach ( Match texture in Regex.Matches( File.ReadAllText( source ), @"Texture2D\s+(g_t\w+)" ) ) textures[texture.Groups[1].Value] = color;
		textures["g_tColor"] = color;
		textures["g_tNormal"] = normal;
		textures["g_tRma"] = rma;

		// A mount's shaders load from its Assets folder, added to the content for them
		var shaderPath = relative.StartsWith( "core/", StringComparison.Ordinal ) ? relative["core/".Length..] : relative;
		if ( relative.IndexOf( "/Assets/", StringComparison.Ordinal ) is var assets and > 0 )
		{
			var root = Path.Combine( game, relative[..assets] + "/Assets" );
			if ( !device.Content.Roots.Contains( Path.GetFullPath( root ), StringComparer.OrdinalIgnoreCase ) ) device.Content.Add( Path.GetFullPath( root ) );
			shaderPath = relative[(assets + "/Assets/".Length)..];
		}
		var classic = (BackendMaterial)((Basalt.GpuDevice)device).CreateMaterial( $"{name} (.shader)", shaderPath );
		if ( classic.Shader is null )
		{
			lines.Add( $"convert {name}: compiles ({compiled.Passes.Count} passes); its .shader isn't loadable here, not compared; notes: {string.Join( "; ", result.Notes )}" );
			return;
		}

		foreach ( var (variable, path) in textures ) classic.SetTexture( variable, (BackendTexture)device.LoadTexture( path ) );

		// The features the surface reads as fields on, on both sides (bark's sway)
		var features = new Dictionary<string, float[]>();
		foreach ( var field in compiled.Material.Fields )
		{
			if ( field.Source is not { } feature || !feature.StartsWith( "F_", StringComparison.Ordinal ) || classic.Shader.FeatureIndex( feature ) is not (>= 0 and var index) ) continue;
			classic.Features[index] = 1;
			features[feature] = [1];
		}

		using var surface = device.Surfaces.CreateMaterial( converted, textures, features, $"{name} (surface)" ) as SurfaceMaterial;
		if ( surface is null ) return;

		var a = Render( device, classic );
		var b = Render( device, surface );
		var (mean, max, over, diff) = Compare( a, b );
		Save( a, Path.Combine( output, name + "-shader.png" ) );
		Save( b, Path.Combine( output, name + "-surface.png" ) );
		Save( diff, Path.Combine( output, name + "-diff.png" ) );
		var forwardMode = classic.Shader.Modes.FirstOrDefault( m => m.Name.Contains( "Forward", StringComparison.OrdinalIgnoreCase ) )?.Name ?? "Forward";
		if ( classic.Variant( device.Shaders, forwardMode, "Forward;convert", _ => null, _ => null )?.Pixel?.Bytecode is { } bytecode && (bytecode.Spirv ?? bytecode.Shared?.Spirv) is { } spirv )
			File.WriteAllBytes( Path.Combine( output, name + "_shader_ps.spv" ), spirv );
		lines.Add( $"convert {name}: surface against .shader: mean difference {mean:F3}, max {max}, {over} pixels over 8 (reported, not gated){(result.Notes.Count > 0 ? "; notes: " + string.Join( "; ", result.Notes ) : "")}" );
	}

	static int Fail( string message )
	{
		Console.WriteLine( $"Surface parity: {message}" );
		return 1;
	}

	/// <summary>Frames rendered before the one compared (<c>-frames</c>, default 8: shadows and exposure settle).</summary>
	static int Frames => Math.Max( Args.Int( "-frames", 8 ), 1 );

	/// <summary>
	/// A ground, a sphere and a box with <paramref name="material"/>, under a sun and a point light; their draws flagged
	/// alpha-tested when asked, as a compiled model's are, so depth passes keep the pixel shader that clips. Without
	/// <paramref name="sun"/> the sun is black, so the point light alone lights the scene.
	/// </summary>
	static Color32[] Render( BackendDevice device, GpuMaterial material, bool alphaTest = false, int samples = 1, bool sun = true )
	{
		RenderMesh Flag( RenderMesh mesh ) => alphaTest ? new RenderMesh( mesh.Geometry, mesh.Bounds, [[mesh.DrawsForLod( 0 )[0] with { AlphaTest = true }]] ) : mesh;

		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( -0.45f, 0.55f, -0.7f ).Normal;
		world.Lighting.SunColor = sun ? new Color( 1.3f, 1.2f, 1.05f ) : new Color( 0, 0, 0 );
		world.Lighting.AmbientColor = new Color( 0.15f, 0.17f, 0.2f, 1 );
		world.Lighting.SunShadowCascades = 2;

		world.Add( new MeshObject( Flag( Box( device, new Vector3( 600, 600, 8 ), material, "Ground", uvScale: 1 / 100f ) ), new Transform( new Vector3( 0, 0, -4 ) ) ) );
		world.Add( new MeshObject( Flag( Sphere( device, 60, material, "Sphere" ) ), new Transform( new Vector3( 0, -70, 60 ) ) ) );
		world.Add( new MeshObject( Flag( Box( device, new Vector3( 70, 70, 70 ), material, "Box" ) ), new Transform( new Vector3( 20, 90, 35 ), Rotation.FromYaw( 30 ) ) ) );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 120, 0, 90 ) ) ) { Color = new Color( 3, 1.5f, 0.8f ), Radius = 260, CastShadows = false } );

		// A fixed shader time, so time-animated shaders (water, sky) draw the same frame on both sides
		var view = new RenderView { Name = "Surface parity", Time = 12.5f, FieldOfView = 60, ZNear = 4, ZFar = 3000, ClearColor = new Color( 0.1f, 0.12f, 0.16f ), Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -330, 80, 200 );
		view.Rotation = Rotation.LookAt( new Vector3( 0, 0, 30 ) - view.Position );

		using var system = new RenderSystem { ParallelRecording = false };
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), samples, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface parity" );
		for ( int frame = 0; frame < Frames; frame++ )
		{
			device.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		return ModelParity.ReadBack( device, target, Width, Height );
	}

	static (double Mean, int Max, int Over, Color32[] Diff) Compare( Color32[] a, Color32[] b )
	{
		var diff = new Color32[a.Length];
		long total = 0;
		int max = 0, over = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			var dr = Math.Abs( a[i].r - b[i].r );
			var dg = Math.Abs( a[i].g - b[i].g );
			var db = Math.Abs( a[i].b - b[i].b );
			total += dr + dg + db;
			var d = Math.Max( dr, Math.Max( dg, db ) );
			max = Math.Max( max, d );
			if ( d > 8 ) over++;
			var v = (byte)Math.Min( 255, d * 8 );
			diff[i] = new Color32( v, v, v, 255 );
		}

		return (total / (3.0 * a.Length), max, over, diff);
	}

	static int Changed( Color32[] a, Color32[] b )
	{
		var changed = 0;
		for ( int i = 0; i < a.Length; i++ )
			if ( Math.Abs( a[i].r - b[i].r ) + Math.Abs( a[i].g - b[i].g ) + Math.Abs( a[i].b - b[i].b ) > 3 ) changed++;
		return changed;
	}

	static void Save( Color32[] pixels, string path )
	{
		var rgba = new byte[pixels.Length * 4];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			rgba[i * 4] = pixels[i].r;
			rgba[i * 4 + 1] = pixels[i].g;
			rgba[i * 4 + 2] = pixels[i].b;
			rgba[i * 4 + 3] = 255;
		}

		File.WriteAllBytes( path, BasaltGpuSample.Capture.Png( rgba, Width, Height ) );
	}

	/// <summary>A box centred on its origin, counter-clockwise from outside; uvs per face 0..1, or its size times <paramref name="uvScale"/>.</summary>
	static RenderMesh Box( BackendDevice device, Vector3 size, GpuMaterial material, string name, float uvScale = 0 )
	{
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		foreach ( var n in normals )
		{
			var u = MathF.Abs( n.z ) > 0.5f ? Vector3.Forward : Vector3.Cross( Vector3.Up, n );
			var v = Vector3.Cross( n, u );
			var first = (uint)vertices.Count;
			var su = uvScale > 0 ? MathF.Abs( Vector3.Dot( u, size ) ) * uvScale : 1;
			var sv = uvScale > 0 ? MathF.Abs( Vector3.Dot( v, size ) ) * uvScale : 1;
			vertices.Add( new MeshVertex( (n - u - v) * 0.5f * size, n, new Vector2( 0, sv ) ) );
			vertices.Add( new MeshVertex( (n + u - v) * 0.5f * size, n, new Vector2( su, sv ) ) );
			vertices.Add( new MeshVertex( (n + u + v) * 0.5f * size, n, new Vector2( su, 0 ) ) );
			vertices.Add( new MeshVertex( (n - u + v) * 0.5f * size, n, new Vector2( 0, 0 ) ) );
			indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3] );
		}

		return BackendMesh.Create( device, vertices.ToArray(), indices.ToArray(), material, name );
	}

	/// <summary>A UV sphere, counter-clockwise from outside, its texture wrapped twice around.</summary>
	static RenderMesh Sphere( BackendDevice device, float radius, GpuMaterial material, string name )
	{
		const int rings = 24, segments = 48;
		var vertices = new List<MeshVertex>();
		var indices = new List<uint>();
		for ( int r = 0; r <= rings; r++ )
		{
			var phi = r / (float)rings * MathF.PI;
			for ( int s = 0; s <= segments; s++ )
			{
				var theta = s / (float)segments * MathF.Tau;
				var n = new Vector3( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Sin( phi ) * MathF.Sin( theta ), MathF.Cos( phi ) );
				vertices.Add( new MeshVertex( n * radius, n, new Vector2( s / (float)segments * 2, r / (float)rings ) ) );
			}
		}

		for ( int r = 0; r < rings; r++ )
		{
			for ( int s = 0; s < segments; s++ )
			{
				var a = (uint)(r * (segments + 1) + s);
				var b = a + segments + 1;
				indices.AddRange( [a, b + 1, a + 1, a, b, b + 1] );
			}
		}

		return BackendMesh.Create( device, vertices.ToArray(), indices.ToArray(), material, name );
	}
}
