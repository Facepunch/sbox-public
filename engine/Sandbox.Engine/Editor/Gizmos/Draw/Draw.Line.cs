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
			if ( !lines.Any() ) return;

			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			var hasCheapCount = lines.TryGetNonEnumeratedCount( out var lineCount );
			if ( hasCheapCount )
			{
				so.Vertices.EnsureCapacity( so.Vertices.Count + lineCount * 2 );
			}

			foreach ( var line in lines )
			{
				AddLineInternal( so, line.Start, line.End );
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

			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			// bottom
			AddLineInternal( so, corners[0], corners[1] );
			AddLineInternal( so, corners[1], corners[2] );
			AddLineInternal( so, corners[2], corners[3] );
			AddLineInternal( so, corners[3], corners[0] );

			// middle
			AddLineInternal( so, corners[0], corners[4] );
			AddLineInternal( so, corners[1], corners[5] );
			AddLineInternal( so, corners[2], corners[6] );
			AddLineInternal( so, corners[3], corners[7] );

			// top
			AddLineInternal( so, corners[4], corners[5] );
			AddLineInternal( so, corners[5], corners[6] );
			AddLineInternal( so, corners[6], corners[7] );
			AddLineInternal( so, corners[7], corners[4] );
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
			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			++rings;

			var vertices = ArrayPool<Vector3>.Shared.Rent( rings * rings );

			int nOutVert = 0;

			int i, j;
			for ( i = 0; i < rings; ++i )
			{
				for ( j = 0; j < rings; ++j )
				{
					float u = j / (float)(rings - 1);
					float v = i / (float)(rings - 1);
					float t = 2.0f * MathF.PI * u;
					float p = MathF.PI * v;

					vertices[nOutVert] = new( sphere.Center.x + (sphere.Radius * MathF.Sin( p ) * MathF.Cos( t )), sphere.Center.y + (sphere.Radius * MathF.Sin( p ) * MathF.Sin( t )), sphere.Center.z + (sphere.Radius * MathF.Cos( p )) );
					++nOutVert;
				}
			}


			for ( i = 0; i < rings - 1; ++i )
			{
				for ( j = 0; j < rings - 1; ++j )
				{
					int idx = rings * i + j;
					AddLineInternal( so, vertices[idx], vertices[idx + rings] );
					AddLineInternal( so, vertices[idx], vertices[idx + 1] );
				}
			}

			ArrayPool<Vector3>.Shared.Return( vertices );
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
			if ( !triangles.Any() ) return;

			var so = VertexObject( Graphics.PrimitiveType.Lines, LineMaterial );

			var hasCheapCount = triangles.TryGetNonEnumeratedCount( out var trianglesCount );
			if ( hasCheapCount )
			{
				so.Vertices.EnsureCapacity( so.Vertices.Count + trianglesCount * 6 );
			}

			foreach ( var triangle in triangles )
			{
				AddLineInternal( so, triangle.A, triangle.B );
				AddLineInternal( so, triangle.B, triangle.C );
				AddLineInternal( so, triangle.C, triangle.A );
			}
		}
	}
}
