using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static SceneMeshData ReadVmapMesh( Element source, string entityClass, bool tileLibrary = false )
	{
		var definition = entityClass is null ? null : GameData.EntityClasses.FirstOrDefault( x => string.Equals( x.Name, entityClass, StringComparison.OrdinalIgnoreCase ) );
		string materialOverride = definition?.Metadata.GetValueOrDefault( "auto_apply_material" ) as string;
		PolygonMesh mesh;
		bool indexedSecondaryBlend;
		string[] tileMaterials = null;
		var sourcePaint = tileLibrary ? new Dictionary<HalfEdgeMesh.HalfEdgeHandle, TileCornerPaint>() : null;
		float angle = source.Get( "smoothingAngle", 40f );
		if ( source.GetValueOrDefault( "dmemesh" ) is Element legacy )
		{
			mesh = ReadDmeMesh( legacy, materialOverride, sourcePaint, out indexedSecondaryBlend );
			if ( tileLibrary ) tileMaterials = mesh.Materials.Where( m => m is not null ).Select( m => m.Name ).ToArray();
		}
		else
			mesh = ReadPolygonData( source, materialOverride, ref angle, sourcePaint, out indexedSecondaryBlend, out tileMaterials );
		mesh.SetSmoothingAngle( angle );
		var physics = source.Get( "physicsType", "default" ) switch
		{
			_ when entityClass is "ent_nav_blocker" or "func_water" or "post_processing_entity" or "post_processing_volume" => SceneMeshPhysics.None,
			"none" => SceneMeshPhysics.None,
			"convex_single" => SceneMeshPhysics.SingleHull,
			"convex_multi" => SceneMeshPhysics.MultipleHulls,
			"sphere" => SceneMeshPhysics.Sphere,
			"capsule" => SceneMeshPhysics.Capsule,
			"convex_decomposition" => SceneMeshPhysics.ConvexDecomposition,
			"default" when entityClass is not null && definition?.Tags.Contains( "PhysicsTypeOverride_Mesh" ) != true => SceneMeshPhysics.MultipleHulls,
			_ => SceneMeshPhysics.Mesh
		};
		var tint = source.Get( "tintColor", new Color32( 255, 255, 255, 255 ) );
		bool guide = entityClass is "func_nav_markup" or "light_importance_volume";
		return new( mesh, angle, guide ? SceneMeshPhysics.None : physics, HammerRenderTint( tint ), !guide && !source.Get( "disableShadows", false ),
			source.GetValueOrDefault( "physicsTags" ) as string ?? "solid", HideInGame: guide,
			DisableHeightDisplacement: source.Get( "disableHeightDisplacement", false ), IndexedSecondaryBlend: indexedSecondaryBlend,
			SourcePaint: sourcePaint is { Count: > 0 } ? sourcePaint : null,
			TileMaterialPaths: tileMaterials, CollisionError: source.Get( "physicsSimplificationOverride", false )
				? source.Get( "physicsSimplificationError", 0f ) : 0.1f );
	}

	static PolygonMesh ReadPolygonData( Element source, string materialOverride, ref float angle, Dictionary<HalfEdgeMesh.HalfEdgeHandle, TileCornerPaint> sourcePaint, out bool indexedSecondaryBlend, out string[] tileMaterials )
	{
		var data = source.Get<Element>( "meshData" );
		ReadOnlySpan<T> Stream<T>( string domain, string semantic, int index = 0 ) where T : unmanaged
		{
			foreach ( var stream in data.Get<Element>( domain ).GetArray<Element>( "streams" ) )
				if ( stream.Get( "semanticIndex", 0 ) == index && stream.Get<string>( "semanticName" ).Equals( semantic, StringComparison.OrdinalIgnoreCase ) )
					return stream.Numbers<T>( "data" );
			return default;
		}

		var firstEdges = data.Numbers<int>( "faceEdgeIndices" );
		var faceData = data.Numbers<int>( "faceDataIndices" );
		var flags = Stream<int>( "faceData", "flags" );
		var materials = data.GetArray<string>( "materials" );
		var materialIds = Stream<int>( "faceData", "materialindex" );
		tileMaterials = null;
		if ( sourcePaint is not null )
		{
			// Hammer removes unused slots while preserving the order of the remaining materials.
			var used = new bool[materials.Length];
			foreach ( int index in materialIds )
				if ( (uint)index < used.Length ) used[index] = true;
			tileMaterials = materials.Where( ( _, i ) => used[i] ).ToArray();
		}
		var mesh = new PolygonMesh();
		var ends = data.Numbers<int>( "edgeVertexIndices" );
		var next = data.Numbers<int>( "edgeNextIndices" );
		LoadTopology( mesh.Topology, data.Numbers<int>( "vertexEdgeIndices" ), ends, data.Numbers<int>( "edgeOppositeIndices" ),
			next, data.Numbers<int>( "edgeFaceIndices" ), firstEdges );
		var positions = Stream<N.Vector3>( "vertexData", "position" );
		var vertexData = data.Numbers<int>( "vertexDataIndices" );
		foreach ( var vertex in mesh.VertexHandles )
			mesh.SetVertexPosition( vertex, positions[vertexData[vertex.Index]] );

		var edgeData = data.Numbers<int>( "edgeDataIndices" );
		var cornerData = data.Numbers<int>( "edgeVertexDataIndices" );
		var uvs = Stream<N.Vector2>( "faceVertexData", "texcoord" );
		var colors = Stream<N.Vector4>( "faceVertexData", "VertexPaintTintColor" );
		var blends = Stream<N.Vector4>( "faceVertexData", "VertexPaintBlendParams" );
		var secondary = Stream<N.Vector4>( "faceVertexData", "VertexPaintBlendParams", 1 );
		indexedSecondaryBlend = !secondary.IsEmpty;
		if ( secondary.IsEmpty ) secondary = Stream<N.Vector4>( "faceVertexData", "VertexPaintBlendParams1" );
		var edgeFlags = Stream<int>( "edgeData", "flags" );
		bool hasOverride = !string.IsNullOrEmpty( materialOverride );
		var faceMaterials = new Material[hasOverride ? 1 : materials.Length];
		List<HalfEdgeMesh.FaceHandle> hidden = null;
		foreach ( var face in mesh.FaceHandles )
		{
			int index = faceData[face.Index];
			int material = materialIds.IsEmpty ? -1 : materialIds[index];
			if ( (!flags.IsEmpty && (flags[index] & 2) != 0) || (uint)material >= materials.Length )
			{
				(hidden ??= []).Add( face );
				continue;
			}
			var faceMaterial = faceMaterials[hasOverride ? 0 : material] ??= Material.Load( hasOverride ? materialOverride : materials[material] );
			mesh.SetFaceMaterial( face, faceMaterial );
			int first = firstEdges[face.Index], edge = first;
			int count = 0;
			do
			{
				if ( ++count > next.Length ) throw new InvalidDataException( $"Invalid face loop in VMAP mesh '{source.Name}'." );
				var corner = mesh.HalfEdgeHandleFromIndex( edge );
				int attr = cornerData[edge];
				int smoothing = edgeFlags.IsEmpty ? 0 : edgeFlags[edgeData[edge]];
				mesh.SetTextureCoord( corner, uvs.IsEmpty ? default : uvs[attr] );
				SetCornerPaint( mesh, corner, colors.IsEmpty ? N.Vector4.One : colors[attr], blends.IsEmpty ? default : blends[attr], sourcePaint );
				if ( !secondary.IsEmpty ) mesh.SetSecondaryVertexBlend( corner, PackVertexPaint( secondary[attr] ) );
				mesh.SetEdgeSmoothing( corner, (PolygonMesh.EdgeSmoothMode)((smoothing & 1) != 0 ? 1 : smoothing & 2) );
				edge = next[edge];
			} while ( edge != first );
		}

		if ( edgeFlags.IsEmpty ) angle = 0;
		mesh.RemoveBadFaces();
		if ( hidden is not null ) mesh.RemoveFaces( hidden.Where( face => face.IsValid ) );
		var normals = Stream<N.Vector3>( "faceVertexData", "normal" );
		if ( !normals.IsEmpty )
		{
			mesh.SetSmoothingAngle( angle );
			mesh.SetIndexedCornerNormals( normals, cornerData );
		}
		BakeSubdivision( mesh, data, colors, blends, secondary, sourcePaint );
		return mesh;
	}

	// Import an existing half-edge graph without reconstructing adjacency one face at a time.
	internal static void LoadTopology( HalfEdgeMesh.Mesh mesh, ReadOnlySpan<int> vertices, ReadOnlySpan<int> ends, ReadOnlySpan<int> opposites,
		ReadOnlySpan<int> next, ReadOnlySpan<int> owners, ReadOnlySpan<int> faces )
	{
		if ( ends.Length != opposites.Length || ends.Length != next.Length || ends.Length != owners.Length )
			throw new InvalidDataException( "Inconsistent half-edge stream lengths." );

		// Each live edge must have exactly one predecessor. Otherwise a loop can enter
		// another cycle without ever returning to its start during mesh editing.
		Span<byte> predecessors = ends.Length <= 512 ? stackalloc byte[ends.Length] : new byte[ends.Length];
		predecessors.Clear();
		for ( int i = 0; i < ends.Length; i++ )
		{
			if ( ends[i] < 0 ) continue;
			int vertex = ends[i], opposite = opposites[i], after = next[i], face = owners[i];
			if ( (uint)vertex >= vertices.Length || vertices[vertex] < 0 || (uint)opposite >= ends.Length ||
				ends[opposite] < 0 || opposite == i || opposites[opposite] != i || (uint)after >= ends.Length ||
				ends[after] < 0 || predecessors[after] != 0 || owners[after] != face ||
				(face >= 0 && ((uint)face >= faces.Length || faces[face] < 0)) )
				throw new InvalidDataException( "Invalid half-edge connectivity." );
			predecessors[after] = 1;
		}
		foreach ( var edge in vertices )
			if ( edge >= 0 && ((uint)edge >= ends.Length || ends[edge] < 0) )
				throw new InvalidDataException( "Invalid vertex edge." );
		for ( int i = 0; i < faces.Length; i++ )
			if ( faces[i] >= 0 && ((uint)faces[i] >= ends.Length || ends[faces[i]] < 0 || owners[faces[i]] != i) )
				throw new InvalidDataException( "Invalid face edge." );

		// VMAP stores one stream per field. The scene mesh reader expects interleaved edges.
		int length = checked((3 + vertices.Length + 4 * ends.Length + faces.Length) * sizeof( int ));
		var buffer = ArrayPool<byte>.Shared.Rent( length );
		try
		{
			var layout = MemoryMarshal.Cast<byte, int>( buffer.AsSpan( 0, length ) );
			layout[0] = vertices.Length;
			layout[1] = ends.Length;
			layout[2] = faces.Length;
			vertices.CopyTo( layout[3..] );
			int offset = 3 + vertices.Length;
			for ( int i = 0; i < ends.Length; i++ )
			{
				layout[offset++] = ends[i];
				layout[offset++] = opposites[i];
				layout[offset++] = next[i];
				layout[offset++] = owners[i];
			}
			faces.CopyTo( layout[offset..] );

			using var stream = new MemoryStream( buffer, 0, length, writable: false );
			using var reader = new BinaryReader( stream );
			mesh.Deserialize( reader );
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( buffer );
		}
	}

	// A tile can receive another tint after placement. Preserve fractional source values
	// until that multiplication so the scene vertex buffer is quantized only once.
	static void SetCornerPaint( PolygonMesh mesh, HalfEdgeMesh.HalfEdgeHandle corner, N.Vector4 tint, N.Vector4 blend, Dictionary<HalfEdgeMesh.HalfEdgeHandle, TileCornerPaint> sourcePaint )
	{
		var color = PackVertexPaint( tint );
		var layers = PackVertexPaint( blend );
		mesh.SetVertexColor( corner, color );
		mesh.SetVertexBlend( corner, layers );
		if ( sourcePaint is null ) return;
		if ( tint * 255f != new N.Vector4( color.r, color.g, color.b, color.a ) || blend * 255f != new N.Vector4( layers.r, layers.g, layers.b, layers.a ) )
			sourcePaint[corner] = new( tint, blend );
		else sourcePaint.Remove( corner );
	}

	// Hammer packs each paint channel independently into its R8G8B8A8 vertex buffer.
	internal static Color32 PackVertexPaint( N.Vector4 value )
	{
		static byte Channel( float component ) => (byte)(Math.Clamp( component, 0f, 1f ) * 255f);
		return new( Channel( value.X ), Channel( value.Y ), Channel( value.Z ), Channel( value.W ) );
	}
}
