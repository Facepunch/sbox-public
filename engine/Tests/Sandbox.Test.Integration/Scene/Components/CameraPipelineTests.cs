namespace SceneTests.Components;

/// <summary>
/// A camera modifier that adds a fixed FOV delta and records what it saw, so tests can prove the
/// chain runs in order and hands the final view to the post pass.
/// </summary>
public sealed class TestCameraModifier : Component, ICameraModifier
{
	public int Order;
	public float AddFov;

	public float SawFovInModify { get; private set; }
	public float SawFovInPost { get; private set; }
	public int ModifyCalls { get; private set; }
	public int PostCalls { get; private set; }

	int ICameraModifier.CameraOrder => Order;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		ModifyCalls++;
		SawFovInModify = view.FieldOfView;
		view.FieldOfView += AddFov;
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		PostCalls++;
		SawFovInPost = view.FieldOfView;
	}
}

/// <summary>
/// Pins the camera composition pipeline: modifiers run ordered by CameraOrder, the post pass and
/// <see cref="CameraComponent.View"/> carry the final composed view, transient camera effects apply
/// to the scene camera only, and the composed clip planes reach the render.
/// </summary>
[TestClass]
public class CameraPipelineTests
{
	static CameraComponent CreateCamera( Scene scene )
	{
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 300 );
		var cam = go.Components.Create<CameraComponent>();
		cam.FieldOfView = 60;
		return cam;
	}

	/// <summary>
	/// Modifiers run lowest CameraOrder first regardless of creation order - the later modifier sees
	/// the earlier one's change, and both post passes see the fully composed value.
	/// </summary>
	[TestMethod]
	public void ModifiersRunInOrderAndPostSeesFinal()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateCamera( scene );

		// Created in reverse order to prove the sort
		var late = scene.CreateObject().Components.Create<TestCameraModifier>();
		late.Order = 200;
		late.AddFov = 5;

		var early = scene.CreateObject().Components.Create<TestCameraModifier>();
		early.Order = 100;
		early.AddFov = 10;

		scene.GameTick();

		Assert.AreEqual( 60f, early.SawFovInModify, 0.001f, "First modifier sees the base view" );
		Assert.AreEqual( 70f, late.SawFovInModify, 0.001f, "Second modifier sees the first one's change" );
		Assert.AreEqual( 75f, early.SawFovInPost, 0.001f, "Post pass sees the final view" );
		Assert.AreEqual( 75f, late.SawFovInPost, 0.001f );
		Assert.AreEqual( 1, early.ModifyCalls );
		Assert.AreEqual( 1, early.PostCalls );
	}

	/// <summary>
	/// The composed view is published on <see cref="CameraComponent.View"/> - zoom-aware logic like
	/// sensitivity scaling reads the effective FOV from here.
	/// </summary>
	[TestMethod]
	public void ComposedViewIsPublished()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateCamera( scene );

		var zoom = scene.CreateObject().Components.Create<TestCameraModifier>();
		zoom.Order = 200;
		zoom.AddFov = -40;

		scene.GameTick();

		Assert.AreEqual( 20f, cam.View.FieldOfView, 0.001f );
		Assert.AreEqual( cam.WorldPosition, cam.View.Position );
		Assert.AreEqual( 60f, cam.FieldOfView, 0.001f, "The component's base FOV is never written by the pipeline" );
	}

	/// <summary>
	/// Transient camera effects apply after composition, to the scene camera only - neither the
	/// component transform nor the published View carry them, so they can never accumulate.
	/// </summary>
	[TestMethod]
	public void EffectsAreRenderOnly()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateCamera( scene );
		var baseRotation = cam.WorldRotation;

		// A tilt held at full strength - deterministic, unlike the random shake
		var tilt = CameraEffectSystem.Get( scene ).AddTilt( cam, new Angles( 0, 0, 30 ), duration: 100f, easeTime: 0.2f );
		tilt.Update( 1f );

		scene.GameTick();

		using var sceneCamera = new SceneCamera( "test" );
		cam.UpdateSceneCamera( sceneCamera );

		Assert.AreEqual( 30f, sceneCamera.Rotation.Angles().roll, 0.1f, "The render camera carries the tilt" );
		Assert.AreEqual( 0f, cam.View.Rotation.Angles().roll, 0.001f, "The composed view does not" );
		Assert.AreEqual( baseRotation, cam.WorldRotation, "The component transform is never touched" );
	}

	/// <summary>
	/// The view composes once, at the camera stage of the tick - projection helpers reuse the
	/// composed view instead of re-running the modifier chain.
	/// </summary>
	[TestMethod]
	public void ViewComposesOncePerTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateCamera( scene );

		var mod = scene.CreateObject().Components.Create<TestCameraModifier>();
		mod.AddFov = 5;

		scene.GameTick();

		Assert.AreEqual( 1, mod.ModifyCalls, "the tick composes exactly once" );
		Assert.AreEqual( 65f, cam.View.FieldOfView, 0.001f );

		cam.PointToScreenPixels( Vector3.Zero );
		Assert.AreEqual( 1, mod.ModifyCalls, "projection helpers must not recompose" );

		scene.GameTick();
		Assert.AreEqual( 2, mod.ModifyCalls, "the next tick composes again" );
	}

	/// <summary>
	/// A camera changed after the camera stage stomps the composition - it renders from its raw
	/// values, no modifiers, no recompose. Place a camera, render it, done - no tick required.
	/// </summary>
	[TestMethod]
	public void MovingAfterComposeStomps()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateCamera( scene );

		var mod = scene.CreateObject().Components.Create<TestCameraModifier>();
		mod.AddFov = 5;

		scene.GameTick();
		Assert.AreEqual( 1, mod.ModifyCalls );

		// The scene isn't ticking - a video tool places the camera and renders.
		cam.WorldPosition = new Vector3( 5000, 0, 0 );

		using var sceneCamera = new SceneCamera( "test" );
		cam.UpdateSceneCamera( sceneCamera );

		Assert.AreEqual( new Vector3( 5000, 0, 0 ), sceneCamera.Position, "renders from the raw transform" );
		Assert.AreEqual( 1, mod.ModifyCalls, "moving the camera does not recompose" );
	}

	/// <summary>
	/// Modifier-written clip planes reach the render camera.
	/// </summary>
	[TestMethod]
	public void ClipPlanesFlowThrough()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cam = CreateCamera( scene );
		cam.ZNear = 10;
		cam.ZFar = 10000;

		var mod = scene.CreateObject().Components.Create<ClipPlaneModifier>();

		scene.GameTick();

		using var sceneCamera = new SceneCamera( "test" );
		cam.UpdateSceneCamera( sceneCamera );

		Assert.AreEqual( 55f, sceneCamera.ZNear, 0.001f );
		Assert.AreEqual( 5555f, sceneCamera.ZFar, 0.001f );
		Assert.AreEqual( 55f, cam.View.ZNear, 0.001f );
	}
}

/// <summary>
/// A modifier that overrides the view's clip planes.
/// </summary>
public sealed class ClipPlaneModifier : Component, ICameraModifier
{
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		view.ZNear = 55;
		view.ZFar = 5555;
	}
}
