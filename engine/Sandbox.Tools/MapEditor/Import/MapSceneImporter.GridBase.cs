using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		readonly Dictionary<string, (Element[] Layers, Vector3 Origin)> baseLayers = new( StringComparer.OrdinalIgnoreCase );
		static Element[] AuthoredLayers( Element grid ) => grid.Get<Element[]>( "layerData", null )
			?? (grid.GetValueOrDefault( "tileGridData" ) is Element legacy ? [legacy] : []);

		Element[] GridLayers( Element grid, string filename, out bool inherited, out Vector3 origin )
		{
			var local = AuthoredLayers( grid );
			var path = grid.Get( "baseMapPath", "" );
			inherited = false;
			origin = grid.Get( "origin", System.Numerics.Vector3.Zero );
			if ( string.IsNullOrEmpty( path ) ) return local;
			var file = ReferencePath( filename, path );
			if ( file is null ) throw new FileNotFoundException( $"Missing base map '{path}' in '{filename}'." );
			if ( !baseLayers.TryGetValue( file, out var definition ) )
			{
				// Hammer reads the referenced document's saved grid. Its other meshes, prefabs and base map do not participate.
				var elements = ReadMapFile( file, FindOpenDocument( file ) );
				var root = elements.FirstOrDefault()?.Get<Element>( "world", null ) ?? throw new InvalidDataException( $"Missing world in grid base '{file}'." );
				if ( FirstGrid( root, new() ) is { } source )
					definition = (AuthoredLayers( source ), source.Get( "origin", System.Numerics.Vector3.Zero ));
				baseLayers[file] = definition;
			}
			if ( definition.Layers is not { } layers ) return local;
			origin = definition.Origin;

			inherited = true;
			var result = new Element[layers.Length];
			for ( int i = 0; i < result.Length; i++ )
				result[i] = i < local.Length ? ConformLayer( local[i], layers[i] ) : layers[i];
			return result;
		}

		static Element FirstGrid( Element node, HashSet<Element> visited )
		{
			if ( !visited.Add( node ) ) return null;
			if ( node.ClassName == "CMapDotaTileGrid" ) return node;
			foreach ( var child in node.SceneChildren )
				if ( child.ClassName != "CMapWorld" && FirstGrid( child, visited ) is { } grid ) return grid;
			return null;
		}

		static readonly string[] ObjectTypeFields = ["objectsPropType", "objectsTreeType", "objectsPlantType"];
		static readonly string[] BaseFields =
		[
			"gridWidth", "gridHeight", "cellsTileSet", "cellsHidden", "cellsOrientation", "cellsVariationId",
			"cellConfigurationByName", "objectConfigurationByName", "edgesPath", "edgesDestruction", "verticesHeight", "verticesWater",
			"objectsTileSet", "objectsTreeSize", "objectsRotation", "objectsPitch", "objectsVariationId", "gridnavFlags"
		];

		static Element ConformLayer( Element local, Element source )
		{
			var layer = new Element( local.ClassName, source.Name );
			foreach ( var (key, value) in local ) layer.Add( key, value );
			foreach ( var key in BaseFields )
			{
				if ( source.TryGetValue( key, out var value ) ) layer[key] = value;
				else layer.Remove( key );
			}
			var srcTables = source.Get<Element[]>( "tileSetMapInfo", [] );
			var dstTables = local.Get<Element[]>( "tileSetMapInfo", [] );
			var slots = GridValues<int>( source, "objectsTileSet" );

			for ( int category = 0; category < ObjectTypeFields.Length; category++ )
			{
				var field = ObjectTypeFields[category];
				if ( !source.TryGetValue( field, out var data ) ) { layer.Remove( field ); continue; }
				layer[field] = data;
				var values = GridValues<int>( source, field );
				var tables = new int[Math.Min( 4, Math.Min( srcTables.Length, dstTables.Length ) )][];
				for ( int slot = 0; slot < tables.Length; slot++ )
				{
					var src = srcTables[slot].Get<string[]>( TypeTables[category], [] );
					var dst = dstTables[slot].Get<string[]>( TypeTables[category], [] );
					if ( !src.AsSpan().SequenceEqual( dst ) )
						tables[slot] = Array.ConvertAll( src, name => Math.Max( 0, Array.FindIndex( dst, v => string.Equals( v, name, StringComparison.OrdinalIgnoreCase ) ) ) );
				}
				int[] remapped = null;
				for ( int i = 0; i < values.Length; i++ )
				{
					int type = values[i], slot = GridValue( slots, i );
					if ( type == 0 || (uint)slot >= tables.Length || tables[slot] is not { } ids ) continue;
					int id = (uint)type < ids.Length ? ids[type] : 0;
					if ( id != type ) (remapped ??= values.ToArray())[i] = id;
				}
				if ( remapped is not null ) layer[field] = remapped;
			}
			ResizeLocalGridFields( local, layer );
			return layer;
		}

		static void ResizeLocalGridFields( Element local, Element layer )
		{
			int w = local.Get( "gridWidth", 0 ), h = local.Get( "gridHeight", 0 );
			int width = layer.Get( "gridWidth", 0 ), height = layer.Get( "gridHeight", 0 );
			if ( w == width && h == height ) return;
			int dx = (w - width) / 2, dy = (h - height) / 2;
			ResizeValues<int>( "cellsMaterialSet", 1, 0 );
			ResizeValues<byte>( "cellsCustomPathType", 1, 0 );
			ResizePaths( "cellConfiguration", 1, 0 );
			ResizePaths( "objectConfiguration", 4, 1 );
			ResizeValues<Color32>( "blendOpacity", 8, 1, new( 0, 255, 0, 128 ) );
			ResizeValues<Color32>( "blendColor", 8, 1, new( 255, 255, 255, 0 ) );
			ResizeValues<Color32>( "blendTransitionAndPath", 8, 1, new( 0, 255, 0, 0 ) );
			ResizeValues<Color32>( "flowMap", 8, 1, new( 10, 20, 0, 0 ) );
			ResizeValues<Color32>( "fogFlowMap", 8, 1, new( 10, 20, 0, 0 ) );
			ResizeValues<byte>( "blendHeight", 8, 1, 128 );
			ResizeValues<byte>( "grassOpacity", 8, 1, byte.MaxValue );
			ResizeValues<byte>( "fogOpacity", 8, 1, byte.MaxValue );

			int SourceIndex( int i, int scale, int extra, bool clamp )
			{
				int x = i % (width * scale + extra) + dx * scale, y = i / (width * scale + extra) + dy * scale;
				int cols = w * scale + extra, rows = h * scale + extra;
				if ( cols <= 0 || rows <= 0 ) return -1;
				if ( clamp ) return Math.Clamp( x, 0, cols - 1 ) + Math.Clamp( y, 0, rows - 1 ) * cols;
				return (uint)x < cols && (uint)y < rows ? x + y * cols : -1;
			}

			void ResizeValues<T>( string field, int scale, int extra, T fallback = default ) where T : unmanaged
			{
				var values = GridValues<T>( local, field );
				if ( values.IsEmpty ) return;
				var result = new T[checked((width * scale + extra) * (height * scale + extra))];
				for ( int i = 0; i < result.Length; i++ )
				{
					int old = SourceIndex( i, scale, extra, true );
					result[i] = (uint)old < values.Length ? values[old] : fallback;
				}
				layer[field] = result;
			}

			void ResizePaths( string field, int scale, int extra )
			{
				var records = GridValues<int>( local, field );
				if ( records.IsEmpty ) return;
				int count = checked((w * scale + extra) * (h * scale + extra));
				if ( records.Length < count ) { layer.Remove( field ); return; }
				var offsets = new int[count];
				var tail = records;
				for ( int i = 0; i < count; i++ )
				{
					offsets[i] = records.Length - tail.Length;
					ReadGridPath( ref tail, local );
				}
				int length = checked((width * scale + extra) * (height * scale + extra));
				var result = new List<int>( length );
				for ( int i = 0; i < length; i++ )
				{
					int old = SourceIndex( i, scale, extra, scale == 1 );
					if ( old < 0 ) { result.Add( 0 ); continue; }
					int offset = offsets[old];
					result.AddRange( records.Slice( offset, records[offset] + 1 ) );
				}
				layer[field] = result.ToArray();
			}
		}
	}
}
