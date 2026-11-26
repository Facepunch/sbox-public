using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Sandbox;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Content the engine finds on the rest of its GAME search path (<see cref="ContentFiles.Fallbacks"/>): the
/// <c>sbox_props</c> nature models compiled for the menu (<c>addons/menu/Transients</c>), which games load by path, draw.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MenuContentTests : RenderGpuTest
{
	const int Width = 160, Height = 160;

	/// <summary>
	/// Every <c>models/sbox_props/nature</c> model the menu's compiled files have (tall grass, grass clumps) loads from the
	/// game's content and draws pixels.
	/// </summary>
	[TestMethod]
	public void MenuNatureModelsDraw()
	{
		var content = ContentFiles.FindGame();
		var transients = content.Fallbacks.FirstOrDefault( x => x.EndsWith( Path.Combine( "menu", "Transients" ), StringComparison.OrdinalIgnoreCase ) );
		var nature = transients is null ? null : Path.Combine( transients, "models", "sbox_props", "nature" );
		if ( nature is null || !Directory.Exists( nature ) ) Assert.Inconclusive( "The menu's compiled sbox_props nature models aren't built" );

		var models = Directory.EnumerateFiles( nature, "*.vmdl_c", SearchOption.AllDirectories )
			.Select( x => Path.GetRelativePath( transients, x ).Replace( '\\', '/' )[..^2] )
			.Order( StringComparer.OrdinalIgnoreCase )
			.ToArray();
		CollectionAssert.Contains( models, "models/sbox_props/nature/tallgrass/tallgrass_c.vmdl" );

		using var backend = new BackendDevice( Device, content );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Menu content test" );
			foreach ( var path in models )
			{
				var mesh = backend.LoadModel( path );
				Assert.IsNotNull( mesh, $"{path} loads from the game's content" );

				var world = new RenderWorld();
				world.Lighting.SunShadowCascades = 0;
				world.Add( new MeshObject( mesh, Transform.Zero ) );

				var view = new RenderView { Name = "Menu content test", FieldOfView = 60, ClearColor = new Color( 1, 0, 1 ), Viewport = new Rect( 0, 0, Width, Height ) };
				var size = mesh.Bounds.Size.Length;
				view.Rotation = Rotation.From( 15, 30, 0 );
				view.Position = mesh.Bounds.Center - view.Rotation.Forward * size * 1.2f;
				view.ZNear = size * 0.05f;
				view.ZFar = size * 10;

				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );

				// Pixels (RGBA16F) that aren't the magenta clear
				var drawn = 0;
				for ( int i = 0; i + 8 <= pixels.Length; i += 8 )
				{
					var r = (float)BitConverter.ToHalf( pixels, i );
					var g = (float)BitConverter.ToHalf( pixels, i + 2 );
					var b = (float)BitConverter.ToHalf( pixels, i + 4 );
					if ( MathF.Abs( r - 1 ) + g + MathF.Abs( b - 1 ) > 0.05f ) drawn++;
				}

				Assert.IsTrue( drawn > Width * Height / 200, $"{path} drew {drawn} pixels" );
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
