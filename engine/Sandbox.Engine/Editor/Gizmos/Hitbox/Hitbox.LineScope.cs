using Sandbox.Utility;

namespace Sandbox;

public static partial class Gizmo
{
	internal struct HitboxLineScope
	{
		public bool Enabled { get; set; }
		public string LinePath { get; set; }
	}

	public sealed partial class GizmoHitbox
	{
		/// <summary>
		/// Start a line scope. Any drawn lines should become a hitbox during this scope.
		/// </summary>
		public IDisposable LineScope()
		{
			var old = Active.lineScope;

			Active.lineScope.Enabled = true;
			Active.lineScope.LinePath = Path;

			unsafe
			{
				static void RestoreLineScope( HitboxLineScope old )
				{
					Active.lineScope = old;
				}

				return new DisposeAction<HitboxLineScope>( &RestoreLineScope, old );
			}
		}



		/// <summary>
		/// If we're in a hitbox linescope we'll distance this test vs the current ray. If
		/// not, we'll return immediately.
		/// This is automatically called when rendering lines
		/// </summary>
		public void AddPotentialLine( in Vector3 p0, in Vector3 p1, float thickness )
		{
			if ( !Active.lineScope.Enabled )
				return;
			if ( Record( new( HitShapeType.Line, p0, p1, thickness ) ) ) return;

			var ray = LocalRay;

			var line = new Line( p0, p1 );

			if ( !line.ClosestPoint( ray, out var point_on_line, out var point_on_ray ) )
				return;

			var lineDist = (point_on_line - point_on_ray).Length;
			if ( lineDist > thickness * 0.5f )
				return;

			var position = Transform.PointToWorld( point_on_line );
			var distance = Camera.Ortho ? Camera.OrthoHeight : Camera.Position.Distance( position );

			// Here we try to balance world distance with the distance from the line

			TrySetHovered( distance + lineDist * 10.0f );
		}
	}
}
