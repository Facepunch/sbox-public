namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		sealed class BoxDrag
		{
			internal string Path;
			internal BBox Start;
			internal BBox Delta;
		}

		public bool UpdateBoundingBox( string name, BBox value, out BBox outValue )
			=> UpdateBoundingBox( name, value, out outValue, out _, out _ );

		public bool UpdateBoundingBox( string name, BBox value, out BBox outValue, out bool outPressed, out Vector3 outResizeAxis )
		{
			outValue = value;
			outPressed = false;
			outResizeAxis = default;
			using var scope = PushScope( name );
			var path = Active.current.PressedPath;
			if ( path is null || !path.StartsWith( Path ) || (!IsLeftMouseDown && !Active.HasReleaseInput) ) return false;
			var direction = path.AsSpan( Path.Length ) switch
			{
				"/Forward/WorldScale" => Vector3.Forward,
				"/Backward/WorldScale" => Vector3.Backward,
				"/Up/WorldScale" => Vector3.Up,
				"/Down/WorldScale" => Vector3.Down,
				"/Left/WorldScale" => Vector3.Left,
				"/Right/WorldScale" => Vector3.Right,
				_ => Vector3.Zero
			};
			if ( direction == Vector3.Zero ) return false;
			outPressed = true;
			var drag = Active.GetValue<BoxDrag>( "gizmo.bounding-box.drag" );
			if ( drag is null )
			{
				drag = new BoxDrag();
				Active.SetValue( "gizmo.bounding-box.drag", drag );
			}
			if ( WasLeftMousePressed || drag.Path != path )
			{
				drag.Path = path;
				drag.Start = value;
				drag.Delta = default;
			}

			var scale = Transform.UniformScale;
			var opposite = value.Center - direction * value.Size * 0.5f;
			Transform = Transform.ToWorld( new Transform( opposite, Rotation.LookAt( direction, Vector3.Up ) ) );
			Transform = Transform.WithRotation( Rotation.LookAt( Transform.Rotation.Forward, Camera.Rotation.Forward ) );
			Path = path;
			var distance = Vector3.Forward.Dot( GetMouseDelta( 0, Vector3.Up ) ) / scale;
			if ( distance.AlmostEqual( 0 ) ) return false;
			outResizeAxis = direction;
			drag.Delta.Maxs += Vector3.Max( direction, Vector3.Zero ) * distance;
			drag.Delta.Mins += Vector3.Min( direction, Vector3.Zero ) * distance;
			var mins = drag.Start.Mins + drag.Delta.Mins;
			var maxs = drag.Start.Maxs + drag.Delta.Maxs;
			if ( Settings.SnapToGrid != IsCtrlPressed )
			{
				mins = Snap( mins, drag.Delta.Mins );
				maxs = Snap( maxs, drag.Delta.Maxs );
			}
			outValue = new BBox { Mins = mins, Maxs = maxs };
			return true;
		}
	}

	public sealed partial class GizmoDraw
	{
		public void BoundingBox( string name, BBox value )
		{
			using var scope = PushScope( name, value.Center );
			var halfSize = value.Size * 0.5f;
			BoxFace( "Forward", Vector3.Forward, halfSize.x, Colors.Forward );
			BoxFace( "Backward", Vector3.Backward, halfSize.x, Colors.Forward );
			BoxFace( "Up", Vector3.Up, halfSize.z, Colors.Up );
			BoxFace( "Down", Vector3.Down, halfSize.z, Colors.Up );
			BoxFace( "Left", Vector3.Left, halfSize.y, Colors.Left );
			BoxFace( "Right", Vector3.Right, halfSize.y, Colors.Left );
		}

		void BoxFace( string name, Vector3 direction, float length, Color color )
		{
			using var scope = PushScope( name, direction * length, Rotation.LookAt( direction, Vector3.Up ) );
			using var scaler = GizmoControls.PushFixedScaleScope();
			if ( !Pressed.This && length.AlmostEqual( 0 ) ) return;
			const float radius = 1.3f;
			var sphereRadius = IsHovered ? radius * 1.25f : radius;
			var arrowRadius = sphereRadius * 0.9f;
			var arrowLength = arrowRadius * 2;
			Hitbox.DepthBias = 0.01f;
			Hitbox.Sphere( new Sphere( 0, radius * 1.25f ) );
			var from = Vector3.Forward * -(arrowLength + sphereRadius);
			Hitbox.Sphere( new Sphere( from / 2, radius * 0.9f * 1.25f ) );
			IgnoreDepth = true;
			Color = IsHovered || Pressed.This ? Colors.Active : color;
			SolidSphere( Vector3.Zero, sphereRadius );
			Arrow( from, Vector3.Forward * -sphereRadius, arrowLength, arrowRadius );
		}
	}
}
