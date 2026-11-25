using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		readonly record struct GridBake( Element Grid, SourceNode[] Children, int LayerCount, bool Visible );

		static void AssignGridSkyboxIds( List<GridBake> grids, IEnumerable<SourceNode> authored )
		{
			if ( !grids.Any( g => g.Children.Length > g.LayerCount ) ) return;
			int lastId = Math.Max( 1, authored.Max( n => n.NodeId ) );
			int lastVisible = grids.FindLastIndex( g => g.Visible && g.Children.Length > g.LayerCount );
			for ( int i = 0; i <= lastVisible; i++ )
			{
				var grid = grids[i];
				if ( !grid.Visible ) continue;
				foreach ( var sky in grid.Children.AsSpan( grid.LayerCount ) ) sky.NodeId = ++lastId;
				if ( i < lastVisible ) lastId += CountBakedNodes( grid );
			}

			// Hidden placements stay editable and cannot consume a visible target's ID.
			foreach ( var grid in grids )
				if ( !grid.Visible )
					foreach ( var sky in grid.Children.AsSpan( grid.LayerCount ) ) sky.NodeId = ++lastId;
		}

		static int CountBakedNodes( GridBake grid )
		{
			// Hammer creates navigation markup unless it is baking a base-map override.
			int count = grid.Grid.Get( "compileAsOverride", true ) && !string.IsNullOrEmpty( grid.Grid.Get( "baseMapPath", "" ) ) ? 0 : 1;
			foreach ( var layer in grid.Children.AsSpan( 0, grid.LayerCount ) )
			{
				count += 2; // Layer group and global flow/opacity maps.
				foreach ( var set in layer.Children )
				{
					count++; // One instance-set group contains the tile and its selected proxies.
					count += CountPlacement( set );
				}
			}
			return count;

			static int CountPlacement( SourceNode placement )
			{
				var target = placement.Target;
				// A tile's children are cloned into a fresh group. Other targets are cloned whole.
				int count = 1 + CountClone( target ) - (target.TypeString == "CMapTile" ? 1 : 0);
				foreach ( var proxy in placement.Children ) count += CountPlacement( proxy );
				return count;
			}

			static int CountClone( SourceNode node )
			{
				if ( node.Hidden || node.EditorOnly ) return 0;
				int count = 1;
				foreach ( var child in node.Children ) count += CountClone( child );
				return count;
			}
		}

		SourceNode[] ReadDotaGrid( Element grid, Element[] layers, bool inherited, SourceNode parent, Func<string, SourceNode> readReference )
		{
			List<SourceNode> skyboxPrefabs = null;
			int skyboxes = 0;
			for ( int slot = 0; slot < 4; slot++ )
			{
				var path = grid.Get( $"skyboxMapPath{slot}", "" );
				if ( string.IsNullOrEmpty( path ) ) continue;
				// Hammer bakes skybox prefabs at 3/4 of the world limit, in absolute world coordinates.
				const int offset = 12288;
				var origin = new Vector3( slot is 0 or 3 ? offset : -offset, slot < 2 ? offset : -offset, -offset );
				var name = $"TileSet_Skybox{slot}";
				(skyboxPrefabs ??= []).Add( new SourceNode
				{
					Name = name,
					Kind = NodeKind.Instance,
					TypeString = "CMapPrefab",
					FixupEntityNames = true,
					TargetMapPath = path,
					Target = readReference( path ) ?? throw new FileNotFoundException( $"Missing skybox '{path}' on grid {parent.NodeId}." ),
					Transform = new Transform( origin ),
					Children = [],
					Meshes = []
				} );
				skyboxes |= 1 << slot;
			}
			var result = new SourceNode[layers.Length];
			var sets = new Dictionary<int, SourceNode>[4];
			var automaticTiles = new Dictionary<(int Slot, GroundCell Cell, byte Variation), TileChoice[]>();
			string[] setPaths = [grid.Get( "targetMapPath", "" ), grid.Get( "targetMapPath1", "" ), grid.Get( "targetMapPath2", "" ), grid.Get( "targetMapPath3", "" )];
			int cellWidth = grid.Get( "cellWidth", 256 ), cellHeight = grid.Get( "cellHeight", 256 ), step = grid.Get( "stepHeight", 128 );
			GridMaterialSets materialSets = null;
			bool? perEdge = null, waterRestricted = null;
			bool WaterRestricted() => waterRestricted ??= setPaths.Select( ( path, slot ) => string.IsNullOrEmpty( path ) ? null : GroundSet( Library( slot ) ) )
				.Any( set => set is not null && tileData[set].Get<Element[]>( "properties", [] ).Any( p => string.Equals( p.Name, "water", StringComparison.OrdinalIgnoreCase ) &&
					(p.Get( "targetTypeFlags", 1 ) & p.Get( "componentTypeFlags", 1 ) & 1) != 0 ) );
			bool PerEdgeRamps() => perEdge ??= setPaths.Select( ( path, slot ) => string.IsNullOrEmpty( path ) ? null : GroundSet( Library( slot ) ) )
				.Where( set => set is not null ).All( set => tileData[set].Get( "rampRuleSet", 0 ) == 1 );

			for ( int i = 0; i < layers.Length; i++ )
			{
				var layer = layers[i];
				int width = layer.Get( "gridWidth", 0 ), height = layer.Get( "gridHeight", 0 );
				var variations = inherited ? GridValues<byte>( layer, "cellsVariationId" ) : default;
				var state = GridValues<int>( layer, "cellConfiguration" );
				var namedState = GridValues<int>( layer, "cellConfigurationByName" );
				var slots = GridValues<int>( layer, "cellsTileSet" );
				var hidden = GridValues<byte>( layer, "cellsHidden" );
				var rotations = GridValues<int>( layer, "cellsOrientation" );
				var materials = GridValues<int>( layer, "cellsMaterialSet" );
				if ( layers.Length > 1 && materials.ContainsAnyExcept( 0 ) )
				{
					materialSets ??= new GridMaterialSets();
					materials = materialSets.ReloadLayer( materials, slots, i == 0, slot =>
						(uint)slot < sets.Length && !string.IsNullOrEmpty( setPaths[slot] ) && GroundSet( Library( slot ) ) is { } ground ? tileData[ground] : null );
				}
				var heights = GridValues<int>( layer, "verticesHeight" );
				var water = GridValues<byte>( layer, "verticesWater" );
				var paths = GridValues<byte>( layer, "edgesPath" );
				var destruction = GridValues<byte>( layer, "edgesDestruction" );
				var customTypes = GridValues<byte>( layer, "cellsCustomPathType" );
				var paint = new GroundLayerPaint( layer, width, new( cellWidth, cellHeight ), materials.ContainsAnyExcept( 0 ) );
				bool perEdgeRamps = (paths.ContainsAnyExcept( (byte)0 ) || destruction.ContainsAnyExcept( (byte)0 )) && PerEdgeRamps();
				var placements = new List<SourceNode>();
				Dictionary<int, Dictionary<string, string>> roadReplacements = null;

				int cells = checked(width * height);
				var groundCells = new SourceNode[cells];
				bool hasPaths = paths.ContainsAnyExcept( (byte)0 );
				bool hasConfiguration = state.Length >= cells, hasNames = namedState.Length >= cells;
				for ( int cell = 0; cell < cells; cell++ )
				{
					var path = hasConfiguration ? ReadGridPath( ref state, layer ) : default;
					var names = hasNames ? ReadGridPath( ref namedState, layer ) : default;
					if ( GridValue( hidden, cell ) != 0 ) continue;
					int slot = GridValue( slots, cell );
					int x = cell % width, y = cell / width, vertex = y * (width + 1) + x;
					if ( (uint)slot >= sets.Length ) throw new InvalidDataException( $"Invalid Dota tile set slot {slot}." );
					if ( string.IsNullOrEmpty( setPaths[slot] ) ) continue;
					int rotation = GridValue( rotations, cell );
					var contour = new GroundContour( GridHeight( heights, water, vertex ), GridHeight( heights, water, vertex + 1 ),
						GridHeight( heights, water, vertex + width + 2 ), GridHeight( heights, water, vertex + width + 1 ) );
					int z = contour.Minimum;
					int waterCorners = GridValue( water, vertex ) | (GridValue( water, vertex + 1 ) << 1) |
						(GridValue( water, vertex + width + 2 ) << 2) | (GridValue( water, vertex + width + 1 ) << 3);
					var nodes = Library( slot );
					var tile = path.Length >= 2 && path[1] == -1 ? nodes.GetValueOrDefault( path[0] ) : null;
					byte variation = cell < variations.Length ? variations[cell] : byte.MaxValue;
					if ( inherited || tile?.TypeString != "CMapTile" )
					{
						contour = contour.RelativeTo( z );
						var pathMask = GroundPathMask( paths, width, height, x, y, contour, perEdgeRamps );
						var customMask = GroundPathMask( destruction, width, height, x, y, contour, perEdgeRamps );
						var ground = new GroundCell( contour, waterCorners != 0, pathMask, customMask, customMask == 0 ? 0 : GridValue( customTypes, cell ) );
						if ( !automaticTiles.TryGetValue( (slot, ground, variation), out var choices ) )
							automaticTiles[(slot, ground, variation)] = choices = ReadGroundChoices( nodes, ground, step, variation );
						if ( choices.Length == 0 ) continue;
						if ( !ContainsTile( choices, tile, rotation ) )
						{
							path = default;
							if ( !inherited ) names = default;
							var choice = PickTileChoice( choices );
							tile = choice.Target;
							if ( choice.Rotation >= 0 ) rotation = choice.Rotation;
						}
					}
					int turns = rotation is >= 0 and <= 3 ? rotation : 0;
					int localWater = ((waterCorners | (waterCorners << 4)) >> turns) & 15;
					var pos = new Vector3( (x + 0.5f) * cellWidth, (y + 0.5f) * cellHeight, z * step );
					var transform = parent.Transform.ToWorld( new Transform( pos, Rotation.FromYaw( rotation is >= 0 and <= 3 ? rotation * 90 : 0 ) ) );
					int materialSet = GridValue( materials, cell );
					if ( materialSet > 0 && ownerSet[tile] is { } set )
					{
						// Hammer looks up the brush before loading the base material handles for this cell.
						int available = (materialSets ??= new GridMaterialSets()).Load( set, materialSet );
						if ( hasPaths && available > 0 )
							(roadReplacements ??= [])[cell] = GetTileMaterials( set, materialSet, -1, available );
					}
					var placement = PlaceTile( path, sets[slot], tile, transform, null, materialSet, localWater: localWater, names: names, skyboxSlot: (skyboxes & (1 << slot)) != 0 ? slot : -1 );
					groundCells[cell] = placement;
					if ( !hasPaths && paint.HasPaint )
						PaintGroundTile( placement, parent.Transform, x, y, paint, GridValue( materials, cell ) > 0 );
					placements.Add( placement );
				}
				if ( hasPaths )
				{
					// Hammer paints the initial layer on load before initializing its water restriction.
					// Baking multiple layers switches away and back, regenerating both with the restriction.
					bool restrictWater = layers.Length > 1 && water.ContainsAnyExcept( (byte)0 ) && WaterRestricted();
					PaintRoadLayer( groundCells, width, height, parent.Transform, grid.Get( "angles", System.Numerics.Vector3.Zero ), paint, paths, water, materials, restrictWater, roadReplacements );
				}
				JoinGroundSeams( groundCells, width, height );
				MergeGridWater( groundCells, width, height, water, heights );
				ReadGridObjects( layer, parent, width, height, cellWidth, cellHeight, step, placements, Library, setPaths, skyboxes, inherited );
				result[i] = new SourceNode
				{
					Name = layer.Name,
					Kind = NodeKind.GroupOrMesh,
					TypeString = i == 0 ? "CMapGroup" : "CMapWorldLayer",
					Transform = parent.Transform,
					Meshes = [],
					Children = placements.ToArray()
				};
			}
			if ( skyboxPrefabs is null ) return result;
			return [.. result, .. skyboxPrefabs];

			Dictionary<int, SourceNode> Library( int slot )
			{
				if ( (uint)slot >= sets.Length ) throw new InvalidDataException( $"Invalid Dota tile set slot {slot}." );
				if ( sets[slot] is null )
				{
					var name = setPaths[slot];
					if ( string.IsNullOrEmpty( name ) || readReference( name ) is not { } world )
						throw new FileNotFoundException( $"Missing Dota tile set '{name}' on grid {parent.NodeId}." );
					sets[slot] = GetPrefabNodes( world );
				}
				return sets[slot];
			}

		}

		// The high bit also marks authored height, fog and flow. Each has its own data stream.
		static bool HasSurfaceColorPaint( ReadOnlySpan<Color32> samples, bool hasMaterialOverrides )
		{
			foreach ( var sample in samples )
				if ( hasMaterialOverrides ? sample.a != 0 : ((PaintFlags)sample.a & PaintFlags.Surface) != 0 ) return true;
			return false;
		}

		static bool ContainsTile( TileChoice[] choices, SourceNode tile, int rotation )
		{
			if ( tile is null ) return false;
			foreach ( var choice in choices )
				if ( choice.Target == tile && (choice.Rotation < 0 || choice.Rotation == rotation) ) return true;
			return false;
		}

		static ReadOnlySpan<int> ReadGridPath( ref ReadOnlySpan<int> records, Element layer )
		{
			if ( records.IsEmpty || (uint)records[0] >= records.Length )
				throw new InvalidDataException( $"Invalid tile configuration on '{layer.Name}'." );
			var path = records.Slice( 1, records[0] );
			records = records[(path.Length + 1)..];
			return path;
		}

		static ReadOnlySpan<T> GridValues<T>( Element data, string key ) where T : unmanaged =>
			data.ContainsKey( key ) ? data.Numbers<T>( key ) : [];

		static T GridValue<T>( ReadOnlySpan<T> values, int index ) where T : unmanaged => index < values.Length ? values[index] : default;

		static int GridHeight( ReadOnlySpan<int> heights, ReadOnlySpan<byte> water, int index ) =>
			(short)GridValue( heights, index ) - GridValue( water, index );
	}
}
