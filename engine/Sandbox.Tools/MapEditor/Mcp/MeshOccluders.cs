using Editor.MapDoc;
using System;

namespace Editor.Mcp;

/// <summary>
/// The map's opaque mesh triangles, for working out whether a point is enclosed by solid geometry.
/// A plain managed ray caster - deterministic, and independent of Hammer's own trace.
/// </summary>
internal sealed class MeshOccluders
{
	readonly record struct Triangle( Vector3 A, Vector3 Edge1, Vector3 Edge2, Vector3 Normal );

	sealed class Group
	{
		public BBox Bounds;
		public Triangle[] Triangles;
	}

	readonly List<Group> groups = new();

	/// <summary>
	/// Every face of every mesh, fetched once - reading faces goes through native, so callers reuse these.
	/// </summary>
	public Dictionary<MapMesh, IReadOnlyList<MapMeshFace>> Faces { get; } = new();

	public MeshOccluders( IEnumerable<MapMesh> meshes )
	{
		var occludes = new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );

		foreach ( var mesh in meshes )
		{
			var faces = mesh.GetFaces();
			Faces[mesh] = faces;

			var triangles = new List<Triangle>();
			var points = new List<Vector3>();

			foreach ( var face in faces )
			{
				var material = face.Material ?? "";

				if ( !occludes.TryGetValue( material, out var isOccluder ) )
					occludes[material] = isOccluder = IsOccluderMaterial( material );

				if ( !isOccluder || face.Vertices.Count < 3 )
					continue;

				var a = face.Vertices[0];

				for ( int i = 1; i + 1 < face.Vertices.Count; i++ )
				{
					triangles.Add( new Triangle( a, face.Vertices[i] - a, face.Vertices[i + 1] - a, face.Normal ) );
				}

				points.AddRange( face.Vertices );
			}

			if ( triangles.Count == 0 )
				continue;

			var bounds = BBox.FromPoints( points );
			groups.Add( new Group { Bounds = new BBox( bounds.Mins - 0.1f, bounds.Maxs + 0.1f ), Triangles = triangles.ToArray() } );
		}
	}

	/// <summary>
	/// Whether a material blocks the view - not a tools material, and not see-through.
	/// </summary>
	public static bool IsOccluderMaterial( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;

		var material = Material.Load( path );
		if ( material is null )
			return false;

		var flags = material.Flags;
		return flags.GetInt( "tools.toolsmaterial" ) == 0 && !flags.IsTranslucent && !flags.IsAlphaTest;
	}

	/// <summary>
	/// Whether the ray leaves through the back of a face before it reaches the front of any - which is
	/// what happens when it starts inside a closed solid. Front and back faces at the same distance, like
	/// a two sided sheet, don't count.
	/// </summary>
	public bool StartsInside( Vector3 start, Vector3 direction, float maxDistance = 16384 )
	{
		const float Tie = 0.1f;

		var nearestFront = float.MaxValue;
		var nearestBack = float.MaxValue;

		foreach ( var group in groups )
		{
			if ( !RayEntersBox( start, direction, group.Bounds, maxDistance, out var entry ) )
				continue;

			// Nothing in this group can land before what we've already found
			if ( entry > MathF.Min( nearestFront, nearestBack ) + Tie )
				continue;

			foreach ( var triangle in group.Triangles )
			{
				if ( !Intersect( start, direction, triangle, out var distance ) || distance > maxDistance )
					continue;

				if ( Vector3.Dot( triangle.Normal, direction ) > 0 )
					nearestBack = MathF.Min( nearestBack, distance );
				else
					nearestFront = MathF.Min( nearestFront, distance );
			}
		}

		return nearestBack < float.MaxValue && nearestBack < nearestFront - Tie;
	}

	/// <summary>
	/// The distance to the nearest face pointing back at the ray - the first surface it would land on.
	/// </summary>
	public bool Raycast( Vector3 start, Vector3 direction, float maxDistance, out float distance )
	{
		distance = float.MaxValue;

		foreach ( var group in groups )
		{
			if ( !RayEntersBox( start, direction, group.Bounds, maxDistance, out var entry ) || entry > distance )
				continue;

			foreach ( var triangle in group.Triangles )
			{
				if ( Vector3.Dot( triangle.Normal, direction ) >= 0 )
					continue;

				if ( Intersect( start, direction, triangle, out var hit ) && hit <= maxDistance )
					distance = MathF.Min( distance, hit );
			}
		}

		return distance < float.MaxValue;
	}

	/// <summary>
	/// Two sided Möller-Trumbore, with a little slack on the edges so a ray down a shared edge still hits.
	/// </summary>
	static bool Intersect( Vector3 origin, Vector3 direction, in Triangle triangle, out float distance )
	{
		const float Slack = 1e-5f;
		distance = 0;

		var p = Vector3.Cross( direction, triangle.Edge2 );
		var determinant = Vector3.Dot( triangle.Edge1, p );

		if ( MathF.Abs( determinant ) < 1e-9f )
			return false;

		var inverse = 1.0f / determinant;
		var t = origin - triangle.A;

		var u = Vector3.Dot( t, p ) * inverse;
		if ( u < -Slack || u > 1 + Slack ) return false;

		var q = Vector3.Cross( t, triangle.Edge1 );
		var v = Vector3.Dot( direction, q ) * inverse;
		if ( v < -Slack || u + v > 1 + Slack ) return false;

		distance = Vector3.Dot( triangle.Edge2, q ) * inverse;
		return distance > 1e-4f;
	}

	static bool RayEntersBox( Vector3 origin, Vector3 direction, BBox box, float maxDistance, out float entry )
	{
		var near = 0f;
		var far = maxDistance;

		for ( int axis = 0; axis < 3; axis++ )
		{
			var o = origin[axis];
			var d = direction[axis];
			var min = box.Mins[axis];
			var max = box.Maxs[axis];

			if ( MathF.Abs( d ) < 1e-9f )
			{
				if ( o < min || o > max )
				{
					entry = 0;
					return false;
				}

				continue;
			}

			var t1 = (min - o) / d;
			var t2 = (max - o) / d;
			if ( t1 > t2 ) (t1, t2) = (t2, t1);

			near = MathF.Max( near, t1 );
			far = MathF.Min( far, t2 );

			if ( near > far )
			{
				entry = 0;
				return false;
			}
		}

		entry = near;
		return true;
	}
}
