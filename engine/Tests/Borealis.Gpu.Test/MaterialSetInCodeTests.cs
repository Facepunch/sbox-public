using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Materials a game copied and changed in code, as Borealis.Gpu draws them (<c>BackendDevice.LoadMaterial</c> with values and
/// textures, <c>SetOnMaterial</c>): the compiled file's own values and textures, with what code set over them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MaterialSetInCodeTests : GpuTest
{
	/// <summary>
	/// A texture and a tint set in code replace the file's, the file's other textures stay, and a later set lands on the same
	/// material, moving its version on.
	/// </summary>
	[TestMethod]
	public void SetValuesAndTexturesGoOverTheFile()
	{
		using var backend = new BackendDevice( Device );
		const string path = "materials/default/white.vmat";
		if ( backend.Content.Find( path ) is null ) Assert.Inconclusive( $"{path} isn't built" );

		using var screen = backend.CreateTexture( new TextureDesc { Width = 4, Height = 4, Depth = 1, Format = TextureFormat.RGBA8888, Samples = 1, Mips = 1, RenderTarget = true }, "Screen" );
		using var second = backend.CreateTexture( new TextureDesc { Width = 4, Height = 4, Depth = 1, Format = TextureFormat.RGBA8888, Samples = 1, Mips = 1, RenderTarget = true }, "Second screen" );

		// The file as its .shader draws it, what code-set copies start from
		var plain = (BackendMaterial)backend.LoadMaterial( path, null, null, null );
		var set = (BackendMaterial)backend.LoadMaterial( path, new Dictionary<string, float[]> { ["g_vColorTint"] = [1, 0, 0, 1] }, new Dictionary<string, GpuTexture> { ["g_tColor"] = screen } );

		Assert.AreSame( screen, set.Textures["g_tColor"], "the texture set in code" );
		foreach ( var (name, texture) in plain.Textures )
		{
			if ( name != "g_tColor" ) Assert.AreSame( texture, set.Textures[name], $"{name} stays the file's" );
		}

		var version = set.Version;
		backend.SetOnMaterial( set, null, new Dictionary<string, GpuTexture> { ["g_tColor"] = second } );
		Assert.AreSame( second, set.Textures["g_tColor"], "a later set lands on the same material" );
		Assert.AreNotEqual( version, set.Version, "and moves its version on" );
		Assert.AreNotSame( second, plain.Textures.GetValueOrDefault( "g_tColor" ), "the file's own material is untouched" );
	}

	/// <summary>
	/// A material made in code from a shader (foliage, as <c>Material.Create</c> makes one) with the cut-out rail as its
	/// colour and alpha testing set in code (<c>SetFeature</c>) draws with the feature: it alpha tests, and a box drawn with
	/// it leaves the clear showing through the rail's holes, where the same material without the feature covers them. The
	/// textures code didn't set are native's for a raw material: the error texture.
	/// </summary>
	[TestMethod]
	public void AFeatureSetInCodeDraws()
	{
		using var backend = new BackendDevice( Device );
		const string rail = "materials/dev/simple/simple_rail_32_color_png_b600d868.generated.vtex";
		if ( backend.Content.Find( rail ) is null || backend.Content.Find( "shaders/foliage.shader" ) is null ) Assert.Inconclusive( "The rail's colour or foliage.shader isn't built" );
		var color = backend.LoadTexture( rail );
		Assert.IsNotNull( color );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( 160, 160 ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Feature set in code" );
			var textures = new Dictionary<string, GpuTexture> { ["g_tColor"] = color };

			int Covered( IReadOnlyDictionary<string, int> features, out BackendMaterial made )
			{
				made = (BackendMaterial)backend.CreateRawMaterial( "Made in code", "shaders/foliage.shader" );
				backend.SetOnMaterial( made, null, textures );
				backend.SetFeaturesOnMaterial( made, features );

				var world = new RenderWorld();
				world.Lighting.SunShadowCascades = 0;
				var box = ObjectAttributeTests.BoxMesh( backend, made );
				var draw = box.DrawsForLod( 0 )[0] with { AlphaTest = made.IsAlphaTest };
				world.Add( new MeshObject( new RenderMesh( box.Geometry, box.Bounds, [[draw]] ), Transform.Zero ) );

				var view = new RenderView { Name = "Feature set in code", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = new Color( 1, 0, 1 ), Viewport = new Rect( 0, 0, 160, 160 ) };
				view.Position = new Vector3( -200, 0, 0 );
				view.Rotation = Rotation.LookAt( Vector3.Forward );

				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );

				// Pixels (RGBA16F) that aren't the magenta clear
				var covered = 0;
				for ( int i = 0; i + 8 <= pixels.Length; i += 8 )
				{
					var r = (float)BitConverter.ToHalf( pixels, i );
					var g = (float)BitConverter.ToHalf( pixels, i + 2 );
					var b = (float)BitConverter.ToHalf( pixels, i + 4 );
					if ( MathF.Abs( r - 1 ) + g + MathF.Abs( b - 1 ) > 0.05f ) covered++;
				}

				return covered;
			}

			var solid = Covered( null, out var plain );
			var cut = Covered( new Dictionary<string, int> { ["F_ALPHA_TEST"] = 1 }, out var alphaTested );

			Assert.AreSame( color, plain.Textures["g_tColor"], "the colour set in code" );
			Assert.AreSame( backend.ErrorTexture, plain.Textures["g_tNormal"], "a texture nothing set is native's error texture" );
			Assert.IsFalse( plain.IsAlphaTest, "without the feature it doesn't alpha test" );
			Assert.IsTrue( alphaTested.IsAlphaTest, "with it set in code it does" );
			Assert.IsTrue( solid > 160 * 160 / 10, $"the box covers {solid} pixels" );
			Assert.IsTrue( cut < solid * 9 / 10, $"the rail's holes show the clear: {cut} pixels covered against {solid}" );

			// Set again later: the same material takes it back, as native reloads its static combos
			var version = alphaTested.LayoutVersion;
			backend.SetFeaturesOnMaterial( alphaTested, null );
			Assert.IsFalse( alphaTested.IsAlphaTest, "clearing the features set in code goes back to the shader's" );
			Assert.AreNotEqual( version, alphaTested.LayoutVersion, "and makes its material objects again" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
