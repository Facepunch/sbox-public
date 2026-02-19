namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>
		/// A scalable sphere gizmo. Returns true if the gizmo was interacted with and outValue will return the new radius.
		/// </summary>
		public bool Sphere( string name, float radius, out float outRadius, Color color )
		{
			outRadius = radius;
			var localCameraRot = Transform.RotationToLocal( Camera.Rotation );

			Sandbox.Gizmo.Draw.IgnoreDepth = true;
			using ( var scope = Sandbox.Gizmo.Scope( name, new Transform( 0, localCameraRot ) ) )
			{
				float movement = 0.0f;

				Sandbox.Gizmo.Draw.LineThickness = 4.0f;
				Sandbox.Gizmo.Draw.Color = color;

				if ( !Sandbox.Gizmo.IsHovered )
					Sandbox.Gizmo.Draw.Color = Sandbox.Gizmo.Draw.Color.Darken( 0.33f );

				if ( Pressed.Any && !Pressed.This )
					return false;

				using ( Hitbox.LineScope() )
				{
					var delta = Sandbox.Gizmo.GetMouseDistanceDelta( 0, global::Rotation.FromAxis( localCameraRot.Forward, 0 ).Forward );

					if ( Pressed.This )
					{
						movement += delta;
					}

					Gizmo.Draw.LineCircle( 0, radius, sections: (int)radius * 2 );
				}

				if ( movement != 0.0f )
				{
					outRadius = radius + movement;
					return true;
				}

				return false;
			}
		}
	}
}
