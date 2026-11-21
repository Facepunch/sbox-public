using System.Runtime.InteropServices;

namespace Sandbox;

public sealed partial class PolygonMesh
{
	internal bool UsesPerMaterialCollision( MeshComponent.CollisionType collision ) => TriangulationTransform.HasValue &&
		(CollisionTolerance > 0 || collision != MeshComponent.CollisionType.Mesh);

	internal void AddCollision( ModelBuilder builder, MeshComponent.CollisionType collision, List<Vector3> positions, List<int> indices, List<byte> materials )
	{
		if ( collision == MeshComponent.CollisionType.None ) return;
		if ( !UsesPerMaterialCollision( collision ) )
		{
			AddCollisionShape( builder, collision, CollectionsMarshal.AsSpan( positions ), CollectionsMarshal.AsSpan( indices ), CollectionsMarshal.AsSpan( materials ) );
			return;
		}

		// Fit each render material independently. Only triangle meshes use byte-sized surface slots.
		var groups = new Dictionary<int, (byte Slot, List<int> Corners)>();
		for ( int triangle = 0; triangle < materials.Count; triangle++ )
		{
			var material = MaterialIndex[_triangleFaces[triangle]];
			if ( !groups.TryGetValue( material, out var group ) )
				groups.Add( material, group = (materials[triangle], []) );
			for ( int i = 0; i < 3; i++ )
				group.Corners.Add( indices[triangle * 3 + i] );
		}

		foreach ( var (material, group) in groups )
			MeshCollisionFitting.AddSurfaceShape( builder, collision, positions, group.Corners, group.Slot, GetMaterial( material )?.Surface, TriangulationTransform.Value, CollisionTolerance );
	}

	// Hammer supplies map-space vertices. The physics body stores the fitted shape in local coordinates.
	internal static void AddCollisionShape( ModelBuilder builder, MeshComponent.CollisionType collision, Span<Vector3> points,
		Span<int> indices, Span<byte> materials, Transform? placement = null, Surface surface = null )
	{
		if ( indices.IsEmpty ) return;
		if ( collision == MeshComponent.CollisionType.Sphere )
		{
			if ( FitCollisionSphere( points, indices, requirePositiveVolume: placement.HasValue ) is { } sphere )
				builder.AddBody( surface: surface ).AddSphere( new Sphere( LocalPoint( sphere.Center ), LocalRadius( sphere.Radius ) ) );
			return;
		}
		if ( collision == MeshComponent.CollisionType.Capsule )
		{
			if ( FitCollisionCapsule( points, indices ) is { } capsule )
				builder.AddBody( surface: surface ).AddCapsule( new Capsule( LocalPoint( capsule.CenterA ), LocalPoint( capsule.CenterB ), LocalRadius( capsule.Radius ) ) );
			return;
		}

		if ( placement is { } transform )
			for ( int i = 0; i < points.Length; i++ ) points[i] = transform.PointToLocal( points[i] );

		switch ( collision )
		{
			case MeshComponent.CollisionType.Mesh:
				builder.AddCollisionMesh( points, indices, materials );
				break;
			case MeshComponent.CollisionType.Hull:
				if ( placement.HasValue )
					builder.AddBody( surface: surface ).AddHull( points );
				else
					builder.AddCollisionHull( points );
				break;
		}

		Vector3 LocalPoint( Vector3 point ) => placement?.PointToLocal( point ) ?? point;
		float LocalRadius( float radius ) => placement is { UniformScale: not 0 } map ? radius / MathF.Abs( map.UniformScale ) : radius;
	}

	// Hammer places a collision sphere at the surface centroid and preserves the mesh volume.
	internal static Sphere? FitCollisionSphere( ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, bool requirePositiveVolume = false )
	{
		float area = 0, volume = 0, projectedArea = 0, signedArea = 0;
		var center = Vector3.Zero;
		for ( int i = 0; i < indices.Length; i += 3 )
		{
			var a = positions[indices[i]];
			var b = positions[indices[i + 1]];
			var c = positions[indices[i + 2]];
			var normal = (b - a).Cross( c - a );
			float weight = normal.Length;
			area += weight;
			center += weight * ((a + b + c) / 3);
			signedArea += normal.z;
			projectedArea += MathF.Abs( normal.z );
			volume += (a.z + b.z + c.z) / 6 * normal.z;
		}
		if ( area == 0 || projectedArea == 0 || MathF.Abs( signedArea ) > 0.001f * projectedArea || volume == 0 )
			return null;
		// Hammer fits using signed volume. Its compiled sphere stores zero radius for inward-facing meshes.
		float radius = requirePositiveVolume && volume < 0 ? 0 : MathF.Cbrt( MathF.Abs( volume ) * 3 / (4 * MathF.PI) );
		return new Sphere( center / area, radius );
	}

	// The capsule axes follow the covariance of the triangle surfaces. Sampling only
	// vertices would make its orientation depend on how densely each face was divided.
	internal static Capsule? FitCollisionCapsule( ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices )
	{
		float area = 0;
		Vector3 center = default;
		for ( int i = 0; i < indices.Length; i += 3 )
		{
			var a = vertices[indices[i]];
			var b = vertices[indices[i + 1]];
			var c = vertices[indices[i + 2]];
			float weight = (b - a).Cross( c - a ).Length;
			area += weight;
			center += weight * ((a + b + c) / 3);
		}
		if ( area == 0 ) return null;
		center /= area;

		Span<Vector3> covariance = stackalloc Vector3[3];
		covariance.Clear();
		for ( int i = 0; i < indices.Length; i += 3 )
		{
			var a = vertices[indices[i]] - center;
			var b = vertices[indices[i + 1]] - center;
			var c = vertices[indices[i + 2]] - center;
			var sum = a + b + c;
			float weight = (b - a).Cross( c - a ).Length / 24;
			for ( int j = 0; j < 3; j++ )
				covariance[j] += weight * (a[j] * a + b[j] * b + c[j] * c + sum[j] * sum);
		}

		var basis = PrincipalAxes( covariance );
		var bounds = BBox.FromPositionAndSize( basis.Inverse * (vertices[0] - center), 0 );
		foreach ( var vertex in vertices ) bounds = bounds.AddPoint( basis.Inverse * (vertex - center) );
		var size = bounds.Size;
		int axis = size.x > size.y && size.x > size.z ? 0 : size.y > size.z ? 1 : 2;
		float radius = 0.5f * MathF.Sqrt( size[(axis + 1) % 3] * size[(axis + 2) % 3] );
		if ( radius <= 0 ) return null;
		center += basis * bounds.Center;
		Vector3 half = default;
		half[axis] = MathF.Max( 1, size[axis] * 0.5f - radius );
		return new Capsule( center - basis * half, center + basis * half, radius );
	}

	// Jacobi rotations diagonalize the symmetric covariance matrix.
	static Rotation PrincipalAxes( ReadOnlySpan<Vector3> covariance )
	{
		var rotation = Rotation.Identity;
		Span<Vector3> axes = stackalloc Vector3[3];
		Span<Vector3> products = stackalloc Vector3[3];
		for ( int step = 0; step < 24; step++ )
		{
			axes[0] = rotation.Forward;
			axes[1] = rotation.Left;
			axes[2] = rotation.Up;
			for ( int i = 0; i < 3; i++ )
				products[i] = new( covariance[0].Dot( axes[i] ), covariance[1].Dot( axes[i] ), covariance[2].Dot( axes[i] ) );
			var off = new Vector3( axes[1].Dot( products[2] ), axes[0].Dot( products[2] ), axes[0].Dot( products[1] ) );
			var mag = off.Abs();
			int k = mag.x > mag.y && mag.x > mag.z ? 0 : mag.y > mag.z ? 1 : 2;
			if ( off[k] == 0 ) break;
			int a = (k + 1) % 3, b = (k + 2) % 3;
			float theta = (axes[b].Dot( products[b] ) - axes[a].Dot( products[a] )) / (2 * off[k]);
			float t = 1 / (MathF.Abs( theta ) + MathF.Sqrt( theta * theta + 1 ));
			float cos = 1 / MathF.Sqrt( t * t + 1 );
			float sinHalf = (theta > 0 ? -1 : 1) * MathF.Sqrt( (1 - cos) * 0.5f );
			float cosHalf = MathF.Sqrt( 1 - sinHalf * sinHalf );
			if ( cosHalf == 1 ) break;
			Vector3 axis = default;
			axis[k] = sinHalf;
			rotation = (rotation * new Rotation( axis.x, axis.y, axis.z, cosHalf )).Normal;
		}
		return rotation;
	}
}
