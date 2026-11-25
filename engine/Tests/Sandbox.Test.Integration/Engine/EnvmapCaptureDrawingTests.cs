using Sandbox.Rendering.Bridge;
using System;

namespace EngineTests;

/// <summary>
/// An envmap capture spread over the game window's drawings (<see cref="SceneBridge.FacesInDrawing"/>): its faces render
/// only where the window's frame draws (<see cref="SceneBridge.DrawCaptures"/>, the render thread when the frame is handed to
/// it), not where the capture starts, and the cube they make is the one a whole capture makes.
/// </summary>
[TestClass, DoNotParallelize]
public class EnvmapCaptureDrawingTests
{
	const int Size = 64;

	[TestMethod]
	public void FacesInTheDrawingMakeTheCubeAWholeCaptureMakes()
	{
		TestGraphics.Require();
		var bridge = (SceneBridge)Sandbox.Rendering.SceneRenderer.Borealis;
		CaptureBothWays( bridge, new Vector3( 300, 60, 0 ), new Vector3( -40, 0, -300 ) );
	}

	/// <summary>
	/// A map change: the faces' render systems drew the last scene's world, which is gone; the next scene's capture makes them
	/// again for its own world rather than drawing with what they kept of the last (its freed buffers lost the device in game),
	/// and its cube is still the whole capture's.
	/// </summary>
	[TestMethod]
	public void FacesAreMadeAgainForANewWorld()
	{
		TestGraphics.Require();
		var bridge = (SceneBridge)Sandbox.Rendering.SceneRenderer.Borealis;
		var first = CaptureBothWays( bridge, new Vector3( 300, 60, 0 ), new Vector3( -40, 0, -300 ) );
		var second = CaptureBothWays( bridge, new Vector3( -200, 250, 40 ), new Vector3( 0, -300, 120 ) );
		Assert.AreNotSame( first, second );
		Assert.AreSame( second, bridge.CaptureFacesWorld, "the faces are the new world's" );
	}

	/// <summary>A scene of two boxes captured whole, then spread with its faces in the drawings; their cubes compared. The scene's world.</summary>
	static SceneWorld CaptureBothWays( SceneBridge bridge, Vector3 red, Vector3 white )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var whole = Cube();
		using var spread = Cube();
		try
		{
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;
			foreach ( var (position, material) in new[] { (red, "materials/dev/primary_red.vmat"), (white, "materials/dev/primary_white.vmat") } )
			{
				var box = scene.CreateObject().Components.Create<ModelRenderer>();
				box.Model = Model.Load( "models/dev/box.vmdl" );
				box.MaterialOverride = Material.Load( material );
				box.WorldPosition = position;
			}

			scene.GameTick( 1.0 / 60 );
			scene.PreRender();

			CubemapRendering.Render( scene.SceneWorld, whole, Transform.Zero, 1, 4096, CubemapRendering.GGXFilterType.Fast );

			// Started as the envmap scheduler starts one in the game window: no face renders yet
			var before = Pixels( spread );
			bridge.FacesInDrawing = true;
			bridge.SpreadCaptureMs = 0;
			try
			{
				CubemapRendering.Render( scene.SceneWorld, spread, Transform.Zero, 1, 4096, CubemapRendering.GGXFilterType.Fast );
			}
			finally
			{
				bridge.SpreadCaptureMs = -1;
			}

			Assert.IsTrue( bridge.CaptureSpread, "the capture waits for the drawings" );
			CollectionAssert.AreEqual( before, Pixels( spread ), "nothing rendered where the capture started" );

			// The smallest budget renders a face a drawing
			var drawings = 0;
			while ( bridge.CaptureSpread && drawings < 12 )
			{
				bridge.DrawingBudgetMs = 0.001;
				bridge.DrawCaptures();
				drawings++;
			}

			Assert.IsFalse( bridge.CaptureSpread );
			Assert.AreEqual( 6, drawings, "a face a drawing" );
			Assert.IsTrue( bridge.TakeDrawnMs() > 0, "the drawings' time is counted for the scheduler" );

			var expected = Pixels( whole );
			CollectionAssert.AreNotEqual( before, expected, "the whole capture drew something" );
			CollectionAssert.AreEqual( expected, Pixels( spread ), "every face and mip as the whole capture's" );
			Assert.AreSame( scene.SceneWorld, bridge.CaptureFacesWorld );
			return scene.SceneWorld;
		}
		finally
		{
			bridge.FacesInDrawing = false;
			bridge.DrawingBudgetMs = 0;
			scene.Destroy();
		}
	}

	static Texture Cube()
	{
		var cube = Texture.CreateCube( Size, Size, ImageFormat.RGBA16161616F ).AsRenderTarget().WithUAVBinding().WithMips( 7 ).Finish();
		cube.Clear( Color.Blue );
		return cube;
	}

	/// <summary>Every face of every mip, as RGBA16F.</summary>
	static ulong[] Pixels( Texture cube )
	{
		var all = new System.Collections.Generic.List<ulong>();
		for ( int mip = 0; mip < cube.Mips; mip++ )
		{
			var size = Math.Max( 1, Size >> mip );
			var face = new ulong[size * size];
			for ( int slice = 0; slice < 6; slice++ )
			{
				cube.GetPixels( (0, 0, size, size), slice, mip, face.AsSpan(), ImageFormat.RGBA16161616F );
				all.AddRange( face );
			}
		}

		return all.ToArray();
	}
}
