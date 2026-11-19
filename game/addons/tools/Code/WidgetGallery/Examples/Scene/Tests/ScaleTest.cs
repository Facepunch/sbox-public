
using Sandbox.Utility;
using System;
using System.Diagnostics;

namespace Editor.Widgets.SceneTests;

[Title( "Scale" ), Icon( "fit_screen" )]
[Description( "Standard 1D scale gizmo.\nScale in Source2 is 1D so this is the most common scenario." )]
internal class ScaleSimple : ISceneTest
{
	float scale = 1.0f;

	public void Frame()
	{
		using ( Gizmo.Scope( "Scale", Transform.Zero.WithScale( scale ) ) )
		{
			Gizmo.Draw.Color = Color.White;
			if ( Gizmo.Control.UpdateScale( "scaler", scale, out var newscale ) )
			{
				scale = newscale;

				scale = scale.Clamp( 0.1f, 2.0f );
			}
			Gizmo.Transform = Transform.Zero.WithScale( scale );
			Gizmo.Draw.Model( "models/editor/playerstart.vmdl" );
			Gizmo.Draw.Scale( "scaler", squareSize: 0 );

		}

		Gizmo.Draw.Color = Color.Black;
		Gizmo.Draw.Text( $"Value: {scale:n0.00}", new Transform( Vector3.Down * 50.0f ) );
	}
}
