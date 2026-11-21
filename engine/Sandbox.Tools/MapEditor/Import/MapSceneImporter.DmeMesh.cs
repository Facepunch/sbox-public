using System;
using System.IO;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// DmeMesh stores polygons in material groups. A face index identifies a corner
	// whose position, UV and paint each have their own index into a data stream.
	static PolygonMesh ReadDmeMesh( Element source, string materialOverride, Dictionary<HalfEdgeMesh.HalfEdgeHandle, TileCornerPaint> sourcePaint, out bool indexedSecondaryBlend )
	{
		var bind = source.GetArray<Element>( "baseStates" ).FirstOrDefault( x => x?.Name == "bind" )
			?? throw new InvalidDataException( $"Missing bind state on DME mesh '{source.Name}'." );
		string Field( string name ) => bind.ContainsKey( name + "$0" ) ? name + "$0" : name;
		ReadOnlySpan<T> Values<T>( string field ) where T : unmanaged => bind.ContainsKey( field ) ? bind.Numbers<T>( field ) : default;
		// DME uses the first attribute value for corners with a negative stream index.
		static int CornerIndex( ReadOnlySpan<int> indices, int corner ) => indices.IsEmpty ? corner : Math.Max( 0, indices[corner] );
		var position = Field( "position" );
		var uv = Field( "texcoord" );
		var normal = Field( "normal" );
		var tint = Field( "VertexPaintTintColor" );
		var blend = Field( "VertexPaintBlendParams" );
		var positions = bind.Numbers<N.Vector3>( position );
		var positionIds = Values<int>( position + "Indices" );
		var normals = Values<N.Vector3>( normal );
		var normalIds = Values<int>( normal + "Indices" );
		var uvs = Values<N.Vector2>( uv );
		var uvIds = Values<int>( uv + "Indices" );
		var tints = Values<N.Vector4>( tint );
		var tintIds = Values<int>( tint + "Indices" );
		var blends = Values<N.Vector4>( blend );
		var blendIds = Values<int>( blend + "Indices" );
		bool flip = bind.Get( "flipVCoordinates", false );
		var second = bind.ContainsKey( "VertexPaintBlendParams$1" ) ? "VertexPaintBlendParams$1" : Field( "VertexPaintBlendParams1" );
		var secondary = Values<N.Vector4>( second );
		indexedSecondaryBlend = second == "VertexPaintBlendParams$1" && !secondary.IsEmpty;
		var secondaryIds = Values<int>( second + "Indices" );

		var mesh = new PolygonMesh();
		var vertices = new HalfEdgeMesh.VertexHandle[positions.Length];
		foreach ( var set in source.GetArray<Element>( "faceSets" ) )
		{
			if ( set?.GetValueOrDefault( "material" ) is not Element material ) continue;
			var faceMaterial = materialOverride is null ? ReadDmeMaterial( material ) : Material.Load( materialOverride );
			var indices = set.Numbers<int>( "faces" );
			while ( !indices.IsEmpty )
			{
				int end = indices.IndexOf( -1 );
				var corners = end < 0 ? indices : indices[..end];
				indices = end < 0 ? default : indices[(end + 1)..];
				if ( corners.Length < 3 ) continue;
				var polygon = new HalfEdgeMesh.VertexHandle[corners.Length];
				for ( int i = 0; i < corners.Length; i++ )
				{
					int id = positionIds.IsEmpty ? corners[i] : positionIds[corners[i]];
					if ( !vertices[id].IsValid ) vertices[id] = mesh.AddVertex( positions[id] );
					polygon[i] = vertices[id];
				}
				var face = mesh.AddFace( polygon );
				if ( !face.IsValid ) continue;
				mesh.SetFaceMaterial( face, faceMaterial );
				var edge = face.Edge;
				foreach ( int index in corners )
				{
					if ( !normals.IsEmpty )
					{
						var direction = ((Vector3)normals[CornerIndex( normalIds, index )]).Normal;
						if ( direction.LengthSquared > 0 ) mesh.SetCornerNormal( edge, direction );
					}
					if ( !uvs.IsEmpty )
					{
						var coord = uvs[CornerIndex( uvIds, index )];
						mesh.SetTextureCoord( edge, new( coord.X, flip ? 1f - coord.Y : coord.Y ) );
					}
					SetCornerPaint( mesh, edge, tints.IsEmpty ? N.Vector4.One : tints[CornerIndex( tintIds, index )],
						blends.IsEmpty ? default : blends[CornerIndex( blendIds, index )], sourcePaint );
					if ( !secondary.IsEmpty ) mesh.SetSecondaryVertexBlend( edge, PackVertexPaint( secondary[CornerIndex( secondaryIds, index )] ) );
					edge = edge.NextEdge;
				}
			}
		}
		return mesh;
	}

	static Material ReadDmeMaterial( Element material )
	{
		var name = material.Get( "mtlName", "" );
		if ( string.IsNullOrEmpty( name ) || Path.IsPathRooted( name ) ) return null;
		var extension = Path.GetExtension( name );
		if ( extension.Length == 0 ) return Material.Load( name + ".vmat" );
		return extension.Equals( ".vmat", StringComparison.OrdinalIgnoreCase ) ? Material.Load( name ) : null;
	}
}
