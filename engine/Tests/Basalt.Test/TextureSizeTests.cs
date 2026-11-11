using Basalt;
using Basalt.Features;
using BasaltTests.Headless;
using System;
using System.Linq;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// The texture sizes draws ask streaming for (<see cref="TextureSizes"/>), worked out as native's
/// <c>GeneratePrimitivesArgs_t</c> does: UV density times the view's pixels per world unit at the object's distance, or
/// the object's screen size scaled up when the draw has no UV density; a run asks for its largest object's.
/// </summary>
// Frames go through Basalt's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class TextureSizeTests
{
	static HeadlessDevice Device => HeadlessRenderHost.Headless;

	[TestMethod]
	public void DrawSizesFollowUvDensityOrScreenSize()
	{
		// 1000 pixels wide at 90 degrees: 500 pixels per world unit at distance 1
		var sizes = new TextureSizes( LookingDownX(), LookingDownX() );

		// A unit box 30 away: 29 to its nearest face
		var (scale, fallback) = sizes.ForObject( new Vector3( 30, 0, 0 ), new Vector3( 1 ) );
		Assert.AreEqual( 500.0f / 29.0f, scale, 1e-3f );
		Assert.AreEqual( 4 * 500 / 29, TextureSizes.ForDraw( 4, scale, fallback ) );

		// Without a UV density: its bounding sphere's screen size (radius over distance), times the viewport width, times 2
		Assert.AreEqual( (int)(MathF.Sqrt( 3 ) / 30.0f * 1000 * 2), TextureSizes.ForDraw( 0, scale, fallback ) );

		// Inside it, the nearest face is no nearer than 1, and the whole view
		var (insideScale, insideFallback) = sizes.ForObject( Vector3.Zero, new Vector3( 1 ) );
		Assert.AreEqual( 500.0f, insideScale, 1e-3f );
		Assert.AreEqual( 2000, TextureSizes.ForDraw( 0, insideScale, insideFallback ) );

		// No more than a uint16 holds
		Assert.AreEqual( ushort.MaxValue, TextureSizes.ForDraw( 1000, insideScale, insideFallback ) );
	}

	[TestMethod]
	public void OrthographicViewsAskForEveryMip()
	{
		var view = LookingDownX();
		view.Orthographic = true;
		view.OrthoSize = new Vector2( 100, 100 );
		view.Update();

		// Native's orthographic frustum has a negative field of view: draws with a UV density ask for all mips, and an
		// aggregate's fragments with one don't raise its size
		var sizes = new TextureSizes( view, view );
		var (scale, fallback) = sizes.ForObject( new Vector3( 30, 0, 0 ), new Vector3( 1 ) );
		Assert.AreEqual( TextureSizes.All, TextureSizes.ForDraw( 4, scale, fallback ) );
		Assert.AreEqual( 0.0f, sizes.ForFragment( 4, new Vector3( 30, 0, 0 ), new Vector3( 1 ), view.Position, fallback ) );

		// The fallback's screen size is the sphere's diameter over the view's width
		Assert.AreEqual( (int)(2 * MathF.Sqrt( 3 ) / 100 * 1000 * 2), TextureSizes.ForDraw( 0, scale, fallback ) );
	}

	[TestMethod]
	public void InstancedRunAsksForItsNearestObject()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material, UvDensity: 4 )]] );

		var world = new RenderWorld();
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 30, 4, 0 ) ) ) );
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) ) );
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 50, -4, 0 ) ) ) );

		using var target = ViewTarget.Create( new Vector2Int( 1000, 1000 ) );
		using var system = new RenderSystem();

		GpuCommand[] forward;
		using ( var frame = Device.Capture() )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
			forward = frame.Commands.Where( c => c.IsDraw && c.In( "Opaque Forward" ) ).ToArray();
		}

		// One draw of the run of all three (an occlusion-culled one, whose instances the GPU counts), asking for what the
		// nearest one needs: its nearest face is 19 away
		Assert.AreEqual( 1, forward.Length );
		Assert.AreEqual( 3, system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque.Single().Count );
		Assert.AreEqual( (int)(4 * (500.0f / 19.0f)), forward[0].TextureSize );
	}
}
