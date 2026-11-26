using System.ComponentModel;

namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		public bool Sphere( string name, float radius, out float outRadius, Color color )
		{
			var changed = UpdateSphere( name, radius, out outRadius );
			Draw.Sphere( name, outRadius, color );
			return changed;
		}

		public bool Capsule( string name, Capsule capsule, out Capsule outCapsule, Color color )
		{
			var changed = UpdateCapsule( name, capsule, out outCapsule );
			Draw.Capsule( name, outCapsule, color );
			return changed;
		}

		public bool BoundingBox( string name, BBox value, out BBox outValue )
			=> BoundingBox( name, value, out outValue, out _, out _ );

		public bool BoundingBox( string name, BBox value, out BBox outValue, out bool outPressed )
			=> BoundingBox( name, value, out outValue, out outPressed, out _ );

		public bool BoundingBox( string name, BBox value, out BBox outValue, out bool outPressed, out Vector3 outResizeAxis )
		{
			var changed = UpdateBoundingBox( name, value, out outValue, out outPressed, out outResizeAxis );
			Draw.BoundingBox( name, outValue );
			return changed;
		}

		public bool Rotate( string name, Rotation value, out Rotation newValue )
		{
			var changed = UpdateRotation( name, value, out newValue );
			Draw.Rotate( name );
			return changed;
		}

		public bool RotateSingle( string name, Color color, out float angleDelta, float size = 19, bool useHalfCircle = true )
		{
			var changed = UpdateRotationAxis( name, out angleDelta );
			Draw.RotationAxis( name, color, size, useHalfCircle );
			return changed;
		}

		public bool Scale( string name, float value, out float outValue )
		{
			var changed = UpdateScale( name, value, out outValue );
			Draw.Scale( name, squareSize: 0 );
			return changed;
		}

		public bool Scale( string name, Vector3 value, out Vector3 outValue, Rotation? axisRotation = null, float squareSize = 3 )
		{
			var changed = UpdateScale( name, value, out outValue, axisRotation, squareSize );
			Draw.Scale( name, axisRotation, squareSize );
			return changed;
		}

		public bool Position( string name, Vector3 position, out Vector3 newPos, Rotation? axisRotation = null, float squareSize = 3, Func<Vector3?> centerRaycast = null )
		{
			var changed = UpdatePosition( name, position, out newPos, axisRotation, squareSize, centerRaycast );
			Draw.Position( name, axisRotation, squareSize, centerRaycast is not null );
			return changed;
		}

		[EditorBrowsable( EditorBrowsableState.Never ), Obsolete( "Use the overload with a centerRaycast callback" )]
		public bool Position( string name, Vector3 position, out Vector3 newPos, Rotation? axisRotation, float squareSize )
			=> Position( name, position, out newPos, axisRotation, squareSize, null );

		public bool Arrow( string name, Vector3 axis, out float distance, float length = 24, float girth = 6, float axisOffset = 2, float cullAngle = 10, float snapSize = 0, string head = "cone" )
		{
			var changed = UpdateArrow( name, axis, out distance, axisOffset, cullAngle );
			Draw.Arrow( name, axis, length, girth, axisOffset, cullAngle, snapSize, head );
			return changed;
		}

		public bool DragSquare( string name, Vector2 size, Rotation rotation, out Vector3 movement, Action drawHandle = null )
		{
			var changed = UpdateDragSquare( name, rotation, out movement );
			Draw.DragSquare( name, size, rotation, drawHandle );
			return changed;
		}

		public bool DragBox( string name, Vector3 size, Rotation rotation, out Vector3 movement )
		{
			var changed = UpdateDragBox( name, rotation, out movement );
			Draw.DragBox( name, size, rotation );
			return changed;
		}

		public bool DragCenterRaycast( string name, float size, Func<Vector3?> raycast, out Vector3 movement )
		{
			var changed = UpdateDragCenterRaycast( name, raycast, out movement );
			Draw.DragCenter( name, size );
			return changed;
		}
	}
}
