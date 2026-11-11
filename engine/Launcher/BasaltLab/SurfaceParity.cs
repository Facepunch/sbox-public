using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Sandbox.BasaltLab.Surfaces;
using Sandbox.ShaderCompiler;
using System.IO;
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

		using var device = new BackendDevice( gpu );
		var surfaces = Folder( device );
		device.Surfaces.AddFolder( surfaces );
		device.Surfaces.Error = m => Console.WriteLine( $"Shader: {m}" );

		// Compiled up front, so the times are the compiler's and a failure is reported once
		var lines = new List<string>();
		foreach ( var file in new[] { "rock.slang", "rock_raw.slang", "foliage.slang" } )
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

			foreach ( var (name, material) in new (string, GpuMaterial)[] { ("surface", surface), ("raw", raw) } )
			{
				var pixels = Render( device, material );
				Save( pixels, Path.Combine( folder, $"{name}.png" ) );
				var (mean, max, over, diff) = Compare( reference, pixels );
				Save( diff, Path.Combine( folder, $"{name}-diff.png" ) );
				var ok = mean <= tolerance;
				if ( !ok ) failed++;
				lines.Add( $"{name} against .shader: mean difference {mean:F3}, max {max}, {over} pixels over 8{(ok ? "" : " FAIL")}" );
			}

			// The three forward pixel shaders, for register counts (amdllpc)
			var variant = shader.Variant( device.Shaders, "Forward", "Forward", _ => null, _ => null );
			if ( variant?.Pixel?.Bytecode is { } bytecode && (bytecode.Spirv ?? bytecode.Shared?.Spirv) is { } classic ) File.WriteAllBytes( Path.Combine( folder, "shader_ps.spv" ), classic );
			File.WriteAllBytes( Path.Combine( folder, "surface_ps.spv" ), device.Surfaces.Compile( "rock.slang" ).Pass( ShaderPasses.Forward ).Pixel.Spirv );
			File.WriteAllBytes( Path.Combine( folder, "surface_depth_alphatest_ps.spv" ), device.Surfaces.Compile( "rock.slang" ).Pass( ShaderPasses.DepthAlphaTest ).Pixel.Spirv );
			File.WriteAllBytes( Path.Combine( folder, "raw_ps.spv" ), device.Surfaces.Compile( "rock_raw.slang" ).Pass( ShaderPasses.Forward ).Pixel.Spirv );

			// foliage.shader (alpha test, translucency and wind animation on; sway off, so the picture doesn't depend on
			// the time) against the same material converted by hand to a surface
			var cutOut = (BackendMaterial)device.LoadMaterial( "materials/dev/simple/simple_rail_32.vmat" );
			cutOut.Textures.TryGetValue( "g_tColor", out var leaf );
			var foliageShader = (BackendMaterial)((Basalt.GpuDevice)device).CreateMaterial( "Foliage (.shader)", "shaders/foliage.shader" );
			if ( leaf is null || foliageShader.Shader?.Name.Contains( "foliage", StringComparison.OrdinalIgnoreCase ) != true ) return Fail( "foliage.shader or simple_rail_32.vmat isn't compiled" );
			foreach ( var feature in new[] { "F_ALPHA_TEST", "F_TRANSMISSIVE", "F_FOLIAGE_ANIMATION" } ) foliageShader.Features[foliageShader.Shader.FeatureIndex( feature )] = 1;
			foliageShader.SetTexture( "g_tColor", leaf );
			foliageShader.SetTexture( "g_tNormal", (BackendTexture)normal );
			foliageShader.SetTexture( "g_tRma", (BackendTexture)rma );
			foliageShader.SetTexture( "g_tTransmissiveColor", leaf );
			foliageShader.Set( "g_flSwayStrength", [0] );
			foliageShader.Set( "g_flTransmissionScale", [1] );

			using var foliage = new Foliage { Color = leaf, Normal = normal, Rma = rma, TransmissiveColor = leaf, TransmissionScale = 1, SwayStrength = 0, AlphaTest = true, DisplayName = "Foliage (surface)" };
			var foliageReference = Render( device, foliageShader, alphaTest: true );
			Save( foliageReference, Path.Combine( folder, "foliage-shader.png" ) );
			var foliagePixels = Render( device, foliage, alphaTest: true );
			Save( foliagePixels, Path.Combine( folder, "foliage-surface.png" ) );
			var (fmean, fmax, fover, fdiff) = Compare( foliageReference, foliagePixels );
			Save( fdiff, Path.Combine( folder, "foliage-surface-diff.png" ) );
			lines.Add( $"foliage surface against foliage.shader: mean difference {fmean:F3}, max {fmax}, {fover} pixels over 8 (reported, not gated: alpha-test edges differ by design)" );
			File.WriteAllBytes( Path.Combine( folder, "foliage_surface_ps.spv" ), device.Surfaces.Compile( "foliage.slang" ).Pass( ShaderPasses.Forward + "AlphaTest" ).Pixel.Spirv );
			var foliageVariant = foliageShader.Variant( device.Shaders, "Forward", "Forward;F", _ => null, _ => null );
			if ( foliageVariant?.Pixel?.Bytecode is { } fb && (fb.Spirv ?? fb.Shared?.Spirv) is { } fs ) File.WriteAllBytes( Path.Combine( folder, "foliage_shader_ps.spv" ), fs );
		}
		finally
		{
			gpu.WaitIdle();
			RenderHost.Current = previous;
		}

		lines.Add( $"Surface parity: {(failed == 0 ? "pass" : $"{failed} failed")} ({folder}); Basalt.Gpu validation errors {gpu.ValidationErrors}" );
		foreach ( var line in lines ) Console.WriteLine( line );
		File.WriteAllLines( Path.Combine( folder, "results.txt" ), lines );
		return failed == 0 && gpu.ValidationErrors == 0 ? 0 : 1;
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
	/// alpha-tested when asked, as a compiled model's are, so depth passes keep the pixel shader that clips.
	/// </summary>
	static Color32[] Render( BackendDevice device, GpuMaterial material, bool alphaTest = false )
	{
		RenderMesh Flag( RenderMesh mesh ) => alphaTest ? new RenderMesh( mesh.Geometry, mesh.Bounds, [[mesh.DrawsForLod( 0 )[0] with { AlphaTest = true }]] ) : mesh;

		var world = new RenderWorld();
		world.Lighting.SunDirection = new Vector3( -0.45f, 0.55f, -0.7f ).Normal;
		world.Lighting.SunColor = new Color( 1.3f, 1.2f, 1.05f );
		world.Lighting.AmbientColor = new Color( 0.15f, 0.17f, 0.2f, 1 );
		world.Lighting.SunShadowCascades = 2;

		world.Add( new MeshObject( Flag( Box( device, new Vector3( 600, 600, 8 ), material, "Ground", uvScale: 1 / 100f ) ), new Transform( new Vector3( 0, 0, -4 ) ) ) );
		world.Add( new MeshObject( Flag( Sphere( device, 60, material, "Sphere" ) ), new Transform( new Vector3( 0, -70, 60 ) ) ) );
		world.Add( new MeshObject( Flag( Box( device, new Vector3( 70, 70, 70 ), material, "Box" ) ), new Transform( new Vector3( 20, 90, 35 ), Rotation.FromYaw( 30 ) ) ) );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 120, 0, 90 ) ) ) { Color = new Color( 3, 1.5f, 0.8f ), Radius = 260, CastShadows = false } );

		var view = new RenderView { Name = "Surface parity", FieldOfView = 60, ZNear = 4, ZFar = 3000, ClearColor = new Color( 0.1f, 0.12f, 0.16f ), Viewport = new Rect( 0, 0, Width, Height ) };
		view.Position = new Vector3( -330, 80, 200 );
		view.Rotation = Rotation.LookAt( new Vector3( 0, 0, 30 ) - view.Position );

		using var system = new RenderSystem { ParallelRecording = false };
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Surface parity" );
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
