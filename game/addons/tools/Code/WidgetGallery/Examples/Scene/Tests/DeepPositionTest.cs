
using Sandbox.Utility;
using System;
using System.Diagnostics;

namespace Editor.Widgets.SceneTests;

[Title( "Position - Deep" )]
[Description( "Test position gizmo works even when in a rotated heirachy" )]
[Icon( "control_camera" )]
internal class PositionDeep : ISceneTest
{
	List<Vector3> crumbs = new List<Vector3>();

	Vector3 position = new Vector3( 0, 0, 30.0f );

	public void Frame()
	{
		using ( Gizmo.Scope( "a" ) )
		{
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.4f );
			Gizmo.Draw.Plane( Vector3.Zero, Vector3.Up );
			Gizmo.Draw.Model( "models/citizen_props/crate01.vmdl" );

			using ( Gizmo.Scope( "b", new Transform( Vector3.Up * 30.0f, Rotation.From( 45, 45, 45 ) ) ) )
			{
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.4f );
				Gizmo.Draw.Model( "models/citizen_props/crate01.vmdl" );

				var objectRotation = Rotation.From( 45, 45, 45 );

				using ( Gizmo.Scope( "ObjectPosition", new Transform( position ) ) )
				{
					Gizmo.Draw.Color = Color.White;
					var oldPosition = position;
					if ( Gizmo.Control.UpdatePosition( "my-arrow", position, out var newPosition ) )
					{
						position = newPosition;
					}
					Gizmo.Transform = Gizmo.Transform.ToWorld( new Transform( position - oldPosition ) );
					Gizmo.Draw.Model( "models/citizen_props/crate01.vmdl" );
					Gizmo.Draw.Position( "my-arrow" );

					Gizmo.Draw.Color = Color.Black;
					Gizmo.Draw.Text( $"Value: {position.x:n0}, {position.y:n0}, {position.z:n0}", new Transform( Vector3.Down * 50.0f ) );
				}

			}
		}

	}
}
