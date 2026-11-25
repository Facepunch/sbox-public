namespace Editor;

static class WorkPlane
{
	public static Transform Current => SceneEditorSession.Active?.WorkPlane ?? Transform.Zero;
	public static bool IsActive => Current != Transform.Zero;

	public static Vector3 Snap( Transform frame, Vector3 point, float spacing )
	{
		return frame.PointToWorld( frame.PointToLocal( point ).SnapToGrid( spacing ) );
	}

	public static Vector3 Snap( Vector3 point )
	{
		return Gizmo.Settings.SnapToGrid != Gizmo.IsCtrlPressed
			? Snap( Current, point, Gizmo.Settings.GridSpacing )
			: point;
	}

	public static Vector3 SnapTranslation( Vector3 start, Vector3 movement, Rotation basis, bool globalSpace )
	{
		var origin = globalSpace ? Current.Position : Vector3.Zero;
		return origin + Gizmo.Snap( start - origin, movement, basis );
	}

	public static Rotation AlignNormal( Vector3 normal, Rotation basis )
	{
		normal = normal.Normal;
		var forward = basis.Forward - normal * basis.Forward.Dot( normal );
		if ( forward.LengthSquared < 0.0001f )
			forward = basis.Left - normal * basis.Left.Dot( normal );
		return Rotation.LookAt( forward.Normal, normal );
	}

	public static bool TryPickSurface( SceneTraceResult hit, out Transform frame )
	{
		frame = Transform.Zero;
		if ( !hit.Hit || hit.Normal.LengthSquared < 0.0001f )
			return false;

		var origin = hit.EndPosition;
		var normal = hit.Normal;
		var basis = hit.GameObject.IsValid() ? hit.GameObject.WorldRotation : Rotation.Identity;

		if ( hit.Component is MeshComponent { Mesh: { } mesh } component )
		{
			var face = mesh.TriangleToFace( hit.Triangle );
			if ( face.IsValid )
			{
				normal = GetFaceNormal( component, face, hit.Normal );
				if ( normal.LengthSquared < 0.0001f )
					return false;

				var nearest = float.MaxValue;
				foreach ( var edge in mesh.GetFaceEdges( face ) )
				{
					var line = mesh.GetEdgeLine( edge );
					var start = component.WorldTransform.PointToWorld( line.Start );
					var end = component.WorldTransform.PointToWorld( line.End );
					var direction = end - start;
					var distance = (start - hit.EndPosition).LengthSquared;
					if ( distance >= nearest || direction.LengthSquared < 0.0001f )
						continue;

					nearest = distance;
					origin = start;
					basis = Rotation.LookAt( direction.Normal, normal );
				}
			}
		}

		frame = new Transform( origin, AlignNormal( normal, basis ) );
		return true;
	}

	static Vector3 GetFaceNormal( MeshComponent component, HalfEdgeMesh.FaceHandle face, Vector3 hitNormal )
	{
		var transform = component.WorldTransform;
		var scale = transform.Scale;
		if ( MathF.Abs( scale.x ) < 0.0001f || MathF.Abs( scale.y ) < 0.0001f || MathF.Abs( scale.z ) < 0.0001f )
			return Vector3.Zero;

		component.Mesh.ComputeFaceNormal( face, out var localNormal );
		var normal = (transform.Rotation * (localNormal / scale)).Normal;
		return normal.Dot( hitNormal ) < 0 ? -normal : normal;
	}

	public static bool TryGetPlacement( SceneTrace trace, Transform frame, bool usePlane, out Vector3 point, out Vector3 normal )
	{
		if ( !usePlane )
		{
			var hit = trace.Run();
			if ( hit.Hit )
			{
				point = hit.EndPosition;
				normal = hit.Normal;
				if ( hit.Component is MeshComponent { Mesh: { } mesh } component )
				{
					var face = mesh.TriangleToFace( hit.Triangle );
					if ( face.IsValid )
					{
						normal = GetFaceNormal( component, face, normal );
						if ( normal.LengthSquared < 0.0001f )
							return false;
						var center = component.WorldTransform.PointToWorld( mesh.GetFaceCenter( face ) );
						point = new Plane( center, normal ).SnapToPlane( point );
					}
				}
				return true;
			}
		}

		normal = Gizmo.Camera.Ortho ? -Gizmo.Camera.Rotation.Forward : frame.Rotation.Up;
		return new Plane( frame.Position, normal ).TryTrace( Gizmo.CurrentRay, out point, true );
	}
}
