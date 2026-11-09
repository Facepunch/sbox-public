namespace Sandbox.SceneLab;

/// <summary>
/// Models a custom object draws itself with <c>Graphics.DrawModelInstanced</c> from transforms on the CPU, as games batch
/// their props (Mow the Lawn's static prop batches and grass): a wall of boxes casting shadows, with an ordinary prop in
/// front of it, so the depth prepass, the shadow views and the forward pass all draw the instances.
/// </summary>
internal static class InstancedScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera );
		using var _ = scene.Push();

		GameScenes.Prop( "Front", "models/dev/box.vmdl", new Vector3( -70, -20, 0 ), new Color( 0.9f, 0.55f, 0.3f ), scale: 0.6f );
		new InstancedBoxes( scene.SceneWorld ).Build( 4, 0 );
		return scene;
	}

	/// <summary>
	/// The wall rebuilt taller and moved along, so its transforms change.
	/// </summary>
	public static void Change( Scene scene )
	{
		foreach ( var boxes in scene.SceneWorld.SceneObjects.OfType<InstancedBoxes>() )
			boxes.Build( 6, 40 );
	}
}

/// <summary>
/// A wall of boxes drawn as one instanced draw.
/// </summary>
file sealed class InstancedBoxes : SceneCustomObject
{
	readonly Model model = Model.Load( "models/dev/box.vmdl" );
	Transform[] transforms = [];

	public InstancedBoxes( SceneWorld world ) : base( world )
	{
		Flags.IsOpaque = true;
		Flags.IsTranslucent = false;
		Flags.CastShadows = true;
	}

	public void Build( int rows, float offset )
	{
		var list = new List<Transform>();
		for ( int row = 0; row < rows; row++ )
			for ( int column = 0; column < 7; column++ )
			{
				var position = new Vector3( 60, -150 + column * 50 + offset + (row % 2) * 25, row * 24 );
				list.Add( new Transform( position, Rotation.FromYaw( column * 9 ), 0.45f ) );
			}

		transforms = list.ToArray();
		Bounds = new BBox( new Vector3( 30, -200, -10 ), new Vector3( 90, 250, rows * 24 + 30 ) );
	}

	public override void RenderSceneObject()
	{
		Graphics.DrawModelInstanced( model, transforms );
	}
}
