using System;
using System.Globalization;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	internal readonly record struct GroundContour( int A, int B, int C, int D )
	{
		public int Minimum => Math.Min( Math.Min( A, B ), Math.Min( C, D ) );
		public int this[int corner] => (corner & 3) switch { 0 => A, 1 => B, 2 => C, _ => D };
		public GroundContour RelativeTo( int z ) => new( A - z, B - z, C - z, D - z );
	}

	readonly record struct GroundCell( GroundContour Heights, bool Wet, int Path, int CustomPath, int CustomType );
	readonly record struct TileChoice( SourceNode Target, int Rotation, float Weight );

	// Hammer numbers edges left-to-right, interleaving vertical and horizontal edges on each row.
	// The last row contains only horizontal edges. Corner bits precede edge bits in the path mask.
	static int GridEdgeIndex( int width, int height, int cx, int cy, int side )
	{
		if ( cx < 0 || cy < 0 || cx > width || cy > height || (cx == width && side != 3) || (cy == height && side != 0) ) return -1;
		if ( side == 1 ) cx++;
		if ( side == 2 ) cy++;
		int index = cy * (2 * width + 1) + (cy == height ? cx : 2 * cx + (1 - (side & 1)));
		return index < (2 * width + 1) * height + width ? index : -1;
	}

	internal static int GroundPathMask( ReadOnlySpan<byte> edges, int width, int height, int x, int y, GroundContour heights, bool perEdge )
	{
		if ( edges.IsEmpty ) return 0;
		static bool Marked( ReadOnlySpan<byte> values, int i ) => (uint)i < values.Length && values[i] != 0;
		int marked = 0, slopes = 0;
		for ( int i = 0; i < 4; i++ )
		{
			if ( Marked( edges, GridEdgeIndex( width, height, x, y, i ) ) ) marked |= 1 << i;
			if ( heights[i] != heights[i + 1] ) slopes |= 1 << i;
		}
		if ( marked == 0 ) return 0;
		if ( slopes != 0 )
		{
			if ( !perEdge ) return ((marked & slopes) == slopes ? marked | (15 ^ slopes) : marked) << 4;
			for ( int i = 0; i < 4; i++ )
			{
				int bit = 1 << i, opposite = (i + 2) & 3;
				if ( (marked & bit) == 0 || (slopes & bit) != 0 ) continue;
				int neighbors = (1 << ((i + 1) & 3)) | (1 << ((i + 3) & 3));
				if ( (neighbors & marked & slopes) != 0 ) continue;
				if ( (marked & ~slopes & (1 << opposite)) != 0 && Math.Abs( heights[i] - heights[opposite] ) == 1 ) continue;
				marked &= ~bit;
			}
			return marked << 4;
		}

		int mask = marked << 4;
		for ( int i = 0; i < 4; i++ )
		{
			int vx = x + (i is 1 or 2 ? 1 : 0), vy = y + (i >= 2 ? 1 : 0);
			int a = GridEdgeIndex( width, height, vx, vy, 0 ), b = GridEdgeIndex( width, height, vx, vy, 3 ), c = GridEdgeIndex( width, height, vx - 1, vy - 1, 2 ), d = GridEdgeIndex( width, height, vx - 1, vy - 1, 1 );
			if ( (a < 0 || Marked( edges, a )) && (b < 0 || Marked( edges, b )) &&
				(c < 0 || Marked( edges, c )) && (d < 0 || Marked( edges, d )) ) mask |= 1 << i;
		}
		return mask;
	}

	sealed partial class VmapReader
	{
		// The DMX attribute is uint8. Hammer ignores an int with the same name.
		byte TileVariation( SourceNode tile ) => tileData[tile].GetValueOrDefault( "variationId" ) is byte id ? id : byte.MaxValue;

		static SourceNode GroundSet( Dictionary<int, SourceNode> nodes ) => nodes.Values.FirstOrDefault( n =>
			n.TypeString == "CMapTileSet" && string.Equals( n.Name, "Ground", StringComparison.OrdinalIgnoreCase ) );

		static TileChoice PickTileChoice( TileChoice[] choices )
		{
			if ( choices.Length == 1 ) return choices[0];
			float pick = Random.Shared.NextSingle() * choices[^1].Weight;
			int i = 0;
			while ( i < choices.Length - 1 && pick > choices[i].Weight ) i++;
			return choices[i];
		}

		TileChoice[] ReadGroundChoices( Dictionary<int, SourceNode> nodes, GroundCell cell, int step, byte variation )
		{
			var ground = GroundSet( nodes );
			if ( ground is null ) return [];
			var properties = tileData[ground].Get<Element[]>( "properties", [] );
			Element Property( string name ) => Array.Find( properties, p => Is( p.Name, name ) );
			var water = Property( "water" );
			bool checkWater = water is not null && (water.Get( "targetTypeFlags", 1 ) & 1) != 0 && (water.Get( "componentTypeFlags", 1 ) & 1) != 0;
			var custom = cell.CustomPath == 0 ? null : Property( "customPathType" );
			var customType = custom?.Get<string[]>( "values", [] ).ElementAtOrDefault( cell.CustomType );
			var candidates = new List<TileChoice>();
			foreach ( var tile in ground.Children )
			{
				if ( tile.TypeString != "CMapTile" ) continue;
				if ( checkWater && !WholeTileMatches( tile, water, cell.Wet ? "yes" : "no" ) ) continue;
				if ( customType is not null && !WholeTileMatches( tile, custom, customType ) ) continue;
				candidates.Add( new( tile, -1, tileData[tile].Get( "probability", 1f ) ) );
			}

			string Height( int i ) => ((cell.Heights[i] is >= 0 and < 15 ? cell.Heights[i] : 0) * step).ToString( CultureInfo.InvariantCulture );
			var heights = new[] { Height( 0 ), Height( 1 ), Height( 2 ), Height( 3 ) };
			var choices = Filter( candidates, "height", heights );
			choices = FilterPath( choices, "path", cell.Path );
			var customPath = Property( "destruction" ) ?? Property( "customPath" );
			if ( customPath is not null ) choices = FilterPath( choices, customPath.Name, cell.CustomPath );
			if ( variation != byte.MaxValue && choices.Any( c => TileVariation( c.Target ) == variation ) )
				choices.RemoveAll( c => TileVariation( c.Target ) != variation );
			float total = 0;
			for ( int i = 0; i < choices.Count; i++ )
			{
				var choice = choices[i];
				total += choice.Weight;
				choices[i] = choice with { Weight = total };
			}
			return choices.ToArray();

			List<TileChoice> FilterPath( List<TileChoice> input, string name, int mask )
			{
				if ( input.Count == 0 ) return input;
				if ( mask == 0 ) return Filter( input, name, NoPath );
				var values = new string[8];
				for ( int i = 0; i < values.Length; i++ ) values[i] = (mask & (1 << i)) != 0 ? "yes" : "no";
				var matches = Filter( input, name, values );
				return matches.Count == 0 ? Filter( input, name, NoPath ) : matches;
			}

			List<TileChoice> Filter( List<TileChoice> input, string name, string[] requested )
			{
				var output = new List<TileChoice>();
				var fallback = Property( name )?.Get<string[]>( "values", [] ).FirstOrDefault();
				bool uniform = requested.All( v => v == requested[0] );
				foreach ( var choice in input )
				{
					var tile = tileData[choice.Target];
					int index = TilePropertyIndex( tile, name );
					if ( index < 0 && fallback is null ) continue;
					int first = Math.Max( choice.Rotation, 0 );
					int count = choice.Rotation < 0 && !uniform ? 4 : 1;
					for ( int r = first; r < first + count; r++ )
					{
						bool match = true;
						for ( int i = 0; i < requested.Length && match; i++ )
						{
							int local = (i - r) & 3;
							string value = index < 0 ? fallback : tile.Get<string[]>( i < 4 ? CornerKeys[local] : EdgeKeys[local], [] ).ElementAtOrDefault( index );
							match = TileValueMatches( value, requested[i] );
						}
						if ( match ) output.Add( uniform ? choice : choice with { Rotation = r } );
					}
				}
				return output;
			}

			bool WholeTileMatches( SourceNode node, Element property, string requested )
			{
				var tile = tileData[node];
				int index = TilePropertyIndex( tile, property.Name );
				var value = index < 0 ? property.Get<string[]>( "values", [] ).FirstOrDefault() : tile.Get<string[]>( "propertyValues", [] ).ElementAtOrDefault( index );
				return value?.StartsWith( '*' ) == true || Is( value, requested );
			}

			static bool Is( string a, string b ) => string.Equals( a, b, StringComparison.OrdinalIgnoreCase );
		}

		static int TilePropertyIndex( Element element, string name ) =>
			Array.FindIndex( element.Get<string[]>( "propertyNames", [] ), n => string.Equals( n, name, StringComparison.OrdinalIgnoreCase ) );

		static bool TileValueMatches( string value, string requested ) =>
			value is null || requested is null || value.StartsWith( '*' ) || requested.StartsWith( '*' ) ||
			(value.StartsWith( '~' ) ? !value.AsSpan( 1 ).Equals( requested, StringComparison.OrdinalIgnoreCase ) : string.Equals( value, requested, StringComparison.OrdinalIgnoreCase ));

		static readonly string[] CornerKeys = ["cornerPropertyValues0", "cornerPropertyValues1", "cornerPropertyValues2", "cornerPropertyValues3"];
		static readonly string[] EdgeKeys = ["edgePropertyValues0", "edgePropertyValues1", "edgePropertyValues2", "edgePropertyValues3"];
		static readonly string[] NoPath = ["no", "no", "no", "no", "no", "no", "no", "no"];
	}
}
