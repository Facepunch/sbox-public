using Borealis;
using Borealis.Features;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// A cube capture face (<see cref="RenderView.Capture"/>) draws the world as native's capture views do: without objects hidden
/// in cubemaps or viewmodels.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CaptureTests
{
	[TestMethod]
	public void CapturesLeaveOutHiddenObjectsAndViewmodels()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) );
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) ) { HiddenInCaptures = true } );
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) { Overlay = true } );

		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();
		var view = LookingDownX();
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 3, system.MainPass.Visible( mesh ).Count, "a view draws all three" );

		view.Capture = true;
		CollectAndPrepare( system, world, view );
		CollectionAssert.AreEqual( new[] { 0 }, system.MainPass.Visible( mesh ).ToArray(), "a capture only the plain one" );
	}
}
