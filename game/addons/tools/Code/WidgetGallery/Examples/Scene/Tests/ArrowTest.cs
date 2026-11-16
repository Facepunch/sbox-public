
using Sandbox.Utility;
using System;
using System.Diagnostics;

namespace Editor.Widgets.SceneTests;

[Title( "Arrow - Simple" ), Icon( "arrow_right_alt" )]
[Description( "Dragging up and down changes the value - arrow is static" )]
internal class ArrowSimple : ISceneTest
{
	float someNumber;

	public void Frame()
	{
		Gizmo.Draw.Color = Gizmo.Colors.Green;
		if ( Gizmo.Control.UpdateArrow( "my-arrow", Vector3.Up, out var distance ) )
		{
			someNumber += distance;
		}
		Gizmo.Draw.Arrow( "my-arrow", Vector3.Up );

		Gizmo.Draw.Color = Color.Black;
		Gizmo.Draw.Text( $"Value: {someNumber:n0}", new Transform( Vector3.Down * 10.0f ) );
	}
}

[Title( "Arrow - Stretch" ), Icon( "arrow_right_alt" )]
[Description( "Arrow stretches when you drag it. Has clamping." )]
internal class ArrowStretch : ISceneTest
{
	float someNumber;

	public void Frame()
	{
		Gizmo.Draw.Color = Gizmo.Colors.Red;
		if ( Gizmo.Control.UpdateArrow( "my-arrow", Vector3.Up, out var distance ) )
		{
			someNumber += distance;
		}

		someNumber = someNumber.Clamp( 0, 50 );
		Gizmo.Draw.Arrow( "my-arrow", Vector3.Up, length: 10.0f + someNumber );

		Gizmo.Draw.Color = Color.Black;
		Gizmo.Draw.Text( $"Value: {someNumber:n0}", new Transform( Vector3.Down * 10.0f ) );
	}
}


[Title( "Arrow - Move" ), Icon( "arrow_right_alt" )]
[Description( "Arrow moves when you drag it. Has clamping." )]
internal class ArrowMove : ISceneTest
{
	float someNumber;

	public void Frame()
	{
		Gizmo.Draw.Color = Gizmo.Colors.Green;
		if ( Gizmo.Control.UpdateArrow( "my-arrow", Vector3.Up, out var distance, axisOffset: someNumber ) )
		{
			someNumber += distance;
		}

		someNumber = someNumber.Clamp( 0, 50 );
		Gizmo.Draw.Arrow( "my-arrow", Vector3.Up, axisOffset: someNumber, length: 20.0f );

		Gizmo.Draw.Color = Color.Black;
		Gizmo.Draw.Text( $"Value: {someNumber:n0}", new Transform( Vector3.Down * 10.0f ) );
	}
}
