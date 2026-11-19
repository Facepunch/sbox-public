namespace Editor.Widgets.SceneTests;

[Title( "Rotation" ), Icon( "screen_rotation" )]
[Description( "Standard rotation widget" )]
internal class RotationSimple : ISceneTest
{
	Rotation rotation = Rotation.Identity;
	Rotation startRotation = Rotation.Identity;

	public void Frame()
	{
		using ( Gizmo.Scope( "ObjectRotation", new Transform( new Vector3( 15, 12, 10 ) ) ) )
		{
			Gizmo.Draw.Color = Gizmo.Colors.Green;

			if ( Gizmo.WasLeftMousePressed ) startRotation = rotation;
			if ( Gizmo.Control.UpdateRotation( "my-arrow", Rotation.Identity, out Rotation rotationDelta ) )
			{
				rotation = startRotation * Gizmo.Snap( rotationDelta );
			}
			Gizmo.Draw.Rotate( "my-arrow" );

			using ( Gizmo.Scope( "Object", new Transform( 0, rotation ) ) )
			{
				Gizmo.Draw.Color = Color.White;
				Gizmo.Draw.Model( "models/editor/playerstart.vmdl" );
			}
		}

		var angles = rotation.Angles();
		Gizmo.Draw.Color = Color.Black;
		Gizmo.Draw.Text( $"Value: {angles.pitch:n0}, {angles.yaw:n0}, {angles.roll:n0}", new Transform( Vector3.Down * 50.0f ) );
	}
}
