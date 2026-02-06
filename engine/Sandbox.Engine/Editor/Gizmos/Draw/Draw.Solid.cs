namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		/// <summary>
		/// Draw a solid cone shape
		/// </summary>
		public void SolidCone( Vector3 @base, Vector3 extent, float flRadius, int? segments = null )
		{
			int nSegments = 10;

			if ( segments is int s && s >= 3 )
				nSegments = s;

			var vecTip = @base + extent;

			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			Vector3 vecDir = vecTip - @base;

			var rot = Rotation.LookAt( vecDir );

			Vector3 vecLeft = rot.Left;
			Vector3 vecUp = rot.Up;

			float flAngle = 0;
			float flAngleStep = (2.0f * MathF.PI) / (float)nSegments;
			Vector3[] pVerts = new Vector3[nSegments];
			for ( int i = 0; i < nSegments; flAngle += flAngleStep, i++ )
			{
				pVerts[i] = @base + flRadius * (vecLeft * MathF.Sin( flAngle ) + vecUp * MathF.Cos( flAngle ));
			}

			// Draw the cone.	
			for ( int i = 0; i < nSegments - 1; i++ )
			{
				var n = (pVerts[i] + vecTip).Normal;
				so.Vertices.Add( new Vertex( pVerts[i], Color ) { Normal = n } );
				so.Vertices.Add( new Vertex( vecTip, Color ) { Normal = n } );

				n = (pVerts[i + 1] + vecTip).Normal;
				so.Vertices.Add( new Vertex( pVerts[i + 1], Color ) { Normal = n } );
			}

			var nr = (pVerts[nSegments - 1] + vecTip).Normal;
			so.Vertices.Add( new Vertex( pVerts[nSegments - 1], Color ) { Normal = nr } );
			so.Vertices.Add( new Vertex( vecTip, Color ) { Normal = nr } );

			nr = (pVerts[0] + vecTip).Normal;
			so.Vertices.Add( new Vertex( pVerts[0], Color ) { Normal = nr } );


			// Draw the base.
			if ( true )
			{
				for ( int i = 1; i < nSegments - 1; i++ )
				{
					so.Vertices.Add( new Vertex( pVerts[0], Color ) { Normal = -vecUp } );
					so.Vertices.Add( new Vertex( pVerts[i], Color ) { Normal = -vecUp } );
					so.Vertices.Add( new Vertex( pVerts[i + 1], Color ) { Normal = -vecUp } );
				}
			}

		}

		/// <summary>
		/// Draw a solid box shape
		/// </summary>
		public void SolidBox( BBox box )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			var mx = box.Maxs;
			var mn = box.Mins;

			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mn.z ), Color ) );

			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mn.z ), Color ) );

			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mn.z ), Color ) );

			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mn.z ), Color ) );

			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mn.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mn.z ), Color ) );

			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mn.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mx.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mx.y, mx.z ), Color ) );
			so.Vertices.Add( new Vertex( new Vector3( mn.x, mn.y, mx.z ), Color ) );
		}

		/// <summary>
		/// Draw a solid triangle shape
		/// </summary>
		public void SolidTriangle( in Triangle triangle )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			so.Vertices.Add( new Vertex( triangle.A, Color ) );
			so.Vertices.Add( new Vertex( triangle.B, Color ) );
			so.Vertices.Add( new Vertex( triangle.C, Color ) );
		}

		/// <summary>
		/// Draw a solid triangle shape
		/// </summary>
		public void SolidTriangle( in Vector3 a, in Vector3 b, in Vector3 c )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			so.Vertices.Add( new Vertex( a, Color ) );
			so.Vertices.Add( new Vertex( b, Color ) );
			so.Vertices.Add( new Vertex( c, Color ) );
		}

		/// <summary>
		/// Multiple solid triangles
		/// </summary>
		public void SolidTriangles( in IEnumerable<Triangle> triangles )
		{
			if ( !triangles.Any() ) return;

			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			var hasCheapCount = triangles.TryGetNonEnumeratedCount( out var trianglesCount );
			if ( hasCheapCount )
			{
				so.Vertices.EnsureCapacity( so.Vertices.Count + trianglesCount * 3 );
			}

			foreach ( var triangle in triangles )
			{
				so.Vertices.Add( new Vertex( triangle.A, Color ) );
				so.Vertices.Add( new Vertex( triangle.B, Color ) );
				so.Vertices.Add( new Vertex( triangle.C, Color ) );
			}
		}

		/// <summary>
		/// Draw a filled circle
		/// </summary>
		public void SolidCircle( Vector3 center, float radius, float startAngle = 0, float totalDegrees = 360, int sections = 8 )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			var right = Vector3.Right;
			var up = Vector3.Up;

			totalDegrees = totalDegrees.DegreeToRadian();
			startAngle = startAngle.DegreeToRadian();

			Vector3 lastPos = up;

			for ( int i = 0; i <= sections; i++ )
			{
				var f = startAngle + (((float)i) / sections) * totalDegrees;

				Vector3 vPos = 0;

				vPos += right * MathF.Sin( f );
				vPos += up * MathF.Cos( f );

				var a = lastPos * radius;
				var b = vPos * radius;
				var c = Vector3.Zero;
				var d = Vector3.Zero;

				if ( i > 0 )
				{
					so.Vertices.Add( new Vertex( center + a, Color ) );
					so.Vertices.Add( new Vertex( center + b, Color ) );
					so.Vertices.Add( new Vertex( center + c, Color ) );

					so.Vertices.Add( new Vertex( center + c, Color ) );
					so.Vertices.Add( new Vertex( center + d, Color ) );
					so.Vertices.Add( new Vertex( center + a, Color ) );
				}

				lastPos = vPos;
			}
		}

		/// <summary>
		/// Draw a filled ring
		/// </summary>
		public void SolidRing( Vector3 center, float innerRadius, float outerRadius, float startAngle = 0, float totalDegrees = 360.0f, int sections = 8 )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );

			var right = Vector3.Right;
			var up = Vector3.Up;

			totalDegrees = totalDegrees.DegreeToRadian();
			startAngle = startAngle.DegreeToRadian();

			Vector3 lastPos = up;

			for ( int i = 0; i <= sections; i++ )
			{
				var f = startAngle + (((float)i) / sections) * totalDegrees;

				Vector3 vPos = 0;

				vPos += right * MathF.Sin( f );
				vPos += up * MathF.Cos( f );

				var a = lastPos * outerRadius;
				var b = vPos * outerRadius;
				var c = vPos * innerRadius;
				var d = lastPos * innerRadius;

				if ( i > 0 )
				{
					so.Vertices.Add( new Vertex( center + a, Color ) );
					so.Vertices.Add( new Vertex( center + b, Color ) );
					so.Vertices.Add( new Vertex( center + c, Color ) );

					so.Vertices.Add( new Vertex( center + c, Color ) );
					so.Vertices.Add( new Vertex( center + d, Color ) );
					so.Vertices.Add( new Vertex( center + a, Color ) );
				}

				lastPos = vPos;
			}
		}

		/// <summary>
		/// Draw a solid sphere shape
		/// </summary>
		public void SolidSphere( Vector3 center, float radius, int hSegments = 8, int vSegments = 8 )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );
			var vertices = new List<Vertex>();

			for ( var y = 0; y <= vSegments; y++ )
			{
				var v = (float)y / vSegments;
				var vAngle = v * MathF.PI;

				for ( int x = 0; x <= hSegments; x++ )
				{
					var u = (float)x / hSegments;
					var hAngle = u * MathF.PI * 2;

					var vertex = new Vector3(
						center.x + radius * MathF.Sin( vAngle ) * MathF.Cos( hAngle ),
						center.y + radius * MathF.Cos( vAngle ),
						center.z + radius * MathF.Sin( vAngle ) * MathF.Sin( hAngle )
					);

					vertices.Add( new Vertex( vertex, Color ) );
				}
			}

			for ( var y = 0; y < vSegments; y++ )
			{
				for ( var x = 0; x < hSegments; x++ )
				{
					int i0 = y * (hSegments + 1) + x;
					int i1 = i0 + 1;
					int i2 = i0 + (hSegments + 1);
					int i3 = i2 + 1;

					so.Vertices.Add( vertices[i0] );
					so.Vertices.Add( vertices[i2] );
					so.Vertices.Add( vertices[i1] );

					so.Vertices.Add( vertices[i1] );
					so.Vertices.Add( vertices[i2] );
					so.Vertices.Add( vertices[i3] );
				}
			}
		}

		/// <summary>
		/// Draw a solid cylinder shape
		/// </summary>
		public void SolidCylinder( Vector3 start, Vector3 end, float radius, int hSegments = 32 )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );
			var vertices = new List<Vertex>();

			var axis = (end - start).Normal;
			var up = Vector3.Up;

			if ( MathF.Abs( Vector3.Dot( axis, up ) ) > 0.99f )
			{
				up = Vector3.Forward;
			}

			var right = Vector3.Cross( axis, up ).Normal;
			up = Vector3.Cross( right, axis ).Normal;

			for ( var i = 0; i <= hSegments; i++ )
			{
				var angle = (float)i / hSegments * MathF.PI * 2;
				var direction = right * MathF.Cos( angle ) + up * MathF.Sin( angle );

				vertices.Add( new Vertex( start + direction * radius, Color ) );
				vertices.Add( new Vertex( end + direction * radius, Color ) );
			}

			var topCenter = new Vertex( end, Color );
			var bottomCenter = new Vertex( start, Color );

			var topCenterIndex = vertices.Count;
			vertices.Add( topCenter );

			var bottomCenterIndex = vertices.Count;
			vertices.Add( bottomCenter );

			for ( var i = 0; i < hSegments; i++ )
			{
				var curIndex = i * 2;
				var nextIndex = (i + 1) * 2;

				so.Vertices.Add( vertices[curIndex] );
				so.Vertices.Add( vertices[nextIndex] );
				so.Vertices.Add( vertices[curIndex + 1] );

				so.Vertices.Add( vertices[curIndex + 1] );
				so.Vertices.Add( vertices[nextIndex] );
				so.Vertices.Add( vertices[nextIndex + 1] );
			}

			for ( var i = 0; i < hSegments; i++ )
			{
				var curIndex = i * 2;
				var nextIndex = (i + 1) * 2;

				so.Vertices.Add( vertices[topCenterIndex] );
				so.Vertices.Add( vertices[nextIndex + 1] );
				so.Vertices.Add( vertices[curIndex + 1] );

				so.Vertices.Add( vertices[bottomCenterIndex] );
				so.Vertices.Add( vertices[curIndex] );
				so.Vertices.Add( vertices[nextIndex] );
			}
		}

		/// <summary>
		/// Draw a solid capsule shape
		/// </summary>
		public void SolidCapsule( Vector3 start, Vector3 end, float radius, int hSegments, int vSegments )
		{
			var so = VertexObject( Graphics.PrimitiveType.Triangles, SolidMaterial );
			var vertices = new List<Vertex>();

			var axis = (end - start).Normal;
			var right = Vector3.Cross( axis, Vector3.Right ).Normal;
			var forward = Vector3.Cross( right, axis ).Normal;

			for ( var y = 0; y <= vSegments / 2; y++ )
			{
				var v = (float)y / (vSegments / 2);
				var vAngle = v * MathF.PI * 0.5f;

				for ( var x = 0; x <= hSegments; x++ )
				{
					var u = (float)x / hSegments;
					var hAngle = u * MathF.PI * 2;

					var vertex = start
								- axis * radius * MathF.Cos( vAngle )
								+ (right * MathF.Cos( hAngle ) + forward * MathF.Sin( hAngle )) * radius * MathF.Sin( vAngle );

					vertices.Add( new Vertex( vertex, Color ) );
				}
			}

			for ( var y = 0; y <= 1; y++ )
			{
				var heightOffset = Vector3.Lerp( start, end, y );

				for ( var x = 0; x <= hSegments; x++ )
				{
					var u = (float)x / hSegments;
					var hAngle = u * MathF.PI * 2;

					var vertex = heightOffset + (right * MathF.Cos( hAngle ) + forward * MathF.Sin( hAngle )) * radius;
					vertices.Add( new Vertex( vertex, Color ) );
				}
			}

			for ( var y = 0; y <= vSegments / 2; y++ )
			{
				var v = (float)y / (vSegments / 2);
				var vAngle = v * MathF.PI * 0.5f;

				for ( var x = 0; x <= hSegments; x++ )
				{
					var u = (float)x / hSegments;
					var hAngle = u * MathF.PI * 2;

					var vertex = end
								+ axis * radius * MathF.Cos( vAngle )
								+ (right * MathF.Cos( hAngle ) + forward * MathF.Sin( hAngle )) * radius * MathF.Sin( vAngle );

					vertices.Add( new Vertex( vertex, Color ) );
				}
			}

			var segmentCount = (vSegments / 2 + 1) * (hSegments + 1);
			var totalSegments = segmentCount + (hSegments + 1) * 2;

			for ( var y = 0; y < vSegments / 2; y++ )
			{
				for ( var x = 0; x < hSegments; x++ )
				{
					var i0 = y * (hSegments + 1) + x;
					var i1 = i0 + 1;
					var i2 = i0 + (hSegments + 1);
					var i3 = i2 + 1;

					so.Vertices.Add( vertices[i0] );
					so.Vertices.Add( vertices[i2] );
					so.Vertices.Add( vertices[i1] );

					so.Vertices.Add( vertices[i1] );
					so.Vertices.Add( vertices[i2] );
					so.Vertices.Add( vertices[i3] );
				}
			}

			for ( var x = 0; x < hSegments; x++ )
			{
				var i0 = segmentCount + x;
				var i1 = i0 + 1;
				var i2 = i0 + (hSegments + 1);
				var i3 = i2 + 1;

				so.Vertices.Add( vertices[i0] );
				so.Vertices.Add( vertices[i2] );
				so.Vertices.Add( vertices[i1] );

				so.Vertices.Add( vertices[i1] );
				so.Vertices.Add( vertices[i2] );
				so.Vertices.Add( vertices[i3] );
			}

			for ( var y = 0; y < vSegments / 2; y++ )
			{
				for ( var x = 0; x < hSegments; x++ )
				{
					var i0 = totalSegments + y * (hSegments + 1) + x;
					var i1 = i0 + 1;
					var i2 = i0 + (hSegments + 1);
					var i3 = i2 + 1;

					if ( i3 < vertices.Count )
					{
						so.Vertices.Add( vertices[i0] );
						so.Vertices.Add( vertices[i2] );
						so.Vertices.Add( vertices[i1] );

						so.Vertices.Add( vertices[i1] );
						so.Vertices.Add( vertices[i2] );
						so.Vertices.Add( vertices[i3] );
					}
				}
			}
		}

		/// <summary>
		/// Draws a half circle that tries its best to point towards the camera. This is used by
		/// the rotation widgets that bias towards the camera.
		/// </summary>
		public void ScreenBiasedHalfCircle( Vector3 center, float radius )
		{
			var vecViewPoint = Transform.PointToLocal( Camera.Position );
			var vecForward = (center - vecViewPoint).Normal;

			Vector3 vecAxis = Vector3.Forward;
			float flDot = MathF.Abs( vecForward.Dot( vecAxis ) );

			//
			// Facing the camera, just draw the whole circle
			//
			if ( flDot > 0.95f )
			{
				Draw.LineCircle( 0, radius, sections: 64 );
				return;
			}

			// Draw a half-circle to reduce visual clutter.
			// Find the vector within the rotation plane toward the camera position. This will be our "right" direction for our circle.	
			float flPlaneDist = Vector3.Dot( vecViewPoint, vecAxis ) - Vector3.Dot( center, vecAxis );

			Vector3 vecRight = vecViewPoint - flPlaneDist * vecAxis;
			vecRight -= center;
			vecRight = vecRight.Normal;

			// Generate the "up" vector within the rotation plane for the circle.
			Vector3 vecUp = Vector3.Cross( vecAxis, vecRight ).Normal;

			using ( Gizmo.Scope() )
			{
				Transform = Transform.WithRotation( Transform.Rotation * Rotation.LookAt( vecAxis, vecUp ) );
				Draw.LineCircle( 0, radius, 0, 180.0f, sections: 32 );
			}
		}

		/// <summary>
		/// Draw a sprite.
		/// </summary>
		public void Sprite( Vector3 center, float size, string texture )
		{
			using var tex = Texture.Load( texture );
			Sprite( center, size, tex );
		}

		/// <summary>
		/// Draw a sprite.
		/// </summary>
		public void Sprite( Vector3 center, float size, Texture texture )
		{
			Sprite( center, size, texture, true );
		}

		/// <summary>
		/// Draw a sprite.
		/// </summary>
		public void Sprite( Vector3 center, Vector2 size, Texture texture, bool worldspace )
		{
			Sprite( center, size, texture, worldspace, 0.0f );
		}

		/// <summary>
		/// Draw a sprite.
		/// </summary>
		public void Sprite( Vector3 center, Vector2 size, Texture texture, bool worldspace, float angle )
		{
			var so = VertexObject( Graphics.PrimitiveType.Points, SpriteMaterial, false );

			var a = new Vertex
			{
				Position = Transform.PointToWorld( center ),
				Color = Color,
				TexCoord0 = new Vector4( size.x, size.y, worldspace ? 1 : 0, angle.DegreeToRadian() ),
				Normal = Vector3.Up,
				Tangent = new Vector4( 1, 0, 0, 1 )
			};

			so.Bounds = so.Bounds.AddBBox( BBox.FromPositionAndSize( center, size.Length * 2 ) );
			so.Vertices.Add( a );
			so.Flags.IsTranslucent = true;
			so.Flags.IsOpaque = false;

			so.Attributes.Set( "TextureColor", texture ?? Texture.White );
		}
	}
}
