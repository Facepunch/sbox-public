namespace Sandbox;

/// <summary>
/// Render surfaces and convex compound collision, including genuinely hollow pieces.
/// Shapes are generated at their recipe size; custom blocks are then fitted to exactly fill their box.
/// </summary>
public sealed partial class BuilderGeometry
{
	// Bump when recipes change so hotload cannot reuse old meshes/collision.
	public const int GeometryVersion = 15;

	const float TextureUnit = BuilderCatalog.Unit;

	const int MaxStairSteps = 32;

	static readonly BuilderGeometry Scratch = new();
	static Material _defaultMaterial;

	readonly List<Vector3> triangles = new( 4096 );
	readonly List<Vector3> hullPoints = new( 2048 );
	readonly List<int> hullEnds = new( 256 );
	readonly List<Vertex> vertices = new( 4096 );
	readonly List<int> indices = new( 4096 );
	Vector3[] hullBuffer = new Vector3[256];

	readonly List<int> groups = new( 1400 );
	int group;
	int groupCount;
	readonly List<Vector3> fitted = new( 4096 );
	readonly List<Vector3> faceNormals = new( 1400 );
	readonly Dictionary<CornerKey, int> buckets = new( 4096 );
	readonly List<int> cornerBuckets = new( 4096 );
	int[] bucketStarts = new int[4096];
	int[] bucketCorners = new int[4096];
	int[] bucketFill = new int[4096];
	Vector3[] bucketPoles = new Vector3[4096];
	readonly List<(int Group, Vector3 Position, Vector3 Normal)> poles = new();

	readonly List<BuilderFace> faces = new( 64 );
	readonly List<BuilderFace> fittedFaces = new( 64 );

	const float WeldPrecision = 1024f;

	readonly record struct CornerKey( int Group, int X, int Y, int Z );

	bool hasSphere;
	float sphereRadius, sphereHeight;
	Vector3 sphereCenter;

	/// <summary>
	/// Builds a model for <paramref name="key"/>. Leave <paramref name="collision"/> off for previews,
	/// convex hull cooking is the expensive part.
	/// </summary>
	/// <param name="key">What to build.</param>
	/// <param name="collision">Leave off for previews, convex hull cooking is the expensive part.</param>
	/// <param name="faces">When given, filled with the shape's flat rectangular faces, in model space.</param>
	public static Model Create( BuilderShapeKey key, bool collision = true, List<BuilderFace> faces = null )
	{
		var geometry = Scratch;
		geometry.Clear();
		if ( key.Fit ) geometry.BuildCustom( key );
		else geometry.Build( key.Shape, key.Size, key.Parameter( 0 ) );

		var model = geometry.Finish( key, collision );
		faces?.AddRange( geometry.fittedFaces );
		return model;
	}

	/// <summary>The catalog model for a spawn menu piece, shared and cached for the session.</summary>
	public static Model GetModel( BuilderCatalog.Piece piece ) => BuilderModelCache.Get( BuilderShapeKey.For( piece ) );

	void Clear()
	{
		triangles.Clear();
		hullPoints.Clear();
		hullEnds.Clear();
		vertices.Clear();
		indices.Clear();
		groups.Clear();
		group = 0;
		groupCount = 0;
		poles.Clear();
		faces.Clear();
		fittedFaces.Clear();
		hasSphere = false;
		charts.Clear();
		chartNodes.Clear();
		cornerNodes.Clear();
	}

	int NewGroup() => ++groupCount;

	Model Finish( BuilderShapeKey key, bool collision )
	{
		var raw = Bounds( triangles );
		var scale = Vector3.One;
		var offset = Vector3.Zero;

		if ( key.Fit && (BuilderShapes.Get( key.Shape )?.FitsBox ?? true) )
		{
			var target = key.Size;
			var size = raw.Size;
			scale = new Vector3(
				size.x > 0.001f ? target.x / size.x : 1,
				size.y > 0.001f ? target.y / size.y : 1,
				size.z > 0.001f ? target.z / size.z : 1 );
			offset = -raw.Center * scale;
		}

		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		FitAndSmooth( scale, offset );
		UnwrapCharts( scale, offset );
		foreach ( var face in faces )
			fittedFaces.Add( new BuilderFace( face.A * scale + offset, face.B * scale + offset, face.C * scale + offset, face.D * scale + offset ) );

		for ( int t = 0; t < faceNormals.Count; t++ )
		{
			var normal = faceNormals[t];
			if ( normal.IsNearZeroLength ) continue;

			// Stable planar axes keep the two triangles of a face on the same UV projection.
			var planarTangent = MathF.Abs( normal.z ) > 0.99f ? Vector3.Forward : Vector3.Cross( Vector3.Up, normal ).Normal;
			var planarBitangent = Vector3.Cross( planarTangent, normal );

			for ( int k = 0; k < 3; k++ )
			{
				var corner = t * 3 + k;
				var p = fitted[corner];
				var cornerNormal = CornerNormal( corner, normal );

				if ( TryChartVertex( corner, cornerNormal, out var uv, out var tangent, out var sign ) )
				{
					AddVertex( p, cornerNormal, tangent, uv, sign );
				}
				else
				{
					var planarUv = new Vector2( Vector3.Dot( p, planarTangent ), Vector3.Dot( p, planarBitangent ) ) / TextureUnit;
					var shadingTangent = planarTangent - cornerNormal * Vector3.Dot( planarTangent, cornerNormal );
					AddVertex( p, cornerNormal, shadingTangent.IsNearZeroLength ? planarTangent : shadingTangent.Normal, planarUv, -1 );
				}

				mins = mins.ComponentMin( p );
				maxs = maxs.ComponentMax( p );
			}
		}

		if ( vertices.Count == 0 ) return null;

		_defaultMaterial ??= Material.Load( BuilderCatalog.DefaultMaterial );

		var bounds = new BBox( mins, maxs );
		var mesh = new Mesh( _defaultMaterial );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = bounds;

		var builder = Model.Builder.AddMesh( mesh );

		if ( collision )
		{
			var start = 0;
			foreach ( var end in hullEnds )
			{
				var count = end - start;
				if ( hullBuffer.Length < count ) hullBuffer = new Vector3[count];

				for ( int i = 0; i < count; i++ )
					hullBuffer[i] = hullPoints[start + i] * scale + offset;

				builder.AddCollisionHull( hullBuffer.AsSpan( 0, count ) );
				start = end;
			}

			if ( hasSphere ) AddSphereCollision( builder, scale, offset );
		}

		return builder.WithHullBounds( bounds ).WithViewBounds( bounds )
			.WithName( $"builder/{key.Shape}_{key.X}x{key.Y}x{key.Z}_{key.P0}_{key.P1}_{key.P2}_{key.P3}_{key.P4}_{key.P5}_{key.P6}_{key.P7}{(key.Fit ? "f" : "")}_v{GeometryVersion}" )
			.WithSurface( "plastic" ).Create();
	}

	void FitAndSmooth( Vector3 scale, Vector3 offset )
	{
		fitted.Clear();
		faceNormals.Clear();
		buckets.Clear();
		cornerBuckets.Clear();

		for ( int i = 0; i < triangles.Count; i++ )
			fitted.Add( triangles[i] * scale + offset );

		for ( int t = 0; t * 3 + 2 < fitted.Count; t++ )
		{
			var a = fitted[t * 3];
			var cross = Vector3.Cross( fitted[t * 3 + 1] - a, fitted[t * 3 + 2] - a );
			var degenerate = cross.LengthSquared < 0.000001f;
			faceNormals.Add( degenerate ? Vector3.Zero : cross.Normal );
		}

		for ( int corner = 0; corner < fitted.Count; corner++ )
		{
			var face = corner / 3;
			var faceGroup = groups[face];
			if ( faceGroup == 0 || faceNormals[face].IsNearZeroLength )
			{
				cornerBuckets.Add( -1 );
				continue;
			}

			var p = fitted[corner];
			var key = new CornerKey( faceGroup, (int)MathF.Round( p.x * WeldPrecision ), (int)MathF.Round( p.y * WeldPrecision ), (int)MathF.Round( p.z * WeldPrecision ) );
			if ( !buckets.TryGetValue( key, out var bucket ) )
			{
				bucket = buckets.Count;
				buckets[key] = bucket;
			}

			cornerBuckets.Add( bucket );
		}

		var bucketCount = buckets.Count;
		if ( bucketStarts.Length < bucketCount + 1 ) bucketStarts = new int[bucketCount + 1];
		if ( bucketCorners.Length < fitted.Count ) bucketCorners = new int[fitted.Count];

		Array.Clear( bucketStarts, 0, bucketCount + 1 );
		foreach ( var bucket in cornerBuckets )
		{
			if ( bucket >= 0 ) bucketStarts[bucket + 1]++;
		}

		for ( int i = 0; i < bucketCount; i++ )
			bucketStarts[i + 1] += bucketStarts[i];

		if ( bucketFill.Length < bucketCount ) bucketFill = new int[bucketCount];
		Array.Clear( bucketFill, 0, bucketCount );
		var fill = bucketFill;
		for ( int corner = 0; corner < cornerBuckets.Count; corner++ )
		{
			var bucket = cornerBuckets[corner];
			if ( bucket < 0 ) continue;
			bucketCorners[bucketStarts[bucket] + fill[bucket]++] = corner;
		}

		if ( bucketPoles.Length < bucketCount ) bucketPoles = new Vector3[bucketCount];
		Array.Clear( bucketPoles, 0, bucketCount );
		foreach ( var (poleGroup, position, normal) in poles )
		{
			var p = position * scale + offset;
			var key = new CornerKey( poleGroup, (int)MathF.Round( p.x * WeldPrecision ), (int)MathF.Round( p.y * WeldPrecision ), (int)MathF.Round( p.z * WeldPrecision ) );
			if ( buckets.TryGetValue( key, out var bucket ) ) bucketPoles[bucket] = (normal / scale).Normal;
		}
	}

	Vector3 CornerNormal( int corner, Vector3 faceNormal )
	{
		var bucket = cornerBuckets[corner];
		if ( bucket < 0 ) return faceNormal;
		if ( !bucketPoles[bucket].IsNearZeroLength ) return bucketPoles[bucket];

		var sum = Vector3.Zero;
		for ( int i = bucketStarts[bucket]; i < bucketStarts[bucket + 1]; i++ )
		{
			var other = bucketCorners[i];
			sum += faceNormals[other / 3] * CornerAngle( other );
		}

		return sum.IsNearZeroLength ? faceNormal : sum.Normal;
	}

	float CornerAngle( int corner )
	{
		var face = corner / 3 * 3;
		var p = fitted[corner];
		var a = fitted[face + (corner - face + 1) % 3] - p;
		var b = fitted[face + (corner - face + 2) % 3] - p;

		return MathF.Atan2( Vector3.Cross( a, b ).Length, Vector3.Dot( a, b ) );
	}

	/// <summary>A vertex with its tangent frame; <paramref name="sign"/> is the handedness, bitangent = sign × (normal × tangent).</summary>
	void AddVertex( Vector3 p, Vector3 normal, Vector3 tangent, Vector2 uv, float sign )
	{
		indices.Add( vertices.Count );
		vertices.Add( new Vertex( p, normal, tangent, new Vector4( uv.x, uv.y, 0, 0 ) ) { Tangent = new Vector4( tangent, sign ) } );
	}

	// Dense render spheres can fail convex-hull cooking, so a round sphere uses an exact native sphere
	void AddSphereCollision( ModelBuilder builder, Vector3 scale, Vector3 offset )
	{
		var center = sphereCenter * scale + offset;
		var axes = new Vector3( sphereRadius * scale.x, sphereRadius * scale.y, sphereHeight * 0.5f * scale.z );

		var largest = MathF.Max( axes.x, MathF.Max( axes.y, axes.z ) );
		var smallest = MathF.Min( axes.x, MathF.Min( axes.y, axes.z ) );
		if ( largest - smallest <= largest * 0.005f )
		{
			builder.AddCollisionSphere( (axes.x + axes.y + axes.z) / 3, center );
			return;
		}

		// Hull cooking silently produces nothing past roughly 50 points
		const int columns = 16, rows = 4;
		var count = 0;
		Span<Vector3> points = stackalloc Vector3[columns * (rows - 1) + 2];
		points[count++] = center - new Vector3( 0, 0, axes.z );
		points[count++] = center + new Vector3( 0, 0, axes.z );

		for ( int v = 1; v < rows; v++ )
		{
			var t = v * MathF.PI / rows;
			var ring = MathF.Sin( t );
			var z = -MathF.Cos( t ) * axes.z;

			for ( int u = 0; u < columns; u++ )
			{
				var a = u * MathF.Tau / columns;
				points[count++] = center + new Vector3( MathF.Cos( a ) * ring * axes.x, MathF.Sin( a ) * ring * axes.y, z );
			}
		}

		builder.AddCollisionHull( points );
	}

	static BBox Bounds( List<Vector3> points )
	{
		if ( points.Count == 0 ) return default;

		var mins = points[0];
		var maxs = points[0];
		for ( int i = 1; i < points.Count; i++ )
		{
			mins = mins.ComponentMin( points[i] );
			maxs = maxs.ComponentMax( points[i] );
		}

		return new BBox( mins, maxs );
	}

	const int FrameStripJoins = (1 << 1) | (1 << 3);

	const int BeamEndsX = (1 << 1) | (1 << 3);
	const int BeamEndsY = (1 << 0) | (1 << 2);

	static void Rectangle( Span<Vector2> polygon, float x0, float y0, float x1, float y1 )
	{
		polygon[0] = new( x0, y0 ); polygon[1] = new( x1, y0 ); polygon[2] = new( x1, y1 ); polygon[3] = new( x0, y1 );
	}

	void BoxFrame( float x, float y, float z, float width )
	{
		Span<Vector2> beam = stackalloc Vector2[4];

		for ( int sx = -1; sx <= 1; sx += 2 )
		for ( int sy = -1; sy <= 1; sy += 2 )
		{
			float px = sx < 0 ? -x : x - width, py = sy < 0 ? -y : y - width;
			Rectangle( beam, px, py, px + width, py + width );

			// The two sides facing into the box are covered by beams at both ends; only their middle shows
			int inwardX = sx < 0 ? 1 : 3, inwardY = sy < 0 ? 2 : 0;
			Prism( beam, -z, z, hiddenSides: (1 << inwardX) | (1 << inwardY) );
			Side( beam[inwardX], beam[(inwardX + 1) % 4], -z + width, z - width );
			Side( beam[inwardY], beam[(inwardY + 1) % 4], -z + width, z - width );
		}

		for ( int sz = -1; sz <= 1; sz += 2 )
		{
			float bottom = sz < 0 ? -z : z - width, top = bottom + width;
			for ( int side = -1; side <= 1; side += 2 )
			{
				float py = side < 0 ? -y : y - width;
				Rectangle( beam, -x + width, py, x - width, py + width );
				Prism( beam, bottom, top, hiddenSides: BeamEndsX );

				float px = side < 0 ? -x : x - width;
				Rectangle( beam, px, -y + width, px + width, y - width );
				Prism( beam, bottom, top, hiddenSides: BeamEndsY );
			}
		}
	}

	void Triangle( Vector3 a, Vector3 b, Vector3 c )
	{
		triangles.Add( a );
		triangles.Add( b );
		triangles.Add( c );
		groups.Add( group );
		cornerNodes.Add( NoNode );
		cornerNodes.Add( NoNode );
		cornerNodes.Add( NoNode );
	}

	void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d ) { Triangle( a, b, c ); Triangle( a, c, d ); }

	void EndHull() => hullEnds.Add( hullPoints.Count );

	// Convex counterclockwise XY polygon extruded along Z. Smooth sides wrap as one unwrapped strip; hidden sides only apply to flat ones.
	void Prism( ReadOnlySpan<Vector2> polygon, float bottom, float top, bool smoothSides = false, int hiddenSides = 0 )
	{
		for ( int i = 1; i < polygon.Length - 1; i++ )
		{
			Triangle( P( polygon[0], top ), P( polygon[i], top ), P( polygon[i + 1], top ) );
			Triangle( P( polygon[0], bottom ), P( polygon[i + 1], bottom ), P( polygon[i], bottom ) );
		}

		if ( smoothSides )
		{
			// A round side wraps once around the shape as one strip
			var sides = AddChart( UvLayout.Strip, polygon.Length, 1, true );
			for ( int i = 0; i <= polygon.Length; i++ )
			{
				SetNode( sides, i, 0, P( polygon[i % polygon.Length], bottom ) );
				SetNode( sides, i, 1, P( polygon[i % polygon.Length], top ) );
			}

			group = NewGroup();
			for ( int i = 0; i < polygon.Length; i++ ) ChartQuad( sides, i, 0 );
			group = 0;
		}
		else
		{
			for ( int i = 0; i < polygon.Length; i++ )
			{
				var j = (i + 1) % polygon.Length;
				if ( (hiddenSides & (1 << i)) != 0 ) continue;

				Side( polygon[i], polygon[j], bottom, top );
			}
		}

		foreach ( var p in polygon ) hullPoints.Add( P( p, bottom ) );
		foreach ( var p in polygon ) hullPoints.Add( P( p, top ) );
		EndHull();

		static Vector3 P( Vector2 p, float z ) => new( p.x, p.y, z );
	}

	void Side( Vector2 a, Vector2 b, float bottom, float top )
	{
		Vector3 a0 = new( a.x, a.y, bottom ), b0 = new( b.x, b.y, bottom ), b1 = new( b.x, b.y, top ), a1 = new( a.x, a.y, top );
		Quad( a0, b0, b1, a1 );
		faces.Add( new BuilderFace( a0, b0, b1, a1 ) );
	}

	void Hollow( Func<float, float, bool, Vector3> point, int columns, int rows, bool closed, int outerSides = 0, Vector3 poleNormal = default,
		UvLayout outerUv = UvLayout.Strip, UvLayout innerUv = UvLayout.Strip )
	{
		Span<Vector3> p = stackalloc Vector3[8];

		var outerSheet = AddChart( outerUv, columns, rows, closed, poleNormal );
		var innerSheet = AddChart( innerUv, columns, rows, closed, poleNormal );
		for ( int v = 0; v <= rows; v++ )
		for ( int u = 0; u <= columns; u++ )
		{
			SetNode( outerSheet, u, v, point( (float)u / columns, (float)v / rows, false ) );
			SetNode( innerSheet, u, v, point( (float)u / columns, (float)v / rows, true ) );
		}

		var outerFirst = groupCount + 1;
		groupCount += Math.Max( 1, outerSides );
		var inner = NewGroup();

		if ( !poleNormal.IsNearZeroLength && outerSides == 0 )
		{
			poles.Add( (outerFirst, NodePoint( outerSheet, 0, rows ), poleNormal) );
			poles.Add( (inner, NodePoint( innerSheet, 0, rows ), -poleNormal) );
		}

		for ( int u = 0; u < columns; u++ )
		for ( int v = 0; v < rows; v++ )
		{
			p[0] = NodePoint( outerSheet, u, v ); p[1] = NodePoint( outerSheet, u + 1, v ); p[2] = NodePoint( outerSheet, u + 1, v + 1 ); p[3] = NodePoint( outerSheet, u, v + 1 );
			p[4] = NodePoint( innerSheet, u, v ); p[5] = NodePoint( innerSheet, u + 1, v ); p[6] = NodePoint( innerSheet, u + 1, v + 1 ); p[7] = NodePoint( innerSheet, u, v + 1 );

			group = outerSides > 0 ? outerFirst + (u + columns / (2 * outerSides)) * outerSides / columns % outerSides : outerFirst;
			ChartQuad( outerSheet, u, v );
			group = inner;
			ChartQuad( innerSheet, u, v, reverse: true );
			group = 0;
			if ( v == 0 ) Quad( p[4], p[5], p[1], p[0] );
			if ( v == rows - 1 ) Quad( p[3], p[2], p[6], p[7] );
			if ( !closed && u == 0 ) Quad( p[0], p[3], p[7], p[4] );
			if ( !closed && u == columns - 1 ) Quad( p[5], p[6], p[2], p[1] );

			foreach ( var corner in p ) hullPoints.Add( corner );
			EndHull();
		}
	}

	void Lathe( float radius, float height, bool sphere, bool inverted = false, float offset = 0, bool cap = true )
	{
		const int segments = 32;
		int rows = sphere ? 16 : 1;

		Vector3 Point( int u, int v )
		{
			float a = u * MathF.Tau / segments, t = (float)v / rows;
			float r = sphere ? MathF.Sin( t * MathF.PI ) * radius : radius * (inverted ? t : 1 - t);
			float z = sphere ? -MathF.Cos( t * MathF.PI ) * height / 2 : (t - 0.5f) * height;
			return new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r, z + offset );
		}

		group = NewGroup();
		if ( sphere )
		{
			var half = rows / 2;
			var bottom = AddChart( UvLayout.Polar, segments, half, true, Vector3.Down );
			var top = AddChart( UvLayout.Polar, segments, rows - half, true, Vector3.Up );
			for ( int u = 0; u <= segments; u++ )
			{
				for ( int v = 0; v <= half; v++ ) SetNode( bottom, u, v, Point( u, half - v ) );
				for ( int v = 0; v <= rows - half; v++ ) SetNode( top, u, v, Point( u, half + v ) );
			}

			for ( int u = 0; u < segments; u++ )
			{
				for ( int v = 0; v < half; v++ ) ChartQuad( bottom, u, v, reverse: true );
				for ( int v = 0; v < rows - half; v++ ) ChartQuad( top, u, v );
			}
		}
		else
		{
			// The side unfolds flat around the apex, which a fan chart keeps in its last row
			var side = AddChart( UvLayout.Fan, segments, rows, true );
			for ( int u = 0; u <= segments; u++ )
			for ( int v = 0; v <= rows; v++ )
				SetNode( side, u, v, Point( u, inverted ? rows - v : v ) );

			for ( int u = 0; u < segments; u++ )
			for ( int v = 0; v < rows; v++ )
				ChartQuad( side, u, v, reverse: inverted );
		}
		group = 0;

		if ( !sphere && cap )
		{
			int end = inverted ? 1 : 0;
			var center = new Vector3( 0, 0, offset + (end - 0.5f) * height );
			for ( int u = 0; u < segments; u++ )
			{
				if ( inverted ) Triangle( center, Point( u, end ), Point( u + 1, end ) );
				else Triangle( center, Point( u + 1, end ), Point( u, end ) );
			}
		}

		if ( sphere )
		{
			hasSphere = true;
			sphereRadius = radius;
			sphereHeight = height;
			sphereCenter = new Vector3( 0, 0, offset );
			return;
		}

		for ( int u = 0; u < segments; u++ )
		{
			hullPoints.Add( Point( u, 0 ) );
			hullPoints.Add( Point( u, rows ) );
		}
		EndHull();
	}

	void BuildCustom( BuilderShapeKey key )
	{
		var size = key.Size;
		var thickness = key.Parameter( BuilderShapes.Thickness );

		switch ( key.Shape )
		{
			case BuilderCatalog.Shape.Tube:
			{
				var outer = new Vector2( size.x / 2, size.y / 2 );
				var inner = outer - new Vector2( thickness );
				Hollow( ( u, v, inside ) =>
				{
					var r = inside ? inner : outer;
					var a = u * MathF.Tau;
					return new Vector3( MathF.Cos( a ) * r.x, MathF.Sin( a ) * r.y, (v - 0.5f) * size.z );
				}, 32, 1, true );
				return;
			}

			case BuilderCatalog.Shape.PlateHole:
			{
				var outer = new Vector2( size.x / 2, size.y / 2 );
				var inner = outer - new Vector2( thickness );
				Hollow( ( u, v, inside ) =>
				{
					var a = u * MathF.Tau;
					var cos = MathF.Cos( a );
					var sin = MathF.Sin( a );
					if ( inside ) return new Vector3( cos * inner.x, sin * inner.y, (v - 0.5f) * size.z );

					var square = MathF.Max( MathF.Abs( cos ), MathF.Abs( sin ) );
					return new Vector3( cos / square * outer.x, sin / square * outer.y, (v - 0.5f) * size.z );
				}, 32, 1, true, outerSides: 4, outerUv: UvLayout.Planar );
				return;
			}

			case BuilderCatalog.Shape.HalfPipe:
			{
				var outer = new Vector2( size.x / 2, size.y );
				var inner = outer - new Vector2( thickness );
				Hollow( ( u, v, inside ) =>
				{
					var r = inside ? inner : outer;
					var a = u * MathF.PI;
					return new Vector3( MathF.Cos( a ) * r.x, MathF.Sin( a ) * r.y, (v - 0.5f) * size.z );
				}, 16, 1, false );
				return;
			}

			case BuilderCatalog.Shape.Dome:
			case BuilderCatalog.Shape.QuarterDome:
			{
				var half = key.Shape == BuilderCatalog.Shape.QuarterDome;
				var outer = new Vector3( size.x / 2, half ? size.y : size.y / 2, size.z );
				var inner = outer - new Vector3( thickness );
				Hollow( ( u, v, inside ) =>
				{
					var r = inside ? inner : outer;
					float a = u * (half ? MathF.PI : MathF.Tau), b = v * MathF.PI / 2;
					return new Vector3( MathF.Cos( a ) * MathF.Cos( b ) * r.x, MathF.Sin( a ) * MathF.Cos( b ) * r.y, MathF.Sin( b ) * r.z );
				}, half ? 16 : 32, 8, !half, poleNormal: Vector3.Up, outerUv: UvLayout.Polar, innerUv: UvLayout.Polar );
				return;
			}

			case BuilderCatalog.Shape.Bend:
			{
				if ( !BuilderPipe.TryFromKey( key, out var pipe ) ) return;

				var inner = pipe.Radius - pipe.Thickness;
				Hollow( ( u, v, inside ) => pipe.Point( (int)MathF.Round( v * pipe.Rows ), u * MathF.Tau, inside ? inner : pipe.Radius ),
					BuilderPipe.Columns, pipe.Rows, true );
				faces.Add( pipe.StartCap );
				faces.Add( pipe.EndCap );
				return;
			}

			case BuilderCatalog.Shape.Carved:
			{
				float x = size.x / 2, y = size.y / 2, z = size.z / 2;
				Span<Vector2> outer = stackalloc Vector2[3] { new( -x, -y ), new( x, -y ), new( -x, y ) };

				var inradius = size.x * size.y / (size.x + size.y + MathF.Sqrt( size.x * size.x + size.y * size.y ));
				var wall = MathF.Min( 12, inradius * 0.5f );

				Span<Vector2> inner = stackalloc Vector2[3];
				for ( int i = 0; i < 3; i++ )
					inner[i] = InsetCorner( outer[(i + 2) % 3], outer[i], outer[(i + 1) % 3], wall );

				Span<Vector2> strip = stackalloc Vector2[4];
				for ( int i = 0; i < 3; i++ )
				{
					int j = (i + 1) % 3;
					strip[0] = outer[i]; strip[1] = outer[j]; strip[2] = inner[j]; strip[3] = inner[i];
					Prism( strip, -z, z, hiddenSides: FrameStripJoins );
				}
				return;
			}

			case BuilderCatalog.Shape.BoxFrame:
				BoxFrame( size.x / 2, size.y / 2, size.z / 2, thickness );
				return;

			case BuilderCatalog.Shape.Pentagon:
			{
				float x = size.x / 2, y = size.y / 2, z = size.z / 2;
				Span<Vector2> polygon = stackalloc Vector2[5];
				for ( int i = 0; i < 5; i++ )
				{
					var a = MathF.PI / 2 + i * MathF.Tau / 5;
					polygon[i] = new( MathF.Cos( a ) * x, MathF.Sin( a ) * y );
				}
				Prism( polygon, -z, z );
				return;
			}

			default:
				Build( key.Shape, size, 0 );
				return;
		}
	}

	static Vector2 InsetCorner( Vector2 previous, Vector2 corner, Vector2 next, float distance )
	{
		var a = (corner - previous).Normal;
		var b = (next - corner).Normal;
		var normalA = new Vector2( -a.y, a.x );
		var normalB = new Vector2( -b.y, b.x );
		return corner + (normalA + normalB) * (distance / (1 + Vector2.Dot( normalA, normalB )));
	}

	void Build( BuilderCatalog.Shape kind, Vector3 size, float angle )
	{
		float x = size.x / 2, y = size.y / 2, z = size.z / 2;
		float wall = MathF.Min( 6, x * 0.25f );
		Span<Vector2> polygon = stackalloc Vector2[32];

		switch ( kind )
		{
			case BuilderCatalog.Shape.Box:
				polygon[0] = new( -x, -y ); polygon[1] = new( x, -y ); polygon[2] = new( x, y ); polygon[3] = new( -x, y );
				Prism( polygon[..4], -z, z );
				break;
			case BuilderCatalog.Shape.Triangle:
				polygon[0] = new( -x, -y ); polygon[1] = new( x, -y ); polygon[2] = new( -x, y );
				Prism( polygon[..3], -z, z );
				break;
			case BuilderCatalog.Shape.Trapezium:
				polygon[0] = new( -x, -y ); polygon[1] = new( x, -y ); polygon[2] = new( x / 2, y ); polygon[3] = new( -x / 2, y );
				Prism( polygon[..4], -z, z );
				break;
			case BuilderCatalog.Shape.Parallelogram:
				polygon[0] = new( -x, -y ); polygon[1] = new( x / 2, -y ); polygon[2] = new( x, y ); polygon[3] = new( -x / 2, y );
				Prism( polygon[..4], -z, z );
				break;
			case BuilderCatalog.Shape.Pentagon:
				for ( int i = 0; i < 5; i++ )
					polygon[i] = new( MathF.Cos( i * MathF.Tau / 5 ) * x, MathF.Sin( i * MathF.Tau / 5 ) * y );
				Prism( polygon[..5], -z, z );
				break;
			case BuilderCatalog.Shape.Carved:
			{
				// A triangular frame with quarter-unit walls and a real triangular opening.
				float w = MathF.Min( 12, MathF.Min( x, y ) * 0.25f );
				Span<Vector2> outer = stackalloc Vector2[3] { new( -x, -y ), new( x, -y ), new( -x, y ) };
				Span<Vector2> inner = stackalloc Vector2[3] { new( -x + w, -y + w ), new( x - w * (1 + MathF.Sqrt( 2 )), -y + w ), new( -x + w, y - w * (1 + MathF.Sqrt( 2 )) ) };
				for ( int i = 0; i < 3; i++ )
				{
					int j = (i + 1) % 3;
					polygon[0] = outer[i]; polygon[1] = outer[j]; polygon[2] = inner[j]; polygon[3] = inner[i];
					Prism( polygon[..4], -z, z, hiddenSides: FrameStripJoins );
				}
				break;
			}
			case BuilderCatalog.Shape.BoxFrame:
				BoxFrame( x, y, z, MathF.Min( 12, MathF.Min( x, MathF.Min( y, z ) ) * 0.25f ) );
				break;
			case BuilderCatalog.Shape.Stairs:
			{
				int steps = Math.Clamp( (int)MathF.Ceiling( size.x / BuilderCatalog.Unit * 4 ), 2, MaxStairSteps );
				for ( int i = 0; i < steps; i++ )
				{
					float a = -x + size.x * i / steps, b = -x + size.x * (i + 1) / steps;
					polygon[0] = new( a, -y ); polygon[1] = new( b, -y ); polygon[2] = new( b, y ); polygon[3] = new( a, y );
					Prism( polygon[..4], -z, -z + size.z * (i + 1) / steps );
				}
				break;
			}
			case BuilderCatalog.Shape.Cone: Lathe( x, size.z, false ); break;
			case BuilderCatalog.Shape.MirroredCone:
				// Base-to-base: widest at the middle, points at the two ends.
				Lathe( x, z, false, true, -z / 2, cap: false );
				Lathe( x, z, false, false, z / 2, cap: false );
				break;
			case BuilderCatalog.Shape.Ring:
				// The legacy recipe enum/ident is retained for saves, but circle pieces
				// are solid discs with solid collision, not annular tubes.
				for ( int i = 0; i < 32; i++ )
					polygon[i] = new( MathF.Cos( i * MathF.Tau / 32 ) * x, MathF.Sin( i * MathF.Tau / 32 ) * y );
				Prism( polygon, -z, z, smoothSides: true );
				break;
			case BuilderCatalog.Shape.Sphere: Lathe( x, size.z, true ); break;
			case BuilderCatalog.Shape.Dome:
			case BuilderCatalog.Shape.QuarterDome:
				bool half = kind == BuilderCatalog.Shape.QuarterDome;
				Hollow( ( u, v, inside ) =>
				{
					float a = u * (half ? MathF.PI : MathF.Tau), b = v * MathF.PI / 2;
					float r = x - (inside ? wall : 0);
					return new Vector3( MathF.Cos( a ) * MathF.Cos( b ) * r, MathF.Sin( a ) * MathF.Cos( b ) * r, MathF.Sin( b ) * r - z );
				}, half ? 16 : 32, 8, !half, poleNormal: Vector3.Up, outerUv: UvLayout.Polar, innerUv: UvLayout.Polar );
				break;
			case BuilderCatalog.Shape.Bend:
				float bendRadius = x, tubeRadius = x / 3;
				Hollow( ( u, v, inside ) =>
				{
					float a = u * MathF.Tau, b = v * angle * MathF.PI / 180;
					float r = tubeRadius * (inside ? 0.75f : 1);
					return new Vector3( (bendRadius + r * MathF.Cos( a )) * MathF.Cos( b ), (bendRadius + r * MathF.Cos( a )) * MathF.Sin( b ), -r * MathF.Sin( a ) );
				}, 16, Math.Max( 4, (int)angle / 10 ), true );
				break;
			case BuilderCatalog.Shape.Tube:
			case BuilderCatalog.Shape.HalfPipe:
			case BuilderCatalog.Shape.PlateHole:
				bool open = kind == BuilderCatalog.Shape.HalfPipe;
				bool plate = kind == BuilderCatalog.Shape.PlateHole;
				Hollow( ( u, v, inside ) =>
				{
					float a = u * (open ? MathF.PI : MathF.Tau);
					float r = inside ? x - wall : x;
					if ( plate )
						r = inside ? x * 0.65f : x / MathF.Max( MathF.Abs( MathF.Cos( a ) ), MathF.Abs( MathF.Sin( a ) ) );
					return new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r, -z + size.z * v );
				}, open ? 16 : 32, 1, !open, plate ? 4 : 0, outerUv: plate ? UvLayout.Planar : UvLayout.Strip );
				break;
		}
	}
}

/// <summary>
/// A flat rectangular face of a building block, in model space: corners in order, A to B along one edge and
/// B to C along the other, wound so the face points outwards.
/// </summary>
public readonly record struct BuilderFace( Vector3 A, Vector3 B, Vector3 C, Vector3 D );
