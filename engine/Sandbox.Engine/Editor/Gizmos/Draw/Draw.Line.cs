using System.Runtime.InteropServices;
using System.Buffers;

namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		/// <summary>
		/// Adds a line to the current object, but also adds it as a potential hitbox
		/// </summary>
		internal void AddLineInternal( VertexSceneObject o, in Vector3 a, in Vector3 b )
		{
			o.Vertices.Add( new Vertex( a, Color32 ) );
			o.Vertices.Add( new Vertex( b, Color32 ) );

			Hitbox.AddPotentialLine( a, b, LineThickness );
		}

		/// <summary>
		/// Draw a line from a to b
		/// </summary>
		public void Line( in Vector3 a, in Vector3 b )
		{
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			AddLineInternal( so, a, b );
		}

		/// <summary>
		/// Draw a line from a to b
		/// </summary>
		public void Line( in Line line )
		{
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			AddLineInternal( so, line.Start, line.End );
		}

		/// <summary>
		/// Draw a lines
		/// </summary>
		public void Lines( in IEnumerable<Line> lines )
		{
			if ( lines is Line[] array ) { Lines( array.AsSpan() ); return; }
			if ( lines is List<Line> list ) { Lines( CollectionsMarshal.AsSpan( list ) ); return; }
			using var iterator = lines.GetEnumerator();
			if ( !iterator.MoveNext() ) return;
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );
			if ( lines.TryGetNonEnumeratedCount( out var count ) ) so.Vertices.EnsureCapacity( checked(so.Vertices.Count + count * 2) );
			do { var line = iterator.Current; AddLineInternal( so, line.Start, line.End ); } while ( iterator.MoveNext() );
		}

		/// <summary>Draw line segments without allocating an enumerator.</summary>
		public void Lines( ReadOnlySpan<Line> lines )
		{
			if ( lines.IsEmpty ) return;
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );
			var start = so.Vertices.Count;
			CollectionsMarshal.SetCount( so.Vertices, checked(start + lines.Length * 2) );
			var target = CollectionsMarshal.AsSpan( so.Vertices )[start..];
			var vertex = new Vertex( Vector3.Zero, Color32 );
			for ( var i = 0; i < lines.Length; ++i )
			{
				vertex.Position = lines[i].Start; target[i * 2] = vertex;
				vertex.Position = lines[i].End; target[i * 2 + 1] = vertex;
				if ( Active.lineScope.Enabled ) Hitbox.AddPotentialLine( lines[i].Start, lines[i].End, LineThickness );
			}
		}

		/// <summary>
		/// Draw a bounding box
		/// </summary>
		public void LineBBox( in BBox box )
		{
			LineBox( stackalloc Vector3[8]
			{
				new Vector3( box.Mins.x, box.Mins.y, box.Mins.z ),
				new Vector3( box.Maxs.x, box.Mins.y, box.Mins.z ),
				new Vector3( box.Maxs.x, box.Maxs.y, box.Mins.z ),
				new Vector3( box.Mins.x, box.Maxs.y, box.Mins.z ),

				new Vector3( box.Mins.x, box.Mins.y, box.Maxs.z ),
				new Vector3( box.Maxs.x, box.Mins.y, box.Maxs.z ),
				new Vector3( box.Maxs.x, box.Maxs.y, box.Maxs.z ),
				new Vector3( box.Mins.x, box.Maxs.y, box.Maxs.z )
			} );
		}

		/// <summary>
		/// Draws a frustum.
		/// </summary>
		public void LineFrustum( in Frustum frustum )
		{
			LineBox( stackalloc Vector3[8]
			{
				frustum.GetCorner( 0 ) ?? Vector3.Zero,
				frustum.GetCorner( 1 ) ?? Vector3.Zero,
				frustum.GetCorner( 2 ) ?? Vector3.Zero,
				frustum.GetCorner( 3 ) ?? Vector3.Zero,

				frustum.GetCorner( 4 ) ?? Vector3.Zero,
				frustum.GetCorner( 5 ) ?? Vector3.Zero,
				frustum.GetCorner( 6 ) ?? Vector3.Zero,
				frustum.GetCorner( 7 ) ?? Vector3.Zero
			} );
		}

		private void LineBox( Span<Vector3> corners )
		{
			Assert.AreEqual( 8, corners.Length );
			ReadOnlySpan<byte> edges = [0, 1, 1, 2, 2, 3, 3, 0, 0, 4, 1, 5, 2, 6, 3, 7, 4, 5, 5, 6, 6, 7, 7, 4];
			Span<Vertex> vertices = stackalloc Vertex[8];
			var transform = Transform;
			var matrix = Matrix.CreateScale( transform.Scale ) * Matrix.CreateRotation( transform.Rotation ) * Matrix.CreateTranslation( transform.Position );
			for ( var i = 0; i < vertices.Length; ++i ) vertices[i] = new Vertex( matrix.Transform( corners[i] ), Color32 );

			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial, worldSpace: true );
			var start = so.Vertices.Count;
			CollectionsMarshal.SetCount( so.Vertices, checked(start + edges.Length) );
			var target = CollectionsMarshal.AsSpan( so.Vertices )[start..];
			for ( var i = 0; i < edges.Length; ++i ) target[i] = vertices[edges[i]];
			if ( Active.lineScope.Enabled )
				for ( var i = 0; i < edges.Length; i += 2 ) Hitbox.AddPotentialLine( corners[edges[i]], corners[edges[i + 1]], LineThickness );
		}

		/// <summary>
		/// Draw a sphere made out of lines
		/// </summary>
		public void LineSphere( in Vector3 point, in float radius, in int rings = 8 ) => LineSphere( new Sphere( point, radius ), rings );

		/// <summary>
		/// Draw a sphere made out of lines
		/// </summary>
		public void LineSphere( in Sphere sphere, int rings = 8 )
		{
			if ( rings < 1 ) return;
			using var aroundBuffer = rings > MaxCachedCircleSections ? new PooledSpan<Vector2>( checked(rings + 1) ) : default;
			using var downBuffer = rings > MaxCachedCircleSections ? new PooledSpan<Vector2>( checked(rings + 1) ) : default;
			var around = CircleSamples( rings, aroundBuffer.Span, lines: true );
			var down = CircleSamples( rings, downBuffer.Span, half: true );
			var stride = checked(rings + 1);
			using var grid = new PooledSpan<Vector3>( checked(stride * stride) );
			var points = grid.Span;
			for ( var y = 0; y <= rings; ++y )
				for ( var x = 0; x <= rings; ++x )
					points[y * stride + x] = sphere.Center + new Vector3( sphere.Radius * down[y].x * around[x].y,
						sphere.Radius * down[y].x * around[x].x, sphere.Radius * down[y].y );

			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );
			var start = so.Vertices.Count;
			CollectionsMarshal.SetCount( so.Vertices, checked(start + rings * rings * 4) );
			var target = CollectionsMarshal.AsSpan( so.Vertices )[start..];
			var vertex = new Vertex( Vector3.Zero, Color32 );
			var output = 0;
			for ( var y = 0; y < rings; ++y )
				for ( var x = 0; x < rings; ++x )
				{
					var index = y * stride + x;
					vertex.Position = points[index]; target[output++] = vertex;
					vertex.Position = points[index + stride]; target[output++] = vertex;
					vertex.Position = points[index]; target[output++] = vertex;
					vertex.Position = points[index + 1]; target[output++] = vertex;
					if ( Active.lineScope.Enabled )
					{
						Hitbox.AddPotentialLine( points[index], points[index + stride], LineThickness );
						Hitbox.AddPotentialLine( points[index], points[index + 1], LineThickness );
					}
				}
		}

		/// <summary>
		/// Draw a sphere made out of lines
		/// </summary>
		public void LineCircle( in Vector3 center, float radius, float startAngle = 0.0f, float totalDegrees = 360.0f, int sections = 16 )
		{
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			var left = Vector3.Left * radius;
			var up = Vector3.Up * radius;

			totalDegrees = totalDegrees.DegreeToRadian();
			startAngle = startAngle.DegreeToRadian();

			Vector3 lastPos = center + MathF.Sin( startAngle ) * left + MathF.Cos( startAngle ) * up;

			for ( int i = 0; i < sections; i++ )
			{
				var f = startAngle + (((float)i + 1) / sections) * totalDegrees;
				Vector3 vPos = center;

				vPos += left * MathF.Sin( f );
				vPos += up * MathF.Cos( f );

				AddLineInternal( so, lastPos, vPos );

				lastPos = vPos;
			}
		}

		public void LineCircle( in Vector3 center, Vector3 forward, float radius, float startAngle = 0.0f, float totalDegrees = 360.0f, int sections = 16 )
		{
			Vector3 up = Vector3.Up;
			if ( MathF.Abs( Vector3.Dot( forward, up ) ) > 0.99 )
			{
				up = Vector3.Forward;
			}

			LineCircle( center, forward, up, radius, startAngle, totalDegrees, sections );
		}

		public void LineCircle( in Vector3 center, Vector3 forward, Vector3 up, float radius, float startAngle = 0.0f, float totalDegrees = 360.0f, int sections = 16 )
		{
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			Vector3 right = Vector3.Cross( forward, up ).Normal;

			right *= radius;
			up *= radius;

			totalDegrees = totalDegrees.DegreeToRadian();
			startAngle = startAngle.DegreeToRadian();

			Vector3 lastPos = center + MathF.Sin( startAngle ) * right + MathF.Cos( startAngle ) * up;

			for ( int i = 0; i < sections; i++ )
			{
				var f = startAngle + (((float)i + 1) / sections) * totalDegrees;
				Vector3 vPos = center;

				vPos += right * MathF.Sin( f );
				vPos += up * MathF.Cos( f );

				AddLineInternal( so, lastPos, vPos );

				lastPos = vPos;
			}
		}

		/// <summary>
		/// A cylinder
		/// </summary>
		public void LineCylinder( Vector3 vPointA, Vector3 vPointB, float flRadiusA, float flRadiusB, int nNumSegments )
		{
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			flRadiusA = MathF.Abs( flRadiusA );
			flRadiusB = MathF.Abs( flRadiusB );

			// Compute the verts for the top and bottom.
			Vector3 vecDir = (vPointB - vPointA).Normal;
			var rotation = Rotation.LookAt( vecDir );

			Vector3 vecLeft = rotation.Left;
			Vector3 vecUp = rotation.Up;

			float flAngle = 0;
			float flAngleStep = (2 * MathF.PI) / (float)nNumSegments;

			Vector3[] pVerts = ArrayPool<Vector3>.Shared.Rent( 2 * nNumSegments );

			for ( int i = 0; i < nNumSegments; flAngle += flAngleStep, i++ )
			{
				Vector3 vecOffset = vecLeft * MathF.Sin( flAngle ) + vecUp * MathF.Cos( flAngle );
				pVerts[i] = vPointA + flRadiusA * vecOffset;
				pVerts[i + nNumSegments] = vPointB + flRadiusB * vecOffset;
			}

			for ( int i = 0; i < nNumSegments; i++ )
			{
				var a = pVerts[i == 0 ? nNumSegments - 1 : i - 1];
				var b = pVerts[i];

				AddLineInternal( so, a, b );

				a = pVerts[nNumSegments + (i == 0 ? nNumSegments - 1 : i - 1)];
				b = pVerts[nNumSegments + i];
				AddLineInternal( so, a, b );

				// We use minimum to avoid div by 0 in modulo if nNumSegments smaller than 4
				var verticalSegmentInterval = nNumSegments / Math.Min( 4, nNumSegments );
				if ( i % verticalSegmentInterval == 0 )
					AddLineInternal( so, pVerts[i], pVerts[i + nNumSegments] );
			}

			ArrayPool<Vector3>.Shared.Return( pVerts );
		}

		public void LineCapsule( Capsule capsule, int rings = 12 )
		{
			var diff = capsule.CenterB - capsule.CenterA;

			if ( diff.IsNearZeroLength )
			{
				LineSphere( capsule.CenterA, capsule.Radius );
				return;
			}

			var rot = Rotation.LookAt( diff );

			// Circles at the centers and 4 lines down the middle
			LineCylinder( capsule.CenterA, capsule.CenterB, capsule.Radius, capsule.Radius, rings );

			// 2 arcs at each end
			LineCircle( capsule.CenterA, rot.Left, rot.Forward, capsule.Radius, 90, 180 );
			LineCircle( capsule.CenterB, rot.Left, rot.Forward, capsule.Radius, 270, 180 );
			LineCircle( capsule.CenterA, rot.Up, rot.Forward, capsule.Radius, 90, 180 );
			LineCircle( capsule.CenterB, rot.Up, rot.Forward, capsule.Radius, 270, 180 );
		}

		/// <summary>
		/// A triangle
		/// </summary>
		public void LineTriangle( in Triangle triangle )
		{
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			AddLineInternal( so, triangle.A, triangle.B );
			AddLineInternal( so, triangle.B, triangle.C );
			AddLineInternal( so, triangle.C, triangle.A );
		}

		/// <summary>
		/// Multiple triangles
		/// </summary>
		public void LineTriangles( in IEnumerable<Triangle> triangles )
		{
			if ( triangles is Triangle[] array ) { LineTriangles( array.AsSpan() ); return; }
			if ( triangles is List<Triangle> list ) { LineTriangles( CollectionsMarshal.AsSpan( list ) ); return; }
			using var iterator = triangles.GetEnumerator();
			if ( !iterator.MoveNext() ) return;
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );
			if ( triangles.TryGetNonEnumeratedCount( out var count ) ) so.Vertices.EnsureCapacity( checked(so.Vertices.Count + count * 6) );
			do
			{
				var triangle = iterator.Current;
				AddLineInternal( so, triangle.A, triangle.B );
				AddLineInternal( so, triangle.B, triangle.C );
				AddLineInternal( so, triangle.C, triangle.A );
			} while ( iterator.MoveNext() );
		}

		/// <summary>Draw triangle outlines without allocating an enumerator.</summary>
		public void LineTriangles( ReadOnlySpan<Triangle> triangles )
		{
			if ( triangles.IsEmpty ) return;
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );
			so.Vertices.EnsureCapacity( checked(so.Vertices.Count + triangles.Length * 6) );
			foreach ( ref readonly var triangle in triangles )
			{
				AddLineInternal( so, triangle.A, triangle.B );
				AddLineInternal( so, triangle.B, triangle.C );
				AddLineInternal( so, triangle.C, triangle.A );
			}
		}

	}
}
