using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		readonly Dictionary<SourceNode, SourceNode[]> waterMeshes = [];

		SourceNode[] GetWaterMeshes( SourceNode target )
		{
			if ( waterMeshes.TryGetValue( target, out var meshes ) ) return meshes;
			var nodes = new List<SourceNode>();
			void Visit( SourceNode node )
			{
				if ( node.TypeString is "CMapMesh" or "CMapTileMesh" && node.Meshes.Count > 0 &&
					node.Meshes[0].Mesh.Materials.Any( material => material?.Flags.GetInt( "bNeedsDynamicReflectionMap" ) == 1 ) )
					nodes.Add( node );
				foreach ( var child in node.Children ) Visit( child );
			}
			Visit( target );
			return waterMeshes[target] = nodes.ToArray();
		}

		void MergeGridWater( SourceNode[] cells, int width, int height, ReadOnlySpan<byte> water, ReadOnlySpan<int> heights )
		{
			int stride = checked(width + 1);
			water = water[..Math.Min( water.Length, checked(stride * (height + 1)) )];
			if ( !water.ContainsAnyExcept( (byte)0 ) ) return;

			var visited = new bool[water.Length];
			var vertices = new List<int>();
			var adjacent = new HashSet<int>();
			var stripped = new Dictionary<SourceNode, SourceNode>();
			var parts = new List<(SourceNode Instance, SourceNode Mesh, Transform World)>();
			for ( int start = 0; start < water.Length; start++ )
			{
				if ( visited[start] || water[start] == 0 ) continue;
				vertices.Clear();
				adjacent.Clear();
				parts.Clear();
				int level = GridHeight( heights, water, start );
				vertices.Add( start );
				visited[start] = true;
				for ( int i = 0; i < vertices.Count; i++ )
				{
					int x = vertices[i] % stride, y = vertices[i] / stride;
					for ( int cy = Math.Max( 0, y - 1 ); cy <= Math.Min( height - 1, y ); cy++ )
						for ( int cx = Math.Max( 0, x - 1 ); cx <= Math.Min( width - 1, x ); cx++ )
							adjacent.Add( cy * width + cx );

					for ( int ny = Math.Max( 0, y - 1 ); ny <= Math.Min( height, y + 1 ); ny++ )
						for ( int nx = Math.Max( 0, x - 1 ); nx <= Math.Min( width, x + 1 ); nx++ )
						{
							int next = ny * stride + nx;
							if ( next >= water.Length || visited[next] || water[next] == 0 || GridHeight( heights, water, next ) != level ) continue;
							visited[next] = true;
							vertices.Add( next );
						}
				}

				// Hammer keeps the first reflective mesh in increasing cell order, including each cell's selected proxies.
				foreach ( int cell in adjacent.Order() )
				{
					if ( cells[cell] is not { } tile ) continue;
					Add( tile );
					foreach ( var proxy in tile.Children ) Add( proxy );
				}
				if ( parts.Count < 2 ) continue;
				var first = parts[0];
				var merged = new PolygonMesh();
				var surface = first.Mesh.Meshes[0];
				// Hammer keeps the destination corner normals and recomputes normals on appended faces.
				merged.MergeMesh( surface.Mesh, Transform.Zero, out _, out _, out _ );
				for ( int i = 1; i < parts.Count; i++ )
				{
					var part = parts[i];
					merged.MergeMesh( part.Mesh.Meshes[0].Mesh, first.World.ToLocal( part.World ), preserveNormals: false, out _, out _, out _ );
				}
				merged.SetSmoothingAngle( surface.Smoothing );

				foreach ( var group in parts.GroupBy( part => part.Instance ) )
				{
					var instance = group.Key;
					var target = instance.Target;
					if ( instance != first.Instance && stripped.TryGetValue( target, out var shared ) )
					{
						instance.Target = shared;
						continue;
					}
					var edits = new Dictionary<SourceNode, SceneMeshData[]>();
					foreach ( var part in group ) edits[part.Mesh] = part == first ? [surface with { Mesh = merged, SourcePaint = null }] : [];
					instance.Target = Replace( target, edits );
					if ( instance != first.Instance ) stripped.Add( target, instance.Target );
				}
			}

			void Add( SourceNode instance )
			{
				var meshes = GetWaterMeshes( instance.Target );
				if ( meshes.Length == 0 ) return;
				var delta = PlacementDelta( instance.Transform, instance.Target );
				foreach ( var mesh in meshes ) parts.Add( (instance, mesh, delta.ToWorld( mesh.Transform )) );
			}
		}
	}
}
