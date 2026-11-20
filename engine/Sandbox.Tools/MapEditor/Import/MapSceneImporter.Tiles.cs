using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// On a rectangular tile with straight edges, Hammer's four-corner deformation
	// stretches points inside the tile and preserves their distance beyond each edge.
	internal readonly record struct RectangularTileFit( Vector2 BaseSize, Vector2 FaceSize, float HeightScale )
	{
		internal Vector3 FitPoint( Vector3 point )
		{
			static float FitAxis( float position, float size, float fittedSize ) =>
				position + Math.Clamp( position / size, -0.5f, 0.5f ) * (fittedSize - size);

			return new( FitAxis( point.x, BaseSize.x, FaceSize.x ), FitAxis( point.y, BaseSize.y, FaceSize.y ), point.z * HeightScale );
		}

		public Transform FitTransform( Transform transform )
		{
			var origin = FitPoint( transform.Position );
			var forward = FitPoint( transform.Position + transform.Rotation.Forward ) - origin;
			var left = FitPoint( transform.Position + transform.Rotation.Left ) - origin;
			return new Transform( origin, Rotation.LookAt( forward, forward.Cross( left ) ), transform.Scale );
		}
	}

	internal static int AlignedTileCorner( ReadOnlySpan<Vector3> quad, int origin, Transform world, int axis )
	{
		Vector3 WorldDirection( Vector3 delta ) => world.Rotation * (world.Scale * delta);
		Vector3 direction;
		switch ( axis )
		{
			case 0: direction = Vector3.Forward; break;
			case 1: direction = Vector3.Left; break;
			case 2: direction = Vector3.Up; break;
			case 3:
				var center = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
				direction = WorldDirection( (quad[3] + quad[0]) * 0.5f - center );
				for ( int i = 0; i < 3; i++ )
				{
					var midpoint = WorldDirection( (quad[i] + quad[i + 1]) * 0.5f - center );
					if ( midpoint.z > direction.z ) direction = midpoint;
				}
				break;
			default: return origin;
		}

		int selected = origin;
		float best = 0;
		for ( int i = 0; i < 4; i++ )
		{
			int start = (origin + i) & 3;
			var edge = WorldDirection( (quad[(start + 3) & 3] - quad[start]).Normal );
			float alignment = direction.Dot( edge );
			if ( alignment <= best ) continue;
			best = alignment;
			selected = start;
		}
		return selected;
	}

	sealed partial class VmapReader
	{
		readonly Dictionary<SourceNode, Element> tileData = new();
		readonly Dictionary<SourceNode, Element> ownerSet = new();
		readonly Dictionary<(Element Set, int Index, int SkyboxSlot, int Limit), Dictionary<string, string>> materialSets = new();
		readonly Dictionary<SourceNode, Dictionary<int, SourceNode>> tileSets = new();

		Dictionary<int, SourceNode> GetPrefabNodes( SourceNode world )
		{
			if ( tileSets.TryGetValue( world, out var tiles ) ) return tiles;
			tiles = new();
			void Visit( SourceNode node, Element set )
			{
				if ( node.NodeId > 0 ) tiles.Add( node.NodeId, node );
				if ( node.TypeString == "CMapTileSet" ) set = tileData[node];
				if ( node.TypeString is "CMapTile" or "CMapTileProxy" or "CMapGroupProxy" )
					ownerSet[node] = set;
				foreach ( var child in node.Children ) Visit( child, set );
			}
			Visit( world, null );
			tileSets.Add( world, tiles );
			return tiles;
		}

		Dictionary<string, string> GetTileMaterials( Element set, int index, int skyboxSlot, int limit = int.MaxValue )
		{
			if ( (index <= 0 || set is null) && skyboxSlot < 0 ) return null;
			var key = (set, index, skyboxSlot, limit);
			if ( materialSets.TryGetValue( key, out var existing ) ) return existing;
			var materials = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			if ( index > 0 && set?.Get<Element[]>( "materialSets", [] ) is { } sets && index < sets.Length )
			{
				var originals = sets[0].GetArray<string>( "materialNames" );
				var overrides = sets[index].GetArray<string>( "materialNames" );
				for ( int i = 0; i < Math.Min( limit, Math.Min( originals.Length, overrides.Length ) ); i++ )
					if ( !string.IsNullOrEmpty( overrides[i] ) && originals[i] != overrides[i] ) materials[originals[i]] = overrides[i];
			}
			if ( skyboxSlot >= 0 )
				materials["materials/tools/tools_tileset_skybox3d.vmat"] = $"materials/tools/tools_tileset_skybox3d_{skyboxSlot}.vmat";
			materialSets.Add( key, materials );
			return materials;
		}

		readonly Dictionary<(SourceNode Tile, Color32? Tint, Element Set, int MaterialSet, int SkyboxSlot), SourceNode> tileVariants = new();

		SourceNode GetTileVariant( SourceNode tile, Color32? tint, Element set, int materialSet, int skyboxSlot )
		{
			if ( tileVariants.TryGetValue( (tile, tint, set, materialSet, skyboxSlot), out var variant ) ) return variant;
			// Hammer bakes only the selected proxy paths. A proxy's candidate children
			// belong to its definition and must not appear in an ordinary tile placement.
			var materials = GetTileMaterials( set, materialSet, skyboxSlot );
			// An untyped null can bind to Color's string conversion and turn an untinted tile magenta.
			Color? meshTint = tint is { } color ? HammerRenderTint( color ) : (Color?)null;
			string renderColor = tint is { } rgb ? $"{rgb.r} {rgb.g} {rgb.b}" : null;
			SourceNode Copy( SourceNode source )
			{
				var copy = source.ShallowCopy();
				copy.TileMaterials = materials;
				if ( source.Meshes.Count > 0 && (materials is { Count: > 0 } || meshTint.HasValue) )
					copy.Meshes = source.Meshes.Select( part =>
					{
						if ( materials is { Count: > 0 } ) part = RemapTileMesh( part, materials );
						return meshTint is { } value ? part with { Tint = value } : part;
					} ).ToArray();
				if ( renderColor is not null && source.Kind == NodeKind.Entity && source.EntityKeys is not null )
				{
					copy.EntityKeys = new( source.EntityKeys, StringComparer.OrdinalIgnoreCase ) { ["rendercolor"] = renderColor };
					copy.Entity = CreateEntityEntry( copy.EntityKeys, copy.Transform );
				}
				copy.Children = source.Children.Where( x => x.TypeString is not ("CMapGroupProxy" or "CMapTileProxy") ).Select( Copy ).ToArray();
				return copy;
			}
			variant = Copy( tile );
			tileVariants.Add( (tile, tint, set, materialSet, skyboxSlot), variant );
			return variant;
		}

		static SceneMeshData RemapTileMesh( SceneMeshData part, Dictionary<string, string> materials )
		{
			if ( !part.Mesh.FaceHandles.Any( face => part.Mesh.GetFaceMaterial( face ) is { } material && materials.ContainsKey( material.Name ) ) ) return part;
			var copy = new PolygonMesh();
			copy.MergeMesh( part.Mesh, Transform.Zero, out _, out var edges, out var faces );
			bool transition = false;
			foreach ( var (original, face) in faces )
			{
				if ( part.Mesh.GetFaceMaterial( original ) is not { } material || !materials.TryGetValue( material.Name, out var replacement ) ) continue;
				copy.SetFaceMaterial( face, replacement );
				transition |= copy.GetFaceMaterial( face )?.Flags.GetBool( "dota.tile_transition_material" ) == true;
			}
			copy.SetSmoothingAngle( part.Smoothing );
			return part with { Mesh = copy, TransitionBlending = transition, SourcePaint = part.SourcePaint?.Where( p => edges.ContainsKey( p.Key ) ).ToDictionary( p => edges[p.Key], p => p.Value ) };
		}

		SourceNode[] ReadTiles( Element element, SourceNode parent, PolygonMesh mesh, Func<string, SourceNode> readReference )
		{
			var data = element.Get<Element>( "tileMeshData" );
			var faces = data.Numbers<int>( "faceIds" );
			var sets = data.Numbers<int>( "tileSetAssignments" );
			var names = element.GetArray<string>( "tileSetMapNames" );
			var referenced = new Dictionary<int, SourceNode>[names.Length];
			var state = data.Numbers<int>( "tileConfiguration" );
			var materials = data.Numbers<int>( "materialSetAssignments", optional: true );
			var tinted = data.Numbers<byte>( "tintColorOverrides", optional: true );
			var colors = data.Numbers<Color32>( "tintColorAssignments", optional: true );
			var assignments = new Dictionary<int, int>( faces.Length );
			for ( int i = 0; i < faces.Length; i++ ) assignments.Add( faces[i], sets[i] );
			var placements = new List<SourceNode>( faces.Length );
			var hidden = new List<HalfEdgeMesh.FaceHandle>();
			for ( int i = 0; i < faces.Length && !state.IsEmpty; i++ )
			{
				int size = state[0];
				if ( (uint)size >= state.Length ) throw new InvalidDataException( $"Invalid tile configuration on {parent.NodeId}." );
				var path = state.Slice( 1, size );
				state = state[(size + 1)..];
				if ( path.IsEmpty ) continue;
				int rootId = TileRootId( path );
				int set = sets[i];
				if ( (uint)set >= names.Length ) throw new InvalidDataException( $"Invalid tile set {set} on node {parent.NodeId}." );
				referenced[set] ??= GetPrefabNodes( readReference( names[set] ) ?? throw new FileNotFoundException( $"Cannot find tile set '{names[set]}'." ) );
				if ( !referenced[set].TryGetValue( rootId, out var tile ) || tile.TypeString != "CMapTile" )
					throw new InvalidDataException( $"Tile {rootId} not found in '{names[set]}'." );
				var face = mesh.FaceHandleFromIndex( faces[i] );
				if ( !face.IsValid ) continue;
				var definition = tileData[tile];
				float degrees = ownerSet[tile]?.Get( "convexityAngle", 45f ) ?? 45f;
				var transform = TileTransform( mesh, face, definition, parent.Transform, MathF.Cos( degrees.DegreeToRadian() ), assignments,
					element.Get( "tileHeightScale", 1f ), out var fit );
				if ( transform is null ) continue;
				var placement = parent.Transform.ToWorld( transform.Value );
				Color32? tint = null;
				if ( i < tinted.Length && tinted[i] != 0 )
					tint = i < colors.Length ? colors[i] : Color32.White;
				int materialSet = i < materials.Length ? materials[i] : 0;
				placements.Add( PlaceTile( path, referenced[set], tile, placement, tint, materialSet, fit: fit ) );
				if ( !definition.Get( "displayBaseFace", false ) ) hidden.Add( face );
			}
			mesh.RemoveFaces( hidden );
			if ( !mesh.FaceHandles.Any() ) parent.Meshes = [];
			return placements.ToArray();
		}

		static int TileRootId( ReadOnlySpan<int> records )
		{
			while ( !records.IsEmpty )
			{
				int end = records.IndexOf( -1 );
				if ( end < 1 ) break;
				if ( end == 1 ) return records[0];
				records = records[(end + 1)..];
			}
			throw new InvalidDataException( "Missing tile root in saved tile configuration." );
		}

		SourceNode PlaceTile( ReadOnlySpan<int> path, Dictionary<int, SourceNode> nodes, SourceNode tile, Transform placement, Color32? tint, int materialSet, Transform? proxyPlacement = null, int? localWater = null, ReadOnlySpan<int> names = default, bool? openGround = null, int skyboxSlot = -1, RectangularTileFit? fit = null )
		{
			var root = Place( tile, placement );
			if ( localWater.HasValue || proxyPlacement.HasValue )
				root.Children = PickTileProxies( tile, proxyPlacement ?? placement, localWater, openGround, path, names, Place );
			else if ( path.Length > 2 )
				root.Children = ReadTileProxies( path, nodes, tile, placement, fit, Place );
			return root;

			SourceNode Place( SourceNode target, Transform at ) => new()
			{
				Name = target.Name,
				Kind = NodeKind.Instance,
				TypeString = "CMapTileInstance",
				Target = GetTileVariant( target, tint, ownerSet[tile], materialSet, skyboxSlot ),
				Transform = at,
				Meshes = [],
				Children = []
			};
		}

		// Each record contains a target ID followed by its instance/proxy path. The root path is empty.
		SourceNode[] ReadTileProxies( ReadOnlySpan<int> records, Dictionary<int, SourceNode> nodes, SourceNode tile,
			Transform placement, RectangularTileFit? fit, Func<SourceNode, Transform, SourceNode> place )
		{
			var results = new List<SourceNode>();
			var allRecords = records;
			while ( !records.IsEmpty )
			{
				int end = records.IndexOf( -1 );
				if ( end < 1 ) throw new InvalidDataException( $"Invalid proxy path for tile {tile.NodeId}." );
				var path = records[1..end];
				int target = records[0];
				records = records[(end + 1)..];
				if ( path.IsEmpty || target == 0 ) continue;
				var parent = tile;
				var transform = Transform.Zero;
				bool deform = fit.HasValue;
				bool orientUp = false;
				for ( int i = 0; i < path.Length; i++ )
				{
					var source = nodes[path[i]];
					orientUp |= source.ProxyOrientUp;
					transform = transform.ToWorld( parent.Transform.ToLocal( source.Transform ) );
					if ( deform && (i == path.Length - 1 || (tileData.TryGetValue( source, out var proxy ) && !proxy.Get( "contentsDeform", true ))) )
					{
						transform = fit.Value.FitTransform( transform );
						deform = false;
					}
					if ( i + 1 < path.Length )
					{
						parent = source.Target ?? FindSelectedParent( allRecords, path[..(i + 1)], nodes );
					}
				}
				transform = placement.ToWorld( transform );
				if ( orientUp ) transform = transform.WithRotation( Rotation.FromYaw( transform.Rotation.Yaw() ) );
				results.Add( place( nodes[target], transform ) );
			}
			return results.ToArray();

			static SourceNode FindSelectedParent( ReadOnlySpan<int> previous, ReadOnlySpan<int> prefix, Dictionary<int, SourceNode> nodes )
			{
				while ( !previous.IsEmpty )
				{
					int end = previous.IndexOf( -1 );
					if ( end < 1 ) break;
					if ( previous[1..end].SequenceEqual( prefix ) ) return nodes[previous[0]];
					previous = previous[(end + 1)..];
				}
				throw new InvalidDataException( $"Missing parent selection for tile proxy {prefix[^1]}." );
			}
		}

		static Transform? TileTransform( PolygonMesh mesh, HalfEdgeMesh.FaceHandle face, Element tile, Transform world, float threshold, Dictionary<int, int> assignments,
			float heightScale, out RectangularTileFit? fit )
		{
			fit = null;
			mesh.GetFaceVerticesConnectedToFace( face, out var edges );
			if ( edges.Length < 4 ) return null;
			var positions = edges.Select( edge => mesh.GetVertexPosition( mesh.GetVertexConnectedToFaceVertex( edge ) ) ).ToArray();
			var corners = Enumerable.Range( 0, edges.Length );
			if ( edges.Length > 4 )
			{
				float CornerScore( int i )
				{
					var before = positions[(i + edges.Length - 1) % edges.Length] - positions[i];
					var after = positions[(i + 1) % edges.Length] - positions[i];
					return MathF.Abs( before.Normal.Dot( after.Normal ) );
				}
				corners = corners.OrderBy( CornerScore ).Take( 4 ).Order();
			}
			var indices = corners.ToArray();
			var quad = indices.Select( i => positions[i] ).ToArray();
			int origin = 0;
			float best = -1;
			for ( int i = 0; i < 4; i++ )
			{
				int prev = (i + 3) % 4;
				var dir = mesh.GetTextureCoord( edges[indices[i]] ) - mesh.GetTextureCoord( edges[indices[prev]] );
				if ( dir.Normal.x <= best ) continue;
				best = dir.Normal.x;
				origin = prev;
			}
			var up = PolygonTriangulation.PolygonNormal( quad );
			int axis = tile.TryGetValue( "alignToAxis", out var alignment ) ? (int)alignment : -1;
			int selected = axis >= 0 ? AlignedTileCorner( quad, origin, world, axis ) : MatchEdges();
			var forward = quad[(selected + 1) % 4] - quad[selected];
			var left = quad[(selected + 3) % 4] - quad[selected];
			if ( edges.Length == 4 && MathF.Abs( forward.Normal.Dot( left.Normal ) ) < 0.00001f &&
				(quad[selected] + forward + left).AlmostEqual( quad[(selected + 2) % 4], 0.001f ) &&
				Array.TrueForAll( edges, edge => !edge.OppositeEdge.Face.IsValid || mesh.GetEdgeSmoothing( edge ) == PolygonMesh.EdgeSmoothMode.Default ) )
			{
				Vector2 size = tile.Get( "baseSize", new System.Numerics.Vector2( 256 ) );
				Vector2 extent = new( forward.Length, left.Length );
				if ( size.x > 0 && size.y > 0 && (size != extent || heightScale != 1) )
					fit = new( size, extent, heightScale );
			}
			var center = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f;
			return new Transform( center, Rotation.LookAt( up.Cross( forward ).Cross( up ), up ) );

			int MatchEdges()
			{
				var edgeTypes = new string[4];
				var differentSet = new bool[4];
				for ( int i = 0; i < 4; i++ )
				{
					int next = (i + 1) & 3;
					var neighbor = edges[indices[next]].OppositeEdge.Face;
					if ( !neighbor.IsValid )
					{
						edgeTypes[i] = "open";
						continue;
					}
					differentSet[i] = assignments.TryGetValue( neighbor.Index, out var otherSet ) && otherSet != assignments[face.Index];
					mesh.GetFaceVerticesConnectedToFace( neighbor, out var ring );
					var otherNormal = PolygonTriangulation.PolygonNormal( ring.Select( e => mesh.GetVertexPosition( e.Vertex ) ).ToArray() );
					edgeTypes[i] = up.Dot( otherNormal ) > threshold ? "flat" :
						(quad[next] - quad[i]).Dot( up.Cross( otherNormal ) ) > 0 ? "convex" : "concave";
				}

				return SelectTileOrientation( tile, quad, origin, edgeTypes, differentSet );
			}
		}

		static int SelectTileOrientation( Element tile, Vector3[] quad, int origin, string[] edgeTypes, bool[] differentSet )
		{
			var properties = tile.Get<string[]>( "propertyNames", [] );
			string[][] rules = [tile.Get<string[]>( "edgePropertyValues0", [] ), tile.Get<string[]>( "edgePropertyValues1", [] ),
				tile.Get<string[]>( "edgePropertyValues2", [] ), tile.Get<string[]>( "edgePropertyValues3", [] )];
			var size = tile.Get( "baseSize", new System.Numerics.Vector2( 256 ) );
			float Extent( int i ) => (quad[(i + 1) & 3].Distance( quad[i & 3] ) + quad[(i + 3) & 3].Distance( quad[(i + 2) & 3] )) * 0.5f;
			bool bySize = tile.Get( "pickBySize", true );
			float width = Extent( 0 ), height = Extent( 1 );
			float bestFit = float.PositiveInfinity;
			int selected = origin;
			for ( int orientation = 0; orientation < 4; orientation++ )
			{
				int start = (origin + orientation) & 3;
				bool matches = true;
				for ( int side = 0; side < 4 && matches; side++ )
				{
					int i = (start + side) & 3;
					var values = rules[side];
					for ( int p = 0; p < properties.Length; p++ )
					{
						var actual = properties[p] switch
						{
							"mesh_edge_connectivity" => edgeTypes[i] == "open" ? "open" : "closed",
							"mesh_edge_convexity" => edgeTypes[i],
							"mesh_edge_convexity_or_tileset" => edgeTypes[i] == "open" || differentSet[i] ? "open_or_different_tileset" : edgeTypes[i],
							_ => null
						};
						if ( p >= values.Length ) continue;
						var expected = values[p];
						if ( actual is null || expected.StartsWith( '*' ) ) continue;
						if ( expected.StartsWith( '~' ) ? actual == expected[1..] : actual != expected ) matches = false;
					}
				}
				if ( !matches ) continue;
				bool swapped = (start & 1) != 0;
				float x = MathF.Abs( (swapped ? height : width) - size.X );
				float y = MathF.Abs( (swapped ? width : height) - size.Y );
				float fit = bySize ? x * y + x + y : 0;
				if ( fit + 1 <= bestFit )
				{
					bestFit = fit;
					selected = start;
				}
			}
			return selected;
		}
	}
}
