namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		public bool UpdateScale( string name, float value, out float outValue )
		{
			var changed = UpdateScale( name, Vector3.Zero, out var delta, squareSize: 0 );
			outValue = value + delta.x + delta.y + delta.z;
			return changed;
		}

		public bool UpdateScale( string name, Vector3 value, out Vector3 outValue, Rotation? axisRotation = null, float squareSize = 3 )
		{
			using var scaler = PushFixedScale();
			using var scope = Scope( name, Vector3.Zero, axisRotation ?? Rotation.Identity );
			if ( Settings.GlobalSpace && axisRotation is null )
				Transform = Transform.WithRotation( Rotation.Identity );
			using var inner = Scope( name );

			Vector3 movement = Vector3.Zero;
			if ( UpdateArrow( "up", Vector3.Up, out var up ) ) movement += Vector3.Up * up;
			if ( UpdateArrow( "left", Vector3.Left, out var left ) ) movement += Vector3.Left * left;
			if ( UpdateArrow( "forward", Vector3.Forward, out var forward ) ) movement += Vector3.Forward * forward;

			if ( squareSize > 0 )
			{
				using var squares = Scope( "squares" );
				var offset = squareSize * 3;
				using ( Scope() )
				{
					if ( UpdateDragBox( "center", Rotation.Identity, out var delta ) ) movement += delta;
				}
				using ( Scope() )
				{
					Transform = Transform.ToWorld( new Transform( (Vector3.Up + Vector3.Left) * offset ) );
					if ( UpdateDragSquare( "left-up", Rotation.LookAt( Vector3.Backward, Vector3.Up ), out var delta ) ) movement += delta;
				}
				using ( Scope() )
				{
					Transform = Transform.ToWorld( new Transform( (Vector3.Forward + Vector3.Left) * offset ) );
					if ( UpdateDragSquare( "forward-left", Rotation.LookAt( Vector3.Up, Vector3.Forward ), out var delta ) ) movement += delta;
				}
				using ( Scope() )
				{
					Transform = Transform.ToWorld( new Transform( (Vector3.Forward + Vector3.Up) * offset ) );
					if ( UpdateDragSquare( "forward-up", Rotation.LookAt( Vector3.Left, Vector3.Down ), out var delta ) ) movement += delta;
				}
			}

			outValue = value + movement * 0.01f;
			return movement != Vector3.Zero;
		}
	}

	public sealed partial class GizmoDraw
	{
		public void Scale( string name, Rotation? axisRotation = null, float squareSize = 3 )
		{
			using var scaler = GizmoControls.PushFixedScale();
			using var scope = Scope( name, Vector3.Zero, axisRotation ?? Rotation.Identity );
			if ( Settings.GlobalSpace && axisRotation is null )
				Transform = Transform.WithRotation( Rotation.Identity );
			using var inner = Scope( name );
			IgnoreDepth = true;
			Color = Colors.Up;
			Arrow( "up", Vector3.Up, head: "box" );
			Color = Colors.Left;
			Arrow( "left", Vector3.Left, head: "box" );
			Color = Colors.Forward;
			Arrow( "forward", Vector3.Forward, head: "box" );
			if ( squareSize <= 0 ) return;
			using var squares = Scope( "squares" );
			Hitbox.DepthBias *= 0.8f;
			using ( Scope() )
			{
				Color = Color.White;
				DragBox( "center", squareSize, Rotation.Identity );
			}
			DrawAxisSquares( squareSize );
		}
	}
}
