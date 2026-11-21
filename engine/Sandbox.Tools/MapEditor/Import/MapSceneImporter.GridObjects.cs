using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// A location rests on a plateau when three equal corners supply at least 3/4 of its weight.
	internal static bool ObjectHeight( GroundContour h, int x, int y, out int z )
	{
		z = h.A;
		if ( x == 0 || y == 0 ) return (x == 0 || h.B == z) && (y == 0 || h.D == z);
		if ( h.A == h.B && h.A == h.D ) return h.A == h.C || x * y <= 4;
		if ( h.A == h.B && h.A == h.C ) return (4 - x) * y <= 4;
		if ( h.A == h.C && h.A == h.D ) return x * (4 - y) <= 4;
		z = h.B;
		return h.B == h.C && h.B == h.D && (4 - x) * (4 - y) <= 4;
	}

	sealed partial class VmapReader
	{
		enum ObjectClass { Prop, Tree, Plant }
		static readonly string[] ObjectSets = ["Props", "Trees", "Plants"];
		static readonly string[] TypeTables = ["propTypes", "treeTypes", "plantTypes"];
		static readonly string[] TreeSizes = ["small", "medium", "large"];

		void ReadGridObjects( Element layer, SourceNode parent, int width, int height, int cellWidth, int cellHeight, int step,
			List<SourceNode> placements, Func<int, Dictionary<int, SourceNode>> library, string[] setPaths, int skyboxes, bool inherited )
		{
			var props = GridValues<int>( layer, "objectsPropType" );
			var trees = GridValues<int>( layer, "objectsTreeType" );
			var plants = GridValues<int>( layer, "objectsPlantType" );
			int authored = Math.Max( props.Length, Math.Max( trees.Length, plants.Length ) );
			if ( authored == 0 ) return;

			var variations = inherited ? GridValues<byte>( layer, "objectsVariationId" ) : default;
			var sizes = GridValues<int>( layer, "objectsTreeSize" );
			var sets = GridValues<int>( layer, "objectsTileSet" );
			var rotation = GridValues<int>( layer, "objectsRotation" );
			var pitch = GridValues<int>( layer, "objectsPitch" );
			var heights = GridValues<int>( layer, "verticesHeight" );
			var water = GridValues<byte>( layer, "verticesWater" );
			var hidden = GridValues<byte>( layer, "cellsHidden" );
			var paths = GridValues<byte>( layer, "edgesPath" );
			var destruction = GridValues<byte>( layer, "edgesDestruction" );
			var records = GridValues<int>( layer, "objectConfiguration" );
			var names = GridValues<int>( layer, "objectConfigurationByName" );
			int stride = checked(4 * width + 1), count = checked(stride * (4 * height + 1));
			bool hasIds = records.Length >= count, hasNames = names.Length >= count;
			var savedSets = layer.Get<Element[]>( "tileSetMapInfo", [] );
			var types = new Dictionary<(int, ObjectClass), (SourceNode Set, string[] Types)>();
			var candidates = new Dictionary<(SourceNode, string, string, byte), SourceNode[]>();
			props = ValidTypes( props, sets, ObjectClass.Prop );
			trees = ValidTypes( trees, sets, ObjectClass.Tree );
			plants = ValidTypes( plants, sets, ObjectClass.Plant );
			int[] visibleTrees = null;
			for ( int i = 0; i < Math.Min( authored, count ); i++ )
			{
				var ids = hasIds ? ReadGridPath( ref records, layer ) : default;
				var named = hasNames ? ReadGridPath( ref names, layer ) : default;
				int prop = GridValue( props, i ), tree = GridValue( trees, i ), plant = GridValue( plants, i );
				if ( (prop | tree | plant) == 0 ) continue;
				int x = i % stride, y = i / stride, cx = x / 4, cy = y / 4;
				if ( cx >= width || cy >= height || GridValue( hidden, cy * width + cx ) != 0 ) continue;
				int slot = GridValue( sets, i );
				if ( (uint)slot >= setPaths.Length || string.IsNullOrEmpty( setPaths[slot] ) ) continue;
				var kind = prop != 0 ? ObjectClass.Prop : tree != 0 ? ObjectClass.Tree : ObjectClass.Plant;
				int type = prop != 0 ? prop : tree != 0 ? tree : plant;
				int vertex = cy * (width + 1) + cx, dx = x & 3, dy = y & 3;
				int next = vertex + width + 1;
				var contour = new GroundContour( GridHeight( heights, water, vertex ), GridHeight( heights, water, vertex + 1 ),
					GridHeight( heights, water, next + 1 ), GridHeight( heights, water, next ) );
				if ( !ObjectHeight( contour, dx, dy, out int z ) || (kind == ObjectClass.Tree && ObjectOnPath( paths, destruction, width, height, cx, cy, dx, dy )) )
				{
					if ( kind == ObjectClass.Tree ) (visibleTrees ??= trees.ToArray())[i] = 0;
					continue;
				}

				var nodes = library( slot );
				var table = TypeTable( slot, kind );
				var typeName = table.Types[type];
				var variant = kind switch
				{
					ObjectClass.Tree => TreeSizes.ElementAtOrDefault( GridValue( sizes, i ) ),
					ObjectClass.Plant => (4 - dx) * (4 - dy) * GridValue( water, vertex ) + dx * (4 - dy) * GridValue( water, vertex + 1 ) +
						(4 - dx) * dy * GridValue( water, next ) + dx * dy * GridValue( water, next + 1 ) >= 8 ? "yes" : "no",
					_ => null
				};
				var saved = ids.Length >= 2 && ids[1] == -1 ? nodes.GetValueOrDefault( ids[0] ) : null;
				if ( saved?.TypeString != "CMapTile" ) saved = null;
				byte variation = i < variations.Length ? variations[i] : byte.MaxValue;
				var key = (table.Set, typeName, variant, variation);
				if ( !candidates.TryGetValue( key, out var choices ) )
				{
					choices = ObjectChoices( table.Set, kind, typeName, variant );
					if ( variation != byte.MaxValue && choices.Length > 1 )
					{
						var matching = Array.FindAll( choices, t => TileVariation( t ) == variation );
						if ( matching.Length > 0 ) choices = matching;
					}
					candidates[key] = choices;
				}
				var tile = choices.Length == 0 || Array.IndexOf( choices, saved ) >= 0 ? saved : choices[Random.Shared.Next( choices.Length )];
				if ( tile is null ) continue;
				bool restored = tile == saved;
				float yaw = GridValue( rotation, i ) * 2.5f;
				int snap = tileData[tile].Get( "rotationSnapping", 0 );
				if ( snap > 0 ) yaw = snap * (int)(yaw / snap);
				var pos = new Vector3( x * 0.25f * cellWidth, y * 0.25f * cellHeight, z * step );
				var placement = parent.Transform.ToWorld( new Transform( pos, new Angles( GridValue( pitch, i ) * 0.625f, yaw, 0 ).ToRotation() ) );
				var proxy = parent.Transform.ToWorld( new Transform( pos, Rotation.FromYaw( yaw ) ) );
				bool? open = choices.Length != 0 && GetProxySites( tile ).Length != 0 ? ObjectOnOpenGround( heights, water, visibleTrees is null ? trees : visibleTrees, props, width, height, x, y ) : null;
				var instance = PlaceTile( restored ? ids : default, nodes, tile, placement, null, 0, proxy, names: restored ? named : default, openGround: open, skyboxSlot: (skyboxes & (1 << slot)) != 0 ? slot : -1 );
				placements.Add( instance );
			}
			// Hammer remaps unknown type indices to zero before resolving object classes and testing neighbors.
			ReadOnlySpan<int> ValidTypes( ReadOnlySpan<int> values, ReadOnlySpan<int> slots, ObjectClass kind )
			{
				int[] adjusted = null;
				for ( int i = 0; i < Math.Min( values.Length, count ); i++ )
				{
					if ( values[i] == 0 ) continue;
					int slot = GridValue( slots, i );
					if ( (uint)slot < setPaths.Length && !string.IsNullOrEmpty( setPaths[slot] ) &&
						(uint)values[i] < TypeTable( slot, kind ).Types.Length ) continue;
					(adjusted ??= values.ToArray())[i] = 0;
				}
				return adjusted is null ? values : adjusted;
			}

			(SourceNode Set, string[] Types) TypeTable( int slot, ObjectClass kind )
			{
				if ( !types.TryGetValue( (slot, kind), out var table ) )
				{
					var set = library( slot ).Values.FirstOrDefault( n => n.TypeString == "CMapTileSet" && string.Equals( n.Name, ObjectSets[(int)kind], StringComparison.OrdinalIgnoreCase ) );
					var values = savedSets.ElementAtOrDefault( slot )?.Get<string[]>( TypeTables[(int)kind], [] );
					if ( values is null && set is not null )
					{
						var property = tileData[set].Get<Element[]>( "properties", [] ).FirstOrDefault( p => string.Equals( p.Name, "type", StringComparison.OrdinalIgnoreCase ) );
						values = (property?.Get<string[]>( "values", [] ) ?? []).Prepend( "none" ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
					}
					table = types[(slot, kind)] = (set, values ?? []);
				}
				return table;
			}
		}

		SourceNode[] ObjectChoices( SourceNode set, ObjectClass kind, string type, string variant )
		{
			if ( set is null ) return [];
			var props = tileData[set].Get<Element[]>( "properties", [] );
			var tiles = set.Children.Where( t => t.TypeString == "CMapTile" );
			bool Matches( SourceNode node, string name, string requested )
			{
				var tile = tileData[node];
				int index = TilePropertyIndex( tile, name );
				string value = index >= 0 ? tile.Get<string[]>( "propertyValues", [] ).ElementAtOrDefault( index ) :
					props.FirstOrDefault( p => string.Equals( p.Name, name, StringComparison.OrdinalIgnoreCase ) )?.Get<string[]>( "values", [] ).FirstOrDefault();
				return value?.StartsWith( '*' ) == true || (value is not null && string.Equals( value, requested, StringComparison.OrdinalIgnoreCase ));
			}
			var variants = kind == ObjectClass.Prop ? tiles : tiles.Where( t => Matches( t, kind == ObjectClass.Tree ? "size" : "water", variant ) );
			var matching = variants.Where( t => Matches( t, "type", type ) ).ToArray();
			if ( matching.Length != 0 || kind != ObjectClass.Tree ) return matching;
			matching = variants.ToArray();
			return matching.Length != 0 ? matching : tiles.ToArray();
		}

		static bool ObjectOnPath( ReadOnlySpan<byte> paths, ReadOnlySpan<byte> destruction, int width, int height, int x, int y, int dx, int dy )
		{
			if ( paths.IsEmpty && destruction.IsEmpty ) return false;
			Span<int> indices = [GridEdgeIndex( width, height, x, y, 0 ), GridEdgeIndex( width, height, x, y, 1 ),
				GridEdgeIndex( width, height, x, y, 2 ), GridEdgeIndex( width, height, x, y, 3 )];
			if ( dx == 0 && dy == 0 )
			{
				indices[1] = GridEdgeIndex( width, height, x - 1, y, 0 );
				indices[2] = GridEdgeIndex( width, height, x, y - 1, 3 );
			}
			if ( (dx == 0) != (dy == 0) )
			{
				int edge = indices[dx == 0 ? 3 : 0];
				return (GridValue( paths, edge ) | GridValue( destruction, edge )) != 0;
			}
			int weight = 0;
			for ( int i = 0; i < 4; i++ )
			{
				int edge = indices[i];
				bool marked = edge >= 0 && (GridValue( paths, edge ) | GridValue( destruction, edge )) != 0;
				if ( dx == 0 && dy == 0 ) { if ( edge >= 0 && !marked ) return false; }
				else if ( marked )
				{
					weight += i switch { 0 => 3 - dy, 1 => dx - 1, 2 => dy - 1, _ => 3 - dx };
				}
			}
			return dx == 0 && dy == 0 || weight >= 2;
		}

		static bool ObjectOnOpenGround( ReadOnlySpan<int> heights, ReadOnlySpan<byte> water, ReadOnlySpan<int> trees, ReadOnlySpan<int> props, int width, int height, int x, int y )
		{
			for ( int dy = -2; dy <= 0; dy++ )
				for ( int dx = -1; dx <= 1; dx++ )
				{
					if ( dy == -2 && dx == 1 ) continue;
					int px = x + dx, py = y + dy;
					if ( px < 0 || py < 0 || px >= 4 * width || py >= 4 * height ) return false;
					int v = py / 4 * (width + 1) + px / 4, h = GridHeight( heights, water, v );
					if ( h != GridHeight( heights, water, v + 1 ) || h != GridHeight( heights, water, v + width + 1 ) || h != GridHeight( heights, water, v + width + 2 ) ) return false;
				}
			for ( int side = 0; side < 3; side++ )
			{
				bool clear = true;
				for ( int forward = 1; clear && forward <= 3; forward++ )
					for ( int lateral = -1; clear && lateral <= 1; lateral++ )
					{
						int px = x + (side == 2 ? lateral : side == 0 ? -forward : forward);
						int py = y + (side == 2 ? -forward : lateral);
						int loc = py * (4 * width + 1) + px;
						clear = px >= 0 && py >= 0 && px <= 4 * width && py <= 4 * height && (GridValue( trees, loc ) == 0 || GridValue( props, loc ) != 0);
					}
				if ( clear ) return true;
			}
			return false;
		}

		bool ProxyFitsOpenGround( SourceNode proxy, bool open )
		{
			var data = tileData[proxy];
			if ( !data.Get( "conditional", false ) ) return true;
			int index = Array.FindIndex( data.Get<string[]>( "tile requirements_propertyNames", [] ), n => string.Equals( n, "openGround", StringComparison.OrdinalIgnoreCase ) );
			var property = ownerSet.GetValueOrDefault( proxy )?.Get<Element[]>( "properties", [] ).FirstOrDefault( p => string.Equals( p.Name, "openGround", StringComparison.OrdinalIgnoreCase ) );
			var value = index >= 0 ? data.Get<string[]>( "tile requirements_propertyValues", [] ).ElementAtOrDefault( index ) : property?.Get<string[]>( "values", [] ).FirstOrDefault();
			return value is not null && TileValueMatches( value, open ? "yes" : "no" );
		}
	}
}
