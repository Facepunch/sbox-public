
using Sandbox.Utility;
using System;
using System.Diagnostics;

namespace Editor.Widgets.SceneTests;

[Title( "Position" ), Icon( "control_camera" )]
[Description( "Dragging the gizmo around will draw a line" )]
internal class PositionSimple : ISceneTest
{
	List<Vector3> crumbs = new List<Vector3>();

	Vector3 position;

	public void Frame()
	{
		using ( Gizmo.Scope( "ObjectPosition", new Transform( position ) ) )
		{
			Gizmo.Draw.Color = Gizmo.Colors.Green;
			var oldPosition = position;
			if ( Gizmo.Control.UpdatePosition( "my-arrow", position, out var newPosition ) )
			{
				position = newPosition;
			}
			Gizmo.Transform = Gizmo.Transform.ToWorld( new Transform( position - oldPosition ) );
			Gizmo.Draw.Position( "my-arrow" );

			Gizmo.Draw.Color = Color.Black;
			Gizmo.Draw.Text( $"Value: {position.x:n0}, {position.y:n0}, {position.z:n0}", new Transform( Vector3.Down * 50.0f ) );
		}

		var lastPos = crumbs.LastOrDefault( 0 );

		var gap = 3.0f;
		while ( position.Distance( lastPos ) > gap )
		{
			var delta = (position - lastPos).Normal * gap;
			crumbs.Add( lastPos + delta );
			lastPos = crumbs.LastOrDefault( 0 );
		}

		using ( Gizmo.Scope( "LineDrawing" ) )
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineThickness = 2.0f;
			Gizmo.Draw.IgnoreDepth = false;

			lastPos = crumbs.FirstOrDefault();
			for ( int i = 1; i < crumbs.Count; i++ )
			{
				var crumb = crumbs[i];

				//Scene.Draw.Model( "models/citizen_props/beachball.vmdl", new Transform( crumb, Rotation.Identity, 0.2f ) );
				Gizmo.Draw.Line( crumb, lastPos );

				var normal = (lastPos - crumb).Normal;
				lastPos = crumb;
			}
		}

	}
}
