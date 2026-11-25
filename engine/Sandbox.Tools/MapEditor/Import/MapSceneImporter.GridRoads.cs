using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	internal sealed record RoadStyle( float Width, float Opacity, float Falloff, Vector3 LayerOpacity, Color32 Sharpness )
	{
		public static readonly RoadStyle Default = new( 150, 0, 1, Vector3.One, new( 127, 127, 127, 255 ) );

		public (RoadStyle Style, bool Done) Apply( Material material )
		{
			float Attribute( string name, float fallback = float.NaN )
			{
				var flags = material.Flags;
				float value = flags.GetFloat( name, float.NaN );
				if ( !float.IsNaN( value ) ) return value;
				int i = flags.GetInt( name, int.MinValue );
				return i == int.MinValue ? fallback : i;
			}

			float opacity = Attribute( "dota.tile_path.opacity" );
			float width = Attribute( "dota.tile_path.width" );
			float falloff = Attribute( "dota.tile_path.falloff" );
			// Layer settings carry forward until a material declares a brush width, opacity or falloff.
			bool done = !float.IsNaN( width ) || !float.IsNaN( opacity ) || !float.IsNaN( falloff );
			float sharp = Attribute( "dota.tile_path.sharpness" );
			byte SharpnessByte( string name, byte previous )
			{
				float value = Attribute( name, sharp );
				return float.IsNaN( value ) ? previous : (byte)((1 - Math.Clamp( value, 0, 1 )) * 255f);
			}
			return (new( float.IsNaN( width ) ? Width : width, float.IsNaN( opacity ) ? Opacity : Math.Clamp( opacity, 0, 1 ),
				float.IsNaN( falloff ) ? Falloff : Math.Clamp( falloff, 0, 1 ),
				new( Attribute( "dota.tile_path.layer3_opacity", LayerOpacity.x ), Attribute( "dota.tile_path.layer2_opacity", LayerOpacity.y ), Attribute( "dota.tile_path.layer1_opacity", LayerOpacity.z ) ),
				new( SharpnessByte( "dota.tile_path.layer3_sharpness", Sharpness.r ), SharpnessByte( "dota.tile_path.layer2_sharpness", Sharpness.g ), SharpnessByte( "dota.tile_path.layer1_sharpness", Sharpness.b ), 255 ) ), done);
		}
	}

	sealed partial class VmapReader
	{
		readonly Dictionary<(SourceNode, RoadStyle, Dictionary<string, string>), (RoadStyle Style, bool Done)> roadStyles = [];
		readonly Dictionary<(Material, RoadStyle), (RoadStyle Style, bool Done)> roadMaterials = [];

		(RoadStyle Style, bool Done) FindRoadStyle( SourceNode node, RoadStyle initial, Dictionary<string, string> replacements )
		{
			var key = (node, initial, replacements);
			if ( roadStyles.TryGetValue( key, out var result ) ) return result;
			roadStyles[key] = result = (initial, false);
			foreach ( var part in node.Meshes )
			{
				// PolygonMesh stores materials in face-use order. Hammer inspects the serialized table in order.
				foreach ( var path in part.TileMaterialPaths ?? part.Mesh.Materials.Select( m => m?.Name ) )
				{
					if ( string.IsNullOrEmpty( path ) ) continue;
					var material = Material.Load( replacements?.GetValueOrDefault( path, path ) ?? path );
					if ( material is null ) continue;
					var materialKey = (material, result.Style);
					if ( !roadMaterials.TryGetValue( materialKey, out result ) )
						roadMaterials[materialKey] = result = materialKey.Style.Apply( material );
					if ( result.Done ) break;
				}
				if ( result.Done ) break;
			}
			if ( !result.Done && node.Target is { } target ) result = FindRoadStyle( target, result.Style, replacements );
			foreach ( var child in node.Children )
			{
				if ( result.Done ) break;
				result = FindRoadStyle( child, result.Style, replacements );
			}
			return roadStyles[key] = result;
		}

		void PaintRoadLayer( SourceNode[] cells, int width, int height, Transform grid, System.Numerics.Vector3 angles, in GroundLayerPaint paint,
			ReadOnlySpan<byte> paths, ReadOnlySpan<byte> water, ReadOnlySpan<int> materials, bool restrictWater, Dictionary<int, Dictionary<string, string>> replacements )
		{
			// Resolve the brushes before applying height paint. Boundary traces see the original neighboring tiles.
			Dictionary<int, (RoadStyle Style, byte[] Samples)> roads = null;
			RoadSurface surface = null;
			// Hammer evaluates brush boundaries in world XY using the grid's authored Euler basis.
			var (sp, cp) = MathF.SinCos( angles.X.DegreeToRadian() );
			var (sy, cy) = MathF.SinCos( angles.Y.DegreeToRadian() );
			var (sr, cr) = MathF.SinCos( angles.Z.DegreeToRadian() );
			var axisX = new Vector2( cp * cy, cp * sy );
			var axisY = new Vector2( sr * sp * cy - cr * sy, sr * sp * sy + cr * cy );
			for ( int i = 0; i < cells.Length; i++ )
			{
				if ( cells[i] is not { } tile ) continue;
				int x = i % width, y = i / width;
				int mask = RoadEdges( paths, water, width, height, x, y, restrictWater );
				if ( mask == 0 || FindRoadStyle( tile, RoadStyle.Default, replacements?.GetValueOrDefault( i ) ).Style is not { Opacity: > 0 } style ) continue;
				surface ??= new RoadSurface( cells, width, height );
				var samples = RasterRoad( paths, water, width, height, x, y, mask, restrictWater, grid.Position, axisX, axisY, paint.CellSize, style, surface );
				if ( samples.AsSpan().ContainsAnyExcept( (byte)0 ) ) (roads ??= [])[i] = (style, samples);
			}
			if ( roads is null && !paint.HasPaint ) return;
			for ( int i = 0; i < cells.Length; i++ )
			{
				if ( cells[i] is not { } tile ) continue;
				(RoadStyle Style, byte[] Samples) road = default;
				roads?.TryGetValue( i, out road );
				if ( road.Samples is not null || paint.HasPaint )
					PaintGroundTile( tile, grid, i % width, i / width, paint, GridValue( materials, i ) > 0, road.Style, road.Samples );
			}
		}
	}

	static bool RoadEdge( ReadOnlySpan<byte> paths, int index ) => (uint)index < paths.Length && paths[index] != 0;

	static int RoadEdges( ReadOnlySpan<byte> paths, ReadOnlySpan<byte> water, int w, int h, int x, int y, bool restrictWater )
	{
		int mask = 0;
		for ( int side = 0; side < 4; side++ )
		{
			if ( !RoadEdge( paths, GridEdgeIndex( w, h, x, y, side ) ) ) continue;
			int a = CornerVertex( w, x, y, side ), b = CornerVertex( w, x, y, (side + 1) & 3 );
			if ( !restrictWater || !(Wet( water, a ) || Wet( water, b )) ) mask |= 1 << side;
		}
		return mask;
	}

	static bool Wet( ReadOnlySpan<byte> water, int index ) => (uint)index < water.Length && water[index] != 0;
	static int CornerVertex( int w, int x, int y, int side ) => (y + (side >= 2 ? 1 : 0)) * (w + 1) + x + (side is 1 or 2 ? 1 : 0);

	static byte[] RasterRoad( ReadOnlySpan<byte> paths, ReadOnlySpan<byte> water, int w, int h, int x, int y, int mask,
		bool restrictWater, Vector2 origin, Vector2 axisX, Vector2 axisY, Vector2 size, RoadStyle style, RoadSurface surface )
	{
		Vector2 Point( float u, float v ) => axisX * ((x + u) * size.x) + axisY * ((y + v) * size.y) + origin;
		Vector2 Mid( int ax, int ay, int bx, int by ) => (Point( ax, ay ) + Point( bx, by )) * 0.5f;
		Span<Vector2> mids = [Mid( 0, 0, 1, 0 ), Mid( 1, 0, 1, 1 ), Mid( 1, 1, 0, 1 ), Mid( 0, 1, 0, 0 )];
		Span<RoadBrush> brushes = stackalloc RoadBrush[20];
		int count = 0;
		float radius = style.Width * 0.5f, cornerRadius = size.x * 0.5f - 1;
		for ( int a = 0; a < 4; a++ )
		{
			if ( (mask & (1 << a)) == 0 ) continue;
			for ( int b = a + 1; b < 4; b++ )
				if ( (mask & (1 << b)) != 0 ) brushes[count++] = RoadBrush.Line( mids[a], mids[b], radius );
			brushes[count++] = RoadBrush.Line( mids[a], mids[a], radius * MathF.Sqrt( 2 ) );
		}
		Span<Vector2> triangle = stackalloc Vector2[4];
		for ( int i = 0; i < 4; i++ )
		{
			int dx = i is 1 or 2 ? 1 : 0, dy = i >= 2 ? 1 : 0;
			if ( restrictWater && Wet( water, CornerVertex( w, x, y, i ) ) ) continue;
			int vx = x + dx, vy = y + dy, incident = 0;
			if ( RoadEdge( paths, GridEdgeIndex( w, h, vx, vy, 0 ) ) ) triangle[incident++] = Mid( dx, dy, dx + 1, dy );
			if ( RoadEdge( paths, GridEdgeIndex( w, h, vx, vy, 3 ) ) ) triangle[incident++] = Mid( dx, dy, dx, dy + 1 );
			if ( RoadEdge( paths, GridEdgeIndex( w, h, vx - 1, vy - 1, 2 ) ) ) triangle[incident++] = Mid( dx, dy, dx - 1, dy );
			if ( RoadEdge( paths, GridEdgeIndex( w, h, vx - 1, vy - 1, 1 ) ) ) triangle[incident++] = Mid( dx, dy, dx, dy - 1 );
			var corner = Point( dx, dy );
			if ( incident == 4 ) brushes[count++] = RoadBrush.Diamond( corner, cornerRadius );
			if ( incident == 3 )
			{
				brushes[count++] = RoadBrush.Triangle( triangle[0], triangle[1], triangle[2] );
				brushes[count++] = RoadBrush.Line( corner, corner, radius * 2 );
			}
		}
		int sides = 0;
		for ( int i = 0; i < 4; i++ )
			if ( (mask & (1 << i)) != 0 ) triangle[sides++] = mids[i];
		if ( sides == 3 ) brushes[count++] = RoadBrush.Triangle( triangle[0], triangle[1], triangle[2] );
		if ( sides == 4 ) brushes[count++] = RoadBrush.Diamond( (mids[0] + mids[1] + mids[2] + mids[3]) * 0.25f, cornerRadius );

		var samples = new byte[81];
		for ( int v = 0; v <= 8; v++ )
			for ( int u = 0; u <= 8; u++ )
			{
				var point = Point( u / 8f, v / 8f );
				float opacity = 0;
				foreach ( var brush in brushes[..count] ) opacity = MathF.Max( opacity, brush.Opacity( point, style ) );
				if ( opacity > 0 && surface.AllowsRoad( x * 8 + u, y * 8 + v, point ) )
					samples[v * 9 + u] = (byte)(opacity * 255f);
			}
		return samples;
	}

	readonly record struct RoadBrush( int Shape, Vector2 A, Vector2 B, Vector2 C, float Radius )
	{
		public static RoadBrush Line( Vector2 a, Vector2 b, float radius ) => new( 0, a, b, default, radius );
		public static RoadBrush Diamond( Vector2 center, float radius ) => new( 1, center, default, default, radius );
		public static RoadBrush Triangle( Vector2 a, Vector2 b, Vector2 c ) => new( 2, a, b, c, 0 );
		public float Opacity( Vector2 point, RoadStyle style )
		{
			var ap = point - A;
			if ( Shape == 1 ) return MathF.Abs( ap.x ) + MathF.Abs( ap.y ) <= Radius ? style.Opacity : 0;
			var ab = B - A;
			if ( Shape == 2 )
			{
				var ac = C - A;
				float aa = ab.Dot( ab ), bb = ac.Dot( ac ), ba = ab.Dot( ac );
				float pa = ab.Dot( ap ), pb = ac.Dot( ap );
				float inv = 1f / (bb * aa - ba * ba);
				float u = (aa * pb - ba * pa) * inv, v = (bb * pa - ba * pb) * inv;
				return u >= 0 && v >= 0 && u + v < 1 ? style.Opacity : 0;
			}
			float len = ab.LengthSquared;
			float t = len < 0.00001f ? 0 : (ab.Dot( point ) - ab.Dot( A )) / len;
			float distance = (point - (A + ab * t)).Length;
			if ( distance > Radius || Radius <= 0 ) return 0;
			float faded = style.Opacity * (1 - Math.Min( 1, distance / Radius ));
			return style.Opacity + (faded - style.Opacity) * style.Falloff;
		}
	}

	static float Cross( Vector2 a, Vector2 b ) => a.x * b.y - a.y * b.x;
}
