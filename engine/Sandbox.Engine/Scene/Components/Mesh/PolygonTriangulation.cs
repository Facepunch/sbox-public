using System;

namespace Sandbox;

internal static class PolygonTriangulation
{
	internal static int[] TriangulateFace( ReadOnlySpan<Vector3> local, Transform placement )
	{
		Span<Vector3> world = local.Length <= 128 ? stackalloc Vector3[local.Length] : new Vector3[local.Length];
		for ( int i = 0; i < local.Length; i++ ) world[i] = placement.PointToWorld( local[i] );
		return TriangulateFace( world );
	}

	internal static int[] TriangulateFace( ReadOnlySpan<Vector3> vertices )
	{
		if ( vertices.Length < 3 ) return [];

		// Hammer's DMX export starts a receiver face at its most orthogonal corner.
		// The first candidate is the end of the first edge, including when scores tie.
		int first = 0;
		float best = 1;
		for ( int i = 0; i < vertices.Length; i++ )
		{
			int corner = (i + 1) % vertices.Length;
			var a = (vertices[i] - vertices[corner]).Normal;
			var b = (vertices[(corner + 1) % vertices.Length] - vertices[corner]).Normal;
			float dot = MathF.Abs( a.Dot( b ) );
			if ( dot >= best ) continue;
			first = corner;
			best = dot;
			if ( best == 0 ) break;
		}
		if ( vertices.Length == 3 ) return [first, (first + 1) % 3, (first + 2) % 3];
		return Triangulate( vertices, PolygonNormal( vertices ), first );
	}

	internal static int[] Triangulate( ReadOnlySpan<Vector3> vertices, Vector3 normal, int first = 0 )
	{
		int count = vertices.Length;
		if ( count < 3 ) return [];
		var indices = new int[(count - 2) * 3];
		Span<int> remaining = count <= 128 ? stackalloc int[count] : new int[count];
		Span<float> scores = count <= 128 ? stackalloc float[count] : new float[count];
		for ( int i = 0; i < count; i++ ) remaining[i] = (first + i) % count;
		for ( int i = 0; i < count; i++ ) scores[i] = ScoreEar( vertices, remaining, i, normal );

		for ( int index = 0; count > 2; index += 3 )
		{
			int best = -1;
			float score = 0;
			for ( int i = 0; i < count; i++ )
			{
				if ( scores[i] <= score ) continue;
				best = i;
				score = scores[i];
			}
			if ( best < 0 ) return [];
			indices[index] = remaining[(best + count - 1) % count];
			indices[index + 1] = remaining[best];
			indices[index + 2] = remaining[(best + 1) % count];
			if ( --count == 2 ) break;

			remaining[(best + 1)..].CopyTo( remaining[best..] );
			scores[(best + 1)..].CopyTo( scores[best..] );
			remaining = remaining[..count];
			scores = scores[..count];
			int previous = (best + count - 1) % count;
			int next = best % count;
			scores[previous] = ScoreEar( vertices, remaining, previous, normal );
			scores[next] = ScoreEar( vertices, remaining, next, normal );
		}
		return indices;
	}

	internal static Vector3 PolygonNormal( ReadOnlySpan<Vector3> vertices )
	{
		Vector3 normal = default;
		for ( int i = 0; i < vertices.Length; i++ )
		{
			var a = vertices[i];
			var b = vertices[(i + 1) % vertices.Length];
			normal.x += (a.y - b.y) * (a.z + b.z);
			normal.y += (a.z - b.z) * (a.x + b.x);
			normal.z += (a.x - b.x) * (a.y + b.y);
		}
		return normal / (normal.Length + 1.1920929e-7f);
	}

	static float ScoreEar( ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> ring, int corner, Vector3 normal )
	{
		const float epsilon = 1e-7f;
		int count = ring.Length;
		var a = vertices[ring[(corner + count - 1) % count]];
		var b = vertices[ring[corner]];
		var c = vertices[ring[(corner + 1) % count]];
		var ab = b - a;
		var ac = c - a;
		var bc = c - b;
		float area = ab.Cross( ac ).Dot( normal );
		if ( area < epsilon ) return 0;
		foreach ( var index in ring )
		{
			var point = vertices[index];
			if ( point == a || point == b || point == c ) continue;
			bool inside = bc.Cross( point - b ).Dot( normal ) > -epsilon &&
				(-ac).Cross( point - c ).Dot( normal ) > -epsilon && ab.Cross( point - a ).Dot( normal ) > -epsilon;
			if ( inside || OnEdge( point - a, ab ) || OnEdge( point - b, bc ) || OnEdge( point - c, -ac ) ) return 0;
		}

		if ( count > 3 )
		{
			var d = vertices[ring[(corner + 2) % count]];
			area = Math.Min( area, (b - d).Cross( c - d ).Dot( normal ) );
		}
		if ( count > 4 )
		{
			var d = vertices[ring[(corner + count - 2) % count]];
			area = Math.Min( area, (a - d).Cross( b - d ).Dot( normal ) );
		}
		ab = ab.Normal;
		ac = ac.Normal;
		bc = bc.Normal;
		float cosine = Math.Max( ab.Dot( ac ), Math.Max( ab.Dot( bc ), ac.Dot( bc ) ) );
		return 2 * (1 - cosine) + 1 / Math.Max( area, 1 );

		static bool OnEdge( Vector3 offset, Vector3 side )
		{
			float t = offset.Dot( side ) / side.LengthSquared;
			return t is >= 0 and <= 1 && (offset - side * t).LengthSquared < epsilon * epsilon;
		}
	}
}
