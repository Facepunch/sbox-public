using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Material shaders drawn from their pass modules draw the picture their compiled <c>.shader_c</c> draws (a device with
/// <see cref="BackendDevice.SlangPasses"/> off): each on a lit box, and the sky behind it. Shaders whose objects only the
/// game makes (sprites, water bodies, terrain, the underwater pass) are checked there.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PassModulePictureTests : RenderGpuTest
{
	const int Width = 320, Height = 200;

	[TestMethod]
	public void MaterialModulesDrawAsTheirShaders()
	{
		// error.shader has only its Default mode, which mesh draws don't use
		string[] shaders = ["shaders/line.shader", "shaders/eyeball.shader", "shaders/terrain_brush.shader", "shaders/lpv_debug.shader"];
		var differences = new List<string>();
		var compared = 0;

		// A device for each route, shared by the shaders: each made its own, with every pipeline of the frame made again
		using var classicBackend = new BackendDevice( Device ) { SlangPasses = false };
		using var moduleBackend = new BackendDevice( Device ) { SlangPasses = true };
		foreach ( var shader in shaders )
		{
			var classic = Draw( classicBackend, backend => Textured( backend, shader ), shader );
			var module = Draw( moduleBackend, backend => Textured( backend, shader ), shader );
			if ( classic is null || module is null )
			{
				TestContext.WriteLine( $"{shader}: not compiled" );
				continue;
			}

			compared++;
			var name = Path.GetFileNameWithoutExtension( shader );
			SaveRgb( classic, $"module-{name}-shader" );
			SaveRgb( module, $"module-{name}-module" );
			var mean = Mean( classic, module );
			TestContext.WriteLine( $"{name}: {mean:F3}" );
			if ( mean > 0.5 ) differences.Add( $"{name} differs by {mean:F3}" );
		}

		if ( compared == 0 ) Assert.Inconclusive( "None of the shaders is compiled" );
		Assert.AreEqual( 0, differences.Count, string.Join( "\n", differences ) );
	}

	/// <summary>A material of <paramref name="shader"/> with the rope's textures in its colour, normal and mask slots.</summary>
	static GpuMaterial Textured( BackendDevice backend, string shader )
	{
		var material = (BackendMaterial)((GpuDevice)backend).CreateMaterial( shader, shader );
		var color = (BackendTexture)backend.LoadTexture( Fixtures.Rope.Color );
		var normal = (BackendTexture)backend.LoadTexture( Fixtures.Rope.Normal );
		var rma = (BackendTexture)backend.LoadTexture( Fixtures.Rope.Rma );
		foreach ( var name in new[] { "g_tColor", "g_tSelfIllumMask" } ) material.SetTexture( name, color );
		foreach ( var name in new[] { "g_tNormal", "g_tIris" } ) material.SetTexture( name, normal );
		foreach ( var name in new[] { "g_tRma", "g_tIrisMask", "g_tOcclusion" } ) material.SetTexture( name, rma );
		return material;
	}

	/// <summary>The sky material (sky.shader) behind a box, from its module and from its compiled programs.</summary>
	[TestMethod]
	public void SkyModuleDrawsAsItsShader()
	{
		const string sky = "materials/skybox/skybox_day_01.vmat";
		var classic = Draw( slangPasses: false, backend => backend.CreateMaterial( "Box", Color.White ), sky: sky );
		var module = Draw( slangPasses: true, backend => backend.CreateMaterial( "Box", Color.White ), sky: sky );
		if ( classic is null || module is null ) Assert.Inconclusive( "No compiled sky material" );
		SaveRgb( classic, "module-sky-shader" );
		SaveRgb( module, "module-sky-module" );
		var mean = Mean( classic, module );
		TestContext.WriteLine( $"sky: {mean:F3}" );
		Assert.IsTrue( mean < 0.5, $"sky differs by {mean:F3}" );
	}

	/// <summary>
	/// A box in <paramref name="material"/> lit by the sun, with an optional sky, as 8-bit gamma bytes, on a device of its
	/// own; null when the material's shader (<paramref name="shader"/>, when given) or the sky's isn't compiled.
	/// </summary>
	byte[] Draw( bool slangPasses, Func<BackendDevice, GpuMaterial> material, string shader = null, string sky = null )
	{
		using var backend = new BackendDevice( Device ) { SlangPasses = slangPasses };
		return Draw( backend, material, shader, sky );
	}

	/// <summary>The same picture on <paramref name="backend"/>, drawn by a render system and a target of its own.</summary>
	byte[] Draw( BackendDevice backend, Func<BackendDevice, GpuMaterial> material, string shader = null, string sky = null )
	{
		var errors = new List<string>();
		backend.Passes.Error = errors.Add;
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			// The box's material is what's compared when a shader is named; else it's only what the sky is behind (a surface)
			var made = material( backend );
			if ( shader is not null && (made is not BackendMaterial { Shader: not null } loaded || !loaded.Shader.Name.EndsWith( Path.GetFileNameWithoutExtension( shader ) + ".shader_c", StringComparison.OrdinalIgnoreCase )) ) return null;
			if ( made is null ) return null;
			GpuMaterial skyMaterial = null;
			if ( sky is not null && (skyMaterial = backend.LoadMaterial( sky )) is not BackendMaterial { Shader: not null } ) return null;

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Pass module picture" );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.5f, -0.75f ).Normal;
			world.Lighting.SunShadowCascades = 1;
			if ( skyMaterial is not null ) world.Lighting.SkyMaterial = skyMaterial;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, made ), new Transform( Vector3.Zero, Rotation.FromYaw( 35 ), 2 ) ) );

			var view = new RenderView { Name = "Pass module picture", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 0.2f, 0.2f, 0.2f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -440, 120, 180 );
			view.Rotation = Rotation.LookAt( -view.Position );

			for ( int frame = 0; frame < 3; frame++ )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
			}

			Device.WaitIdle();
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
			// Draws of passes a shader has no mode for (terrain_brush and lpv_debug have no depth mode) don't draw, on either
			// route; the pictures are what's compared
			var (texture, layout) = BackendDevice.Unwrap( target.Color );
			var pixels = ReadBack( texture, layout );
			BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );

			var bytes = new byte[Width * Height * 3];
			for ( int i = 0; i < bytes.Length; i++ ) bytes[i] = (byte)Math.Clamp( MathF.Pow( Math.Max( (float)BitConverter.ToHalf( pixels, (i / 3 * 4 + i % 3) * 2 ), 0 ), 1 / 2.2f ) * 255 + 0.5f, 0, 255 );
			return bytes;
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
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
}
