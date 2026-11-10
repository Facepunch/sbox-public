using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Gpu;
using System;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// The view: its constant buffer layout, reverse-Z perspective and orthographic projection, axes, and frustum culling.
/// </summary>
[TestClass]
public class ViewTests
{
	[TestMethod]
	public void ViewConstantsMatchNativeLayout()
	{
		ViewConstants.ValidateLayout();
	}

	[TestMethod]
	public void ProjectionIsReverseZ()
	{
		var view = LookingDownX();

		var near = Project( view, new Vector3( 1, 0, 0 ) );
		var far = Project( view, new Vector3( 1000, 0, 0 ) );

		Assert.AreEqual( 1.0f, near.z, 1e-5f, "near plane should be depth 1" );
		Assert.AreEqual( 0.0f, far.z, 1e-5f, "far plane should be depth 0" );
		Assert.AreEqual( 0.0f, near.x, 1e-5f );
		Assert.AreEqual( 0.0f, near.y, 1e-5f );
	}

	[TestMethod]
	public void ProjectionAxes()
	{
		var view = LookingDownX();

		// Source is z up, y left - left of the view is -x in clip space, up is +y
		var left = Project( view, new Vector3( 100, 50, 0 ) );
		var up = Project( view, new Vector3( 100, 0, 50 ) );

		Assert.IsTrue( left.x < 0, $"+y should be left, got {left}" );
		Assert.IsTrue( up.y > 0, $"+z should be up, got {up}" );

		// 90 degree horizontal fov - a point 45 degrees off axis lands on the edge
		var edge = Project( view, new Vector3( 100, -100, 0 ) );
		Assert.AreEqual( 1.0f, edge.x, 1e-4f );
	}

	/// <summary>
	/// A sun cascade's parallel projection: reverse-Z, near plane at depth 1, far at 0, and the
	/// ortho size across the view.
	/// </summary>
	[TestMethod]
	public void OrthographicProjection()
	{
		var view = new RenderView
		{
			Rotation = Rotation.Identity,
			Orthographic = true,
			OrthoSize = new Vector2( 100, 50 ),
			ZNear = 0,
			ZFar = 1000,
			Viewport = new Rect( 0, 0, 2048, 2048 ),
		};
		view.Update();

		Assert.AreEqual( 1, Project( view, new Vector3( 0, 0, 0 ) ).z, 1e-5, "near plane" );
		Assert.AreEqual( 0, Project( view, new Vector3( 1000, 0, 0 ) ).z, 1e-5, "far plane" );
		Assert.AreEqual( 0.5f, Project( view, new Vector3( 500, 0, 0 ) ).z, 1e-5, "depth is linear" );

		// Right is -y in Source's axes, up is +z
		Assert.AreEqual( 1, Project( view, new Vector3( 500, -50, 0 ) ).x, 1e-5 );
		Assert.AreEqual( 1, Project( view, new Vector3( 500, 0, 25 ) ).y, 1e-5 );
	}

	[TestMethod]
	public void FrustumCulling()
	{
		var frustum = LookingDownX().Frustum;

		Assert.IsTrue( frustum.Intersects( new Vector3( 100, 0, 0 ), Vector3.One ), "in front" );
		Assert.IsFalse( frustum.Intersects( new Vector3( -100, 0, 0 ), Vector3.One ), "behind" );
		Assert.IsFalse( frustum.Intersects( new Vector3( 2000, 0, 0 ), Vector3.One ), "past the far plane" );
		Assert.IsFalse( frustum.Intersects( new Vector3( 100, 300, 0 ), Vector3.One ), "off to the left" );
		Assert.IsFalse( frustum.Intersects( new Vector3( 100, 0, -300 ), Vector3.One ), "below" );

		// Straddling a plane still counts
		Assert.IsTrue( frustum.Intersects( new Vector3( 100, 105, 0 ), new Vector3( 10 ) ), "straddling the left plane" );
		Assert.IsTrue( frustum.Intersects( new Vector3( 0.5f, 0, 0 ), Vector3.One ), "straddling the near plane" );
	}
}
