namespace Sandbox.SceneLab;

/// <summary>
/// A camera's extra worlds (<c>SceneCamera.Worlds</c>) and debug worlds (<c>SceneCamera.DebugWorlds</c>), which native draws in
/// the main world's view: same layers, same depth, lit by the main world. The extra world holds an opaque box half behind a
/// wall and a glass box in front of it; the scene's debug world holds gizmo lines with depth and without, a model, a
/// translucent solid sphere and text, drawn every tick as a game draws gizmos. The compare renders a camera set up from the
/// scene's, with the extra world added, through both renderers (<see cref="SceneLabScene.ExtraWorld"/>).
/// </summary>
internal static class ExtraWorldsScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera, new Vector3( -240, -160, 140 ), new Vector3( 0, 0, 30 ) );
		using var _ = scene.Push();

		GameScenes.Prop( "Wall", "models/dev/box.vmdl", new Vector3( -20, 0, 0 ), new Color( 0.7f, 0.7f, 0.75f ), scale: 0.9f ).WorldScale = new Vector3( 0.2f, 2.4f, 1.6f );
		new GameObject( true, "Extra World" ).Components.Create<ExtraWorldFixture>();
		new GameObject( true, "Gizmos" ).Components.Create<GizmoFixture>();
		return scene;
	}

	/// <summary>
	/// The extra world's objects moved and recoloured, and the gizmos moved.
	/// </summary>
	public static void Change( Scene scene )
	{
		using var _ = scene.Push();

		scene.GetAllComponents<ExtraWorldFixture>().Single().Change();
		scene.Directory.FindByName( "Gizmos" ).First().WorldPosition = new Vector3( -30, 40, 10 );
	}

	public static SceneWorld ExtraWorld( Scene scene ) => scene.GetAllComponents<ExtraWorldFixture>().Single().World;

	/// <summary>
	/// Fails when either world is empty, so a pass can't come from neither renderer drawing them.
	/// </summary>
	public static string Validate( Scene scene )
	{
		var extra = ExtraWorld( scene )?.SceneObjects.Count ?? 0;
		var debug = scene.DebugSceneWorld?.SceneObjects.Count ?? 0;
		if ( extra < 2 ) return $"the extra world has {extra} objects";
		if ( debug < 4 ) return $"the debug world has {debug} objects";
		return null;
	}
}

/// <summary>
/// Owns the extra world and its objects, deleted with the scene.
/// </summary>
file sealed class ExtraWorldFixture : Component
{
	SceneModel box;
	SceneModel glass;

	public SceneWorld World { get; private set; }

	protected override void OnEnabled()
	{
		World = new SceneWorld();

		// Half behind the wall from the camera
		box = new SceneModel( World, Model.Load( "models/dev/box.vmdl" ), new Transform( new Vector3( 40, -30, 25 ), Rotation.Identity, 0.5f ) )
		{
			ColorTint = new Color( 1, 0.5f, 0.2f ),
		};

		// In front of the wall, over the box's edge
		glass = new SceneModel( World, Model.Load( "models/dev/box.vmdl" ), new Transform( new Vector3( -80, -30, 30 ), Rotation.Identity, 0.6f ) )
		{
			ColorTint = new Color( 0.4f, 0.8f, 1, 0.7f ),
		};
		glass.SetMaterialOverride( Material.Load( "materials/dev/primary_white_trans.vmat" ) );
	}

	public void Change()
	{
		box.Transform = box.Transform.WithPosition( new Vector3( 10, 40, 25 ) );
		box.ColorTint = new Color( 0.3f, 1, 0.4f );
		glass.Transform = glass.Transform.WithPosition( new Vector3( -90, 10, 40 ) );
	}

	protected override void OnDisabled()
	{
		box?.Delete();
		glass?.Delete();
		World?.Delete();
		box = null;
		glass = null;
		World = null;
	}
}

/// <summary>
/// Draws into the scene's debug world every tick (<c>Scene.DebugSceneWorld</c>, the gizmo instance the tick pushes), from
/// its GameObject's position. Code generation would mark a game's component with <c>IUpdateSubscriber</c>; this one says so
/// itself.
/// </summary>
file sealed class GizmoFixture : Component, Sandbox.Internal.IUpdateSubscriber
{
	protected override void OnUpdate()
	{
		var at = WorldPosition;

		// Lines with depth, partly behind the wall, and over everything
		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.LineThickness = 3;
		Gizmo.Draw.Color = Color.Red;
		Gizmo.Draw.Line( at + new Vector3( 40, -90, 5 ), at + new Vector3( 40, 90, 70 ) );
		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.LineBBox( new BBox( at + new Vector3( -70, -40, 0 ), at + new Vector3( 30, 40, 70 ) ) );

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.Line( at + new Vector3( 55, -90, 5 ), at + new Vector3( 55, 90, 70 ) );

		// A model, lit by the main world's sun, and a translucent solid
		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.Color = new Color( 0.9f, 0.3f, 0.9f );
		Gizmo.Draw.Model( "models/dev/sphere.vmdl", new Transform( at + new Vector3( 60, 60, 30 ), Rotation.Identity, 0.4f ) );
		Gizmo.Draw.Color = new Color( 0.2f, 0.6f, 1, 0.5f );
		Gizmo.Draw.SolidSphere( at + new Vector3( -60, 60, 40 ), 25 );

		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.Text( "Debug World", new Transform( at + new Vector3( 0, 0, 110 ) ), size: 24 );
	}
}
