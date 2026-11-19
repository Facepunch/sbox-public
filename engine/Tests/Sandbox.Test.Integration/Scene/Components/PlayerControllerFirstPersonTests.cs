using System.Collections.Generic;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	/// <summary>
	/// A first person player with a plain body model, a main camera and one pre-render applied.
	/// </summary>
	static (PlayerController Controller, ModelRenderer Body) CreateFirstPersonPlayer( Scene scene )
	{
		scene.CreateObject().Components.Create<CameraComponent>();
		scene.UpdateMainCamera();

		var pc = CreatePlayer( scene, Vector3.Zero );
		pc.ThirdPerson = false;

		var bodyGo = scene.CreateObject();
		bodyGo.Name = "Body Model";
		bodyGo.Parent = pc.GameObject;
		var body = bodyGo.Components.Create<ModelRenderer>();
		body.Model = Model.Load( "models/dev/box.vmdl" );

		pc.OnPreRenderInternal();
		return (pc, body);
	}

	/// <summary>
	/// The local shadow-only copies cast for a renderer's scene object.
	/// </summary>
	static List<SceneObject> ShadowCopiesOf( Scene scene, ModelRenderer renderer )
	{
		var copies = new List<SceneObject>();
		foreach ( var so in scene.SceneWorld.SceneObjects )
		{
			if ( so.Component == renderer && so != renderer.SceneObject && so.Flags.ExcludeGameLayer && so.Flags.CastShadows )
				copies.Add( so );
		}
		return copies;
	}

	/// <summary>
	/// In first person the body root carries the viewer tag, so the body and every effect, light and non-model renderer
	/// under it is excluded from the viewer's camera only - other cameras still see it untouched. The shadow the tag
	/// takes away from the viewer comes back from a local shadow-only copy. RenderType is never rewritten.
	/// </summary>
	[TestMethod]
	public void FirstPersonHidesBodyOnlyFromViewerAndKeepsShadows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, body) = CreateFirstPersonPlayer( scene );
		var so = body.SceneObject;

		var effectGo = scene.CreateObject();
		effectGo.Parent = body.GameObject;
		effectGo.Components.Create<LineRenderer>();
		pc.OnPreRenderInternal();

		Assert.IsTrue( pc.GameObject.Tags.Has( "viewer", false ), "The body root is tagged for the viewer's camera" );
		Assert.IsTrue( effectGo.Tags.Has( "viewer" ), "Non-model renderers under the body inherit the tag" );
		Assert.IsTrue( so.Tags.Has( "viewer" ), "The model is hidden by the tag" );
		Assert.IsFalse( so.Flags.ExcludeGameLayer, "The original stays in the game layer for other cameras" );
		Assert.IsTrue( so.Flags.CastShadows );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.On, body.RenderType, "RenderType must not be rewritten" );

		var shadows = ShadowCopiesOf( scene, body );
		Assert.AreEqual( 1, shadows.Count, "One shadow-only copy for the model" );
		Assert.IsFalse( shadows[0].Tags.Has( "viewer" ), "The shadow copy isn't excluded by the viewer's camera" );

		pc.ThirdPerson = true;
		pc.OnPreRenderInternal();

		Assert.IsFalse( pc.GameObject.Tags.Has( "viewer" ), "Leaving first person clears the tag" );
		Assert.IsFalse( effectGo.Tags.Has( "viewer" ) );
		Assert.IsFalse( so.Tags.Has( "viewer" ) );
		Assert.AreEqual( 0, ShadowCopiesOf( scene, body ).Count, "Leaving first person removes the shadow copy" );
	}

	/// <summary>
	/// The controller only removes a viewer tag it added. A game driving its own camera can tag the body itself.
	/// </summary>
	[TestMethod]
	public void GameViewerTagIsLeftAlone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, _) = CreateFirstPersonPlayer( scene );
		pc.ThirdPerson = true;
		pc.OnPreRenderInternal();
		Assert.IsFalse( pc.GameObject.Tags.Has( "viewer" ) );

		// Own camera: tagged by the game, never touched.
		pc.UseCameraControls = false;
		pc.GameObject.Tags.Add( "viewer" );
		pc.OnPreRenderInternal();
		Assert.IsTrue( pc.GameObject.Tags.Has( "viewer" ) );

		// Already tagged when entering first person: still tagged after leaving it.
		pc.UseCameraControls = true;
		pc.ThirdPerson = false;
		pc.OnPreRenderInternal();
		pc.ThirdPerson = true;
		pc.OnPreRenderInternal();
		Assert.IsTrue( pc.GameObject.Tags.Has( "viewer" ) );
	}

	/// <summary>
	/// Game code changing RenderType while the player is in first person is respected: an Off renderer loses its shadow
	/// copy, a ShadowsOnly one keeps it, and the renderer's own flags are never touched.
	/// </summary>
	[TestMethod]
	public void FirstPersonRespectsRenderTypeChanges()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, body) = CreateFirstPersonPlayer( scene );
		var so = body.SceneObject;

		body.RenderType = ModelRenderer.ShadowRenderType.Off;
		pc.OnPreRenderInternal();

		Assert.AreEqual( ModelRenderer.ShadowRenderType.Off, body.RenderType, "The game's RenderType is kept" );
		Assert.IsFalse( so.Flags.CastShadows );
		Assert.AreEqual( 0, ShadowCopiesOf( scene, body ).Count, "An Off renderer doesn't gain a shadow in first person" );

		body.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		pc.OnPreRenderInternal();

		Assert.IsTrue( so.Flags.ExcludeGameLayer, "ShadowsOnly is the renderer's own setting" );
		Assert.AreEqual( 1, ShadowCopiesOf( scene, body ).Count, "A ShadowsOnly renderer still casts for the viewer" );

		pc.ThirdPerson = true;
		pc.OnPreRenderInternal();

		Assert.AreEqual( ModelRenderer.ShadowRenderType.ShadowsOnly, body.RenderType );
		Assert.IsTrue( so.Flags.CastShadows );
		Assert.IsTrue( so.Flags.ExcludeGameLayer );
	}

	/// <summary>
	/// Without first person shadows the inherited viewer tag alone hides the body, so it's only hidden from cameras
	/// that exclude "viewer" - other cameras (mirrors, render targets) still see it, and its flags are untouched.
	/// </summary>
	[TestMethod]
	public void FirstPersonWithoutShadowsOnlyHidesFromViewerCamera()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, body) = CreateFirstPersonPlayer( scene );
		pc.CastShadowsInFirstPerson = false;
		pc.OnPreRenderInternal();

		var so = body.SceneObject;
		Assert.IsTrue( so.Tags.Has( "viewer" ), "Hidden by the viewer tag" );
		Assert.IsFalse( so.Flags.ExcludeGameLayer, "Not hidden from other cameras" );
		Assert.IsTrue( so.Flags.CastShadows, "Flags stay as the renderer set them" );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.On, body.RenderType );
		Assert.AreEqual( 0, ShadowCopiesOf( scene, body ).Count, "No shadow copies without first person shadows" );
	}
}
