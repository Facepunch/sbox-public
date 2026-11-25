namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		internal void PhysicsOutline( PhysicsGroupDescription physics )
		{
			for ( var i = 0; i < physics.Parts.Count; ++i )
			{
				var body = physics.Parts[i];
				using var scope = PushScope( "part ", body.GetHashCode(), body.Transform );
				// The resource already stores shapes in sphere/capsule/hull/mesh order.
				// Its typed convenience properties materialize a new list on every access.
				for ( var j = 0; j < body.Parts.Count; ++j )
				{
					switch ( body.Parts[j] )
					{
						case PhysicsGroupDescription.BodyPart.SpherePart sphere: LineSphere( sphere.Sphere ); break;
						case PhysicsGroupDescription.BodyPart.CapsulePart capsule: LineCapsule( capsule.Capsule ); break;
						case PhysicsGroupDescription.BodyPart.HullPart hull: Lines( hull.GetGizmoLines() ); break;
						case PhysicsGroupDescription.BodyPart.MeshPart mesh: LineTriangles( mesh.GetGizmoTriangles() ); break;
					}
				}
			}
		}
	}
}
