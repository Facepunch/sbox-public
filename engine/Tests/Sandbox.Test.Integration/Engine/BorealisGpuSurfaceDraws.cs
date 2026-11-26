using Sandbox.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EngineTests;

/// <summary>
/// The engine's own drawing on Borealis.Gpu (<c>Graphics.DrawModel</c>, <c>Graphics.Draw</c> with
/// vertices or a vertex buffer, a command list's draws, all from a custom object) with materials whose shader is a surface
/// alone, with no compiled <c>.shader</c>: standard's (complex, simple and generic stand for it), a material made in code
/// from it with values and a texture set, and the held surfaces' (skin, glass, blendable, vertex_color). Each draws what
/// the scene draws for the same model and material. With <c>SBOX_TEST_PICTURES</c> set, each frame is saved there by
/// name, to compare with the same test on a build that still draws these materials from their <c>.shader</c>.
/// </summary>
[TestClass]
public class BorealisGpuSurfaceDrawsTest
{
	const int Size = 96;

	static readonly Transform Left = new( new Vector3( 0, 40, 0 ) );
	static readonly Transform Right = new( new Vector3( 0, -40, 0 ) );

	/// <summary>A model drawn by the scene on the left and by a custom object's <paramref name="draw"/> on the right: the halves match.</summary>
	[TestMethod]
	[DataRow( "DrawModel" )]
	[DataRow( "CommandList" )]
	public void CustomModelDrawsMatchTheScene( string path )
	{
		TestGraphics.Require();
		var model = Model.Load( "models/dev/box.vmdl" );
		Assert.IsFalse( model.IsError, "models/dev/box.vmdl" );

		var world = new SceneWorld();
		try
		{
			Light( world );
			_ = new SceneModel( world, model, Left );
			var commands = new CommandList();
			commands.DrawModel( model, Right );
			var custom = new SceneCustomObject( world ) { Transform = Right, Bounds = model.Bounds.Transform( Right ) };
			custom.RenderOverride = _ =>
			{
				switch ( path )
				{
					case "DrawModel": Graphics.DrawModel( model, Right ); break;
					case "CommandList": commands.Execute(); break;
				}
			};

			var (left, right) = Halves( world, $"model-{path}" );
			Assert.IsTrue( left.Length > 10, $"the scene drew the box ({left})" );
			AssertClose( left, right, $"{path} draws the box as the scene does" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A quad facing the camera drawn by a custom object with a material, from CPU vertices and from a vertex buffer: it
	/// draws, not the error colour, and the two ways match. Standard's quad is lit as the scene's box face beside it is.
	/// Materials made or changed in code are drawn by the scene too (the box on the left takes them as its override), and
	/// both take what code set: a tint, a texture.
	/// </summary>
	[TestMethod]
	[DataRow( "materials/dev/primary_white.vmat" )]
	[DataRow( "made in code" )]
	[DataRow( "made skin" )]
	[DataRow( "set in code" )]
	[DataRow( "models/citizen/skin/citizen_skin.vmat" )]
	[DataRow( "models/citizen_clothes/hat/Gasmask/Textures/gasmask_glass.vmat" )]
	[DataRow( "materials/dev/simple/simple_blend.vmat" )]
	[DataRow( "materials/default/vertex_color.vmat" )]
	public void CustomVertexDrawsDrawSurfaceMaterials( string materialPath )
	{
		TestGraphics.Require();
		MountCitizen();

		// Made in code from a shader a surface alone draws (complex is standard's; skin), and a file's copy with a value set
		Material material;
		var inCode = !materialPath.Contains( '/' );
		switch ( materialPath )
		{
			case "made in code":
				material = Material.Create( "surface draws made in code", "shaders/complex.shader" );
				material.Set( "g_vColorTint", Color.Red );
				material.Set( "g_tColor", Texture.White );
				break;
			case "made skin":
				material = Material.Create( "surface draws made skin", "shaders/skin.shader" );
				material.Set( "g_tColor", Texture.White );
				break;
			case "set in code":
				material = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
				material.Set( "g_vColorTint", Color.Green );
				break;
			default:
				material = Material.Load( materialPath );
				break;
		}

		Assert.IsNotNull( material, materialPath );

		var name = inCode ? materialPath.Replace( ' ', '_' ) : Path.GetFileNameWithoutExtension( materialPath );
		var results = new List<(Vector3 Scene, Vector3 Custom)>();
		foreach ( var buffered in new[] { false, true } )
		{
			var world = new SceneWorld();
			GpuBuffer<Vertex> buffer = null;
			try
			{
				Light( world );
				var box = new SceneModel( world, Model.Load( "models/dev/box.vmdl" ), Left );
				if ( inCode ) box.SetMaterialOverride( material );
				var quad = Quad( Right.Position );
				if ( buffered ) buffer = new GpuBuffer<Vertex>( quad.Length, GpuBuffer.UsageFlags.Vertex );
				buffer?.SetData( quad );
				var custom = new SceneCustomObject( world ) { Transform = Transform.Zero, Bounds = BBox.FromPositionAndSize( Right.Position, 80 ) };
				custom.RenderOverride = _ =>
				{
					if ( buffer is not null ) Graphics.Draw( buffer, material, 0, quad.Length );
					else Graphics.Draw( quad.AsSpan(), quad.Length, material );
				};

				results.Add( Halves( world, $"quad-{name}-{(buffered ? "buffer" : "vertices")}" ) );
			}
			finally
			{
				buffer?.Dispose();
				world.Delete();
			}
		}

		var (scene, drawn) = results[0];
		Assert.IsTrue( drawn.Length > 1, $"{materialPath} drew ({drawn})" );
		Assert.IsFalse( drawn.x > 4 * drawn.y && drawn.z > 4 * drawn.y && !inCode, $"{materialPath} isn't the error colour ({drawn})" );
		AssertClose( drawn, results[1].Custom, $"{materialPath}: CPU vertices and a vertex buffer draw the same" );
		if ( inCode ) Assert.IsTrue( scene.Length > 1, $"the scene draws {materialPath} ({scene})" );

		// What code set reaches both: the tint
		if ( materialPath == "made in code" )
		{
			Assert.IsTrue( drawn.x > 3 * drawn.y && drawn.x > 3 * drawn.z, $"its tint set in code is red ({drawn})" );
			Assert.IsTrue( scene.x > 3 * scene.y && scene.x > 3 * scene.z, $"the scene's is red too ({scene})" );
		}

		if ( materialPath == "set in code" )
		{
			Assert.IsTrue( drawn.y > 3 * drawn.x && drawn.y > 3 * drawn.z, $"its tint set in code is green ({drawn})" );
			Assert.IsTrue( scene.y > 3 * scene.x && scene.y > 3 * scene.z, $"the scene's is green too ({scene})" );
		}
	}

	static void MountCitizen()
	{
		var assets = Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "addons/citizen/Assets" );
		NativeEngine.FullFileSystem.AddProjectPath( "surface-draws-citizen", Project.NativeSearchPath( assets ) );
		NativeEngine.g_pResourceSystem.InvalidateDatabase();
	}

	/// <summary>A sun from behind the camera and some ambient light, so lit surfaces show their colour.</summary>
	static void Light( SceneWorld world )
	{
		_ = new SceneDirectionalLight( world, Rotation.From( 30, 20, 0 ), Color.White * 2 );
		world.AmbientLightColor = Color.White * 0.3f;
	}

	/// <summary>A square facing the camera (towards -x), 60 units across, centred on <paramref name="centre"/>.</summary>
	static Vertex[] Quad( Vector3 centre )
	{
		Vertex Corner( float y, float z, float u, float v ) => new( centre + new Vector3( -30, y, z ), new Vector3( -1, 0, 0 ), new Vector3( 0, -1, 0 ), new Vector4( u, v, 0, 0 ) ) { Color = Color.White };
		var a = Corner( 30, 30, 0, 0 );
		var b = Corner( -30, 30, 1, 0 );
		var c = Corner( -30, -30, 1, 1 );
		var d = Corner( 30, -30, 0, 1 );
		// Both windings, so it faces the camera whichever way the engine winds front faces
		return [a, b, c, a, c, d, a, c, b, a, d, c];
	}

	/// <summary>The summed colour of each half of a frame, once textures and pipelines are in; saved by name when asked.</summary>
	static (Vector3 Left, Vector3 Right) Halves( SceneWorld world, string name )
	{
		using var camera = new SceneCamera( "surface draws" ) { World = world, Position = new Vector3( -220, 0, 0 ), Rotation = Rotation.Identity, FieldOfView = 60, BackgroundColor = Color.Black };
		using var bitmap = new Bitmap( Size, Size );
		for ( int frame = 0; frame < 12; frame++ )
		{
			System.Threading.Thread.Sleep( 20 );
			camera.RenderToBitmap( bitmap );
		}

		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_PICTURES" ) is { Length: > 0 } folder )
		{
			Directory.CreateDirectory( folder );
			File.WriteAllBytes( Path.Combine( folder, name.Replace( ' ', '_' ) + ".png" ), bitmap.ToPng() );
		}

		var pixels = bitmap.GetPixels();
		Vector3 left = default, right = default;
		for ( int y = 0; y < Size; y++ )
		{
			for ( int x = 0; x < Size; x++ )
			{
				var p = pixels[y * Size + x];
				var c = new Vector3( p.r, p.g, p.b );
				if ( x < Size / 2 ) left += c;
				else right += c;
			}
		}

		return (left, right);
	}

	static void AssertClose( Vector3 expected, Vector3 actual, string message )
	{
		var difference = (expected - actual).Length;
		Assert.IsTrue( difference <= expected.Length * 0.1f + 1, $"{message}: {actual} against {expected}" );
	}
}
