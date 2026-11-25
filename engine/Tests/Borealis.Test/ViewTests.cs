using Borealis;
using Borealis.Gpu;
using System;
using static BorealisTests.TestScenes;

namespace BorealisTests;

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

	/// <summary>
	/// A temporal upscaler's jitter moves the projection by its offset of clip space, last frame's projection keeps last frame's
	/// jitter (as <c>CFrustum::SetProjectionJitter</c> moves both), and the constants after the upscale have none, at the
	/// display size.
	/// </summary>
	[TestMethod]
	public void JitterOffsetsProjectionAndReprojection()
	{
		var view = LookingDownX();
		var point = new Vector3( 100, 20, 10 );
		var plain = Project( view, point );

		view.Jitter = new Vector2( 0.01f, -0.02f );
		view.Update();
		var jittered = Project( view, point );
		Assert.AreEqual( plain.x + 0.01f, jittered.x, 1e-5f );
		Assert.AreEqual( plain.y - 0.02f, jittered.y, 1e-5f );
		Assert.AreEqual( plain.z, jittered.z, 1e-6f, "depth doesn't move" );

		view.Jitter = new Vector2( -0.03f, 0.04f );
		view.Update();
		var previous = view.PreviousWorldToProjection.Transform( new Vector4( point, 1 ) );
		previous /= previous.w;
		Assert.AreEqual( jittered.x, previous.x, 1e-5f, "last frame's projection keeps last frame's jitter" );
		Assert.AreEqual( jittered.y, previous.y, 1e-5f );

		var c = new ViewConstants();
		view.FillConstants( ref c, new Vector2( 1000, 1000 ), 0 );
		view.FillUpscaledConstants( ref c, new Rect( 0, 0, 2000, 2000 ), new Vector2( 2000, 2000 ) );

		// The view sits at the origin, so its rotation-only world to projection is the whole of it
		var unjittered = c.WorldToProjection.Transform( new Vector4( point, 1 ) );
		unjittered /= unjittered.w;
		Assert.AreEqual( plain.x, unjittered.x, 1e-5f );
		Assert.AreEqual( plain.y, unjittered.y, 1e-5f );
		Assert.AreEqual( 2000, c.ViewportSize.x );
		Assert.AreEqual( 2000, c.RenderTargetSize.y );
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

	static void AssertClose( Matrix expected, Matrix actual, string message )
	{
		var e = (System.Numerics.Matrix4x4)expected;
		var a = (System.Numerics.Matrix4x4)actual;
		for ( int row = 0; row < 4; row++ )
		{
			for ( int column = 0; column < 4; column++ )
				Assert.AreEqual( e[row, column], a[row, column], 1e-4f, $"{message}: M{row + 1}{column + 1}" );
		}
	}

	/// <summary>
	/// A projection in the engine's convention (<c>ViewSetup.ProjectionMatrix</c>: column vectors, forward depth) becomes the
	/// same row-vector reverse-Z one the field of view makes.
	/// </summary>
	[TestMethod]
	public void CustomProjectionInEngineConvention()
	{
		var plain = LookingDownX();
		var custom = LookingDownX();
		custom.Projection = Matrix.CreateProjection( custom.ZNear, custom.ZFar, custom.FieldOfView, 1 );
		custom.Update();

		AssertClose( plain.ViewToProjection, custom.ViewToProjection, "projection" );
		AssertClose( plain.ViewToProjection, custom.SquareViewToProjection, "square" );
	}

	/// <summary>
	/// Clip-space bounds draw part of the projection over the whole view, as native's off-centre projection does
	/// (<c>ComputeProjectionMatrix</c> with clip-space bounds, which <c>Matrix.CreateProjection</c> copies).
	/// </summary>
	[TestMethod]
	public void ClipSpaceBoundsAreAnOffCentreProjection()
	{
		var bounds = new Vector4( -0.4f, -0.6f, 0.5f, 0.3f );

		var view = LookingDownX();
		view.ClipSpaceBounds = bounds;
		view.Update();

		var offCentre = LookingDownX();
		offCentre.Projection = Matrix.CreateProjection( offCentre.ZNear, offCentre.ZFar, offCentre.FieldOfView, 1, bounds );
		offCentre.Update();

		AssertClose( offCentre.ViewToProjection, view.ViewToProjection, "bounds" );

		// The bounds' corners are the view's: right is -y, up is +z, at 45 degrees a side
		var corner = Project( view, new Vector3( 100, -50, 30 ) );
		Assert.AreEqual( 1, corner.x, 1e-4f );
		Assert.AreEqual( 1, corner.y, 1e-4f );
		Assert.IsFalse( view.Frustum.Intersects( new Vector3( 100, -80, 0 ), Vector3.One ), "past the bounds' right" );
		Assert.IsTrue( LookingDownX().Frustum.Intersects( new Vector3( 100, -80, 0 ), Vector3.One ), "inside the whole view" );
	}

	/// <summary>
	/// An oblique projection's near plane is its clip plane: what's on the other side is culled, and the plane is depth 1. Its
	/// square projection, for shadow cascades and light clusters, is the plain one.
	/// </summary>
	[TestMethod]
	public void ObliqueProjectionClipsAtItsPlane()
	{
		var view = LookingDownX();
		var projection = Matrix.CreateProjection( view.ZNear, view.ZFar, view.FieldOfView, 1 );
		var plane = new Plane( new Vector3( 0, 0, -10 ), Vector3.Down );
		view.Projection = Matrix.CreateObliqueProjection( new Transform( view.Position, view.Rotation ), plane, projection );
		view.Update();

		Assert.IsTrue( view.Frustum.Intersects( new Vector3( 100, 0, -30 ), Vector3.One ), "below the plane" );
		Assert.IsFalse( view.Frustum.Intersects( new Vector3( 100, 0, 10 ), Vector3.One ), "above the plane" );
		Assert.AreEqual( 1, Project( view, new Vector3( 100, 20, -10 ) ).z, 1e-4f, "the plane is the near plane" );

		AssertClose( LookingDownX().ViewToProjection, view.SquareViewToProjection, "square" );
	}

	/// <summary>
	/// A parallel projection's depth is made again linear between the near and far planes.
	/// </summary>
	[TestMethod]
	public void SquareDepthOfAParallelProjection()
	{
		var view = new RenderView { Orthographic = true, OrthoSize = new Vector2( 100, 50 ), ZNear = 0, ZFar = 1000, Viewport = new Rect( 0, 0, 100, 50 ) };
		view.Update();

		var skewed = view.ViewToProjection;
		skewed.M13 = 0.3f;
		AssertClose( view.ViewToProjection, RenderView.SquareDepth( skewed, view.ZNear, view.ZFar ), "square" );
	}
}
