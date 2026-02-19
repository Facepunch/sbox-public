using System;

namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>
		/// A front left up position movement widget. If widget was moved then will return true and out will return the new position.
		/// </summary>
		public bool Scale( string name, float value, out float outValue )
		{
			using var scaler = PushFixedScale();

			var screenScale = 1.0f;
			var localRotation = Transform.Rotation;
			outValue = value;

			using ( Sandbox.Gizmo.Scope( name ) )
			{
				float movement = 0.0f;
				Sandbox.Gizmo.Draw.IgnoreDepth = true;

				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Up;
				if ( Arrow( "up", Vector3.Up, out var xdist, head: "box" ) )
				{
					movement += xdist;
				}

				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Left;
				if ( Arrow( "left", Vector3.Left, out var ydist, head: "box" ) )
				{
					movement += ydist;
				}

				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Forward;
				if ( Arrow( "forward", Vector3.Forward, out var zdist, head: "box" ) )
				{
					movement += zdist;
				}

				if ( movement == 0.0f )
				{
					return false;
				}

				outValue += movement * screenScale * 0.01f;
				return true;

			}
		}

		/// <summary>
		/// A front left up position movement widget. If widget was moved then will return true and out will return the new position.
		/// </summary>
		public bool Scale( string name, Vector3 value, out Vector3 outValue, global::Rotation? axisRotation = null, float squareSize = 3.0f )
		{
			using var scaler = PushFixedScale();

			var screenScale = 1.0f;
			var localRotation = Transform.Rotation;
			var axis = axisRotation ?? global::Rotation.Identity;
			outValue = value;

			using var x = Sandbox.Gizmo.Scope( name, Vector3.Zero, axis );

			if ( Settings.GlobalSpace )
				Transform = Transform.WithRotation( global::Rotation.Identity );

			using ( Sandbox.Gizmo.Scope( name ) )
			{
				Vector3 movement = 0.0f;
				Sandbox.Gizmo.Draw.IgnoreDepth = true;

				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Up;
				if ( Arrow( "up", Vector3.Up, out var xdist, head: "box" ) )
				{
					movement += xdist * Vector3.Up;
				}

				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Left;
				if ( Arrow( "left", Vector3.Left, out var ydist, head: "box" ) )
				{
					movement += ydist * Vector3.Left;
				}

				Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Forward;
				if ( Arrow( "forward", Vector3.Forward, out var zdist, head: "box" ) )
				{
					movement += zdist * Vector3.Forward;
				}

				if ( squareSize > 0 )
				{
					float squareOffset = squareSize * 3.0f;
					using var scope = Sandbox.Gizmo.Scope( "squares" );
					Hitbox.DepthBias *= 0.8f;

					using ( Sandbox.Gizmo.Scope() )
					{
						Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Zero ) );
						Sandbox.Gizmo.Draw.Color = Color.White;
						if ( DragBox( "center", squareSize, global::Rotation.Identity, out var moved ) )
						{
							movement += moved;
						}
					}

					using ( Sandbox.Gizmo.Scope() )
					{
						Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Up * squareOffset + Vector3.Left * squareOffset ) );
						Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Up;
						if ( DragSquare( "left-up", squareSize, global::Rotation.LookAt( Vector3.Backward, Vector3.Up ), out var moved ) )
						{
							movement += moved;
						}
					}

					using ( Sandbox.Gizmo.Scope() )
					{
						Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Forward * squareOffset + Vector3.Left * squareOffset ) );
						Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Left;
						if ( DragSquare( "forward-left", squareSize, global::Rotation.LookAt( Vector3.Up, Vector3.Forward ), out var moved ) )
						{
							movement += moved;
						}
					}

					using ( Sandbox.Gizmo.Scope() )
					{
						Sandbox.Gizmo.Transform = Sandbox.Gizmo.Transform.ToWorld( new Transform( Vector3.Forward * squareOffset + Vector3.Up * squareOffset ) );
						Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Colors.Forward;
						if ( DragSquare( "forward-up", squareSize, global::Rotation.LookAt( Vector3.Left, Vector3.Down ), out var moved ) )
						{
							movement += moved;
						}
					}
				}

				if ( movement == 0.0f )
				{
					return false;
				}

				outValue += movement * screenScale * 0.01f;
				return true;

			}
		}

	}
}
