using System;
using System.Runtime.InteropServices;

namespace SeriousEngine3;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct SkinnedVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Vector2 texcoord2, Color32 color, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.Tangent]
	public Vector4 tangent = tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.TexCoord( 1 )]
	public Vector2 texcoord2 = texcoord2;

	[VertexLayout.Color]
	public Color32 color = color;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights = blendWeights;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct StaticVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Vector2 texcoord2, Color32 color )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.Tangent]
	public Vector4 tangent = tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.TexCoord( 1 )]
	public Vector2 texcoord2 = texcoord2;

	[VertexLayout.Color]
	public Color32 color = color;
}

public partial class ModelLoader
{
	public readonly struct SE3Transform
	{
		public readonly Rotation Rotation;
		public readonly Vector3 Translation;
		public readonly Vector3 Scale;
		public readonly bool IsIdentity;

		SE3Transform( Rotation rotation, Vector3 translation, Vector3 scale, bool identity )
		{
			Rotation = rotation;
			Translation = translation;
			Scale = scale;
			IsIdentity = identity;
		}

		public SE3Transform( Rotation rotation, Vector3 translation, Vector3 scale )
			: this( rotation, translation, scale, false ) { }

		public static readonly SE3Transform Identity = new( Rotation.Identity, Vector3.Zero, Vector3.One, true );

		public Vector3 Apply( Vector3 p )
			=> Rotation * new Vector3( p.x * Scale.x, p.y * Scale.y, p.z * Scale.z ) + Translation;

		public Vector3 Rotate( Vector3 v ) => Rotation * v;

		public SE3Transform Combine( SE3Transform child )
		{
			if ( IsIdentity ) return child;
			if ( child.IsIdentity ) return this;

			var scale = new Vector3( Scale.x * child.Scale.x, Scale.y * child.Scale.y, Scale.z * child.Scale.z );
			return new SE3Transform( Rotation * child.Rotation, Apply( child.Translation ), scale );
		}
	}

	static List<int> ReadSurfaceIndices( byte[] bytes, ref int cursor, int indexCount )
	{
		var indices = new List<int>( indexCount );
		for ( int i = 0; i < indexCount; i++ )
		{
			if ( cursor + 2 > bytes.Length ) break;
			ushort local = BitConverter.ToUInt16( bytes, cursor );
			cursor += 2;
			indices.Add( local );
		}
		return indices;
	}

	static List<SkinnedVertex> ReadSkinnedSurfaceVertices( MetaValue surface, byte[] bytes, int vertexCount, Dictionary<string, byte> boneIndexByName, string colorUvChannel, UvMapping mapping, UvMapping normalMapping, SE3Transform transform )
	{
		var vertices = new List<SkinnedVertex>( vertexCount );
		var palette = surface["18"]; // CStaticStackArray<IDENT> (bone palette)

		// Pre-build a lookup table from local palette index to skeleton bone index
		var boneRemap = new byte[palette.Count];
		for ( int i = 0; i < palette.Count; i++ )
		{
			var boneName = palette[i].AsIdentName();
			boneRemap[i] = !string.IsNullOrEmpty( boneName ) && boneIndexByName.TryGetValue( boneName, out var skelIndex )
				? skelIndex
				: (byte)0;
		}

		var paths = ReadAttributePaths( surface );

		int posOffset = (int)paths[1].byteOffset;
		int nrmOffset = (int)paths[2].byteOffset;
		int tanOffset = (int)paths[3].byteOffset;
		bool hasTangents = paths[3].semanticId != 0;
		int uvOffset = ResolveUvOffset( surface, paths, colorUvChannel );
		int boneOffset = (int)paths[5].byteOffset;
		int weightOffset = (int)paths[4].byteOffset;
		bool hasBones = paths[5].semanticId != 0;

		for ( int v = 0; v < vertexCount; v++ )
		{
			var position = ReadVec3( bytes, posOffset + (v * 12) );
			var norm = ReadVec3( bytes, nrmOffset + (v * 12) );

			var tangent = new Vector4( 0, 0, 0, 1 );
			if ( hasTangents )
			{
				int tanBase = tanOffset + (v * 16);
				var rawTan = ReadVec3( bytes, tanBase );
				if ( !transform.IsIdentity ) rawTan = transform.Rotate( rawTan );
				float sign = ReadFloat( bytes, tanBase + 12 );
				var swz = new Vector3( -rawTan.z, -rawTan.x, rawTan.y );
				tangent = new Vector4( -swz.x, -swz.y, -swz.z, sign < 0f ? -1f : 1f );
			}

			var rawUv = uvOffset >= 0 ? ReadVec2( bytes, uvOffset + (v * 8) ) : Vector2.Zero;
			var uv = mapping.Apply( rawUv );
			var uv2 = normalMapping.Apply( rawUv );

			var fixedIndices = new Color32( 0, 0, 0, 0 );
			var rawWeights = new Color32( 255, 0, 0, 0 );

			if ( hasBones )
			{
				var raw = ReadColor32( bytes, boneOffset + (v * 4) );
				fixedIndices = new Color32(
					RemapBone( boneRemap, raw.r ),
					RemapBone( boneRemap, raw.g ),
					RemapBone( boneRemap, raw.b ),
					RemapBone( boneRemap, raw.a )
				);

				rawWeights = ReadColor32( bytes, weightOffset + (v * 4) );
				if ( rawWeights.r + rawWeights.g + rawWeights.b + rawWeights.a == 0 )
					rawWeights = new Color32( 255, 0, 0, 0 );
			}

			if ( !transform.IsIdentity )
			{
				position = transform.Apply( position );
				norm = transform.Rotate( norm );
			}

			position = new Vector3( -position.z, -position.x, position.y );
			norm = new Vector3( -norm.z, -norm.x, norm.y );

			vertices.Add( new SkinnedVertex(
				position * Scale,
				norm,
				tangent,
				uv,
				uv2,
				Color.White,
				fixedIndices,
				rawWeights
			) );
		}

		return vertices;
	}

	static List<StaticVertex> ReadStaticSurfaceVertices( MetaValue surface, byte[] bytes, int vertexCount, string colorUvChannel, UvMapping mapping, UvMapping normalMapping, SE3Transform transform )
	{
		var vertices = new List<StaticVertex>( vertexCount );
		var paths = ReadAttributePaths( surface );

		int posOffset = (int)paths[1].byteOffset;
		int nrmOffset = (int)paths[2].byteOffset;
		int tanOffset = (int)paths[3].byteOffset;
		bool hasTangents = paths[3].semanticId != 0;
		int uvOffset = ResolveUvOffset( surface, paths, colorUvChannel );

		for ( int v = 0; v < vertexCount; v++ )
		{
			var position = ReadVec3( bytes, posOffset + (v * 12) );
			var norm = ReadVec3( bytes, nrmOffset + (v * 12) );

			var tangent = new Vector4( 0, 0, 0, 1 );
			if ( hasTangents )
			{
				int tanBase = tanOffset + (v * 16);
				var rawTan = ReadVec3( bytes, tanBase );
				if ( !transform.IsIdentity ) rawTan = transform.Rotate( rawTan );
				float sign = ReadFloat( bytes, tanBase + 12 );
				var swz = new Vector3( -rawTan.z, -rawTan.x, rawTan.y );
				tangent = new Vector4( -swz.x, -swz.y, -swz.z, sign < 0f ? -1f : 1f );
			}

			var rawUv = uvOffset >= 0 ? ReadVec2( bytes, uvOffset + (v * 8) ) : Vector2.Zero;
			var uv = mapping.Apply( rawUv );
			var uv2 = normalMapping.Apply( rawUv );

			if ( !transform.IsIdentity )
			{
				position = transform.Apply( position );
				norm = transform.Rotate( norm );
			}

			position = new Vector3( -position.z, -position.x, position.y );
			norm = new Vector3( -norm.z, -norm.x, norm.y );

			vertices.Add( new StaticVertex(
				position * Scale,
				norm,
				tangent,
				uv,
				uv2,
				Color.White
			) );
		}

		return vertices;
	}

	static readonly string[] BufferIndexKeys = { "7", "6", "8", "9", "10", "11", "24" };

	static int ResolveUvOffset( MetaValue surface, List<(byte semanticId, byte bufferIndex, uint byteOffset)> paths, string channel )
	{
		int channelIndex = 0;

		if ( !string.IsNullOrEmpty( channel ) )
		{
			var names = surface["12"]; // CStaticArray<IDENT> (UV channel names)
			for ( int i = 0; i < names.Count; i++ )
			{
				if ( names[i].AsIdentName() == channel )
				{
					channelIndex = i;
					break;
				}
			}
		}

		int pathIndex = 7 + channelIndex;
		if ( pathIndex < paths.Count && paths[pathIndex].bufferIndex != 255 )
			return (int)paths[pathIndex].byteOffset;
		if ( paths[7].bufferIndex != 255 )
			return (int)paths[7].byteOffset;
		return -1;
	}

	static List<(byte semanticId, byte bufferIndex, uint byteOffset)> ReadAttributePaths( MetaValue surface )
	{
		var paths = new List<(byte semanticId, byte bufferIndex, uint byteOffset)>();

		void AddPath( MetaValue path )
		{
			byte semanticId = path["1"].AsByte();
			byte bufferIndex = path["2"].AsByte();
			uint byteOffset = path["3"].AsUInt();
			paths.Add( (semanticId, bufferIndex, byteOffset) );
		}

		AddPath( surface["6"] );
		AddPath( surface["7"] );
		AddPath( surface["8"] );
		AddPath( surface["9"] );
		AddPath( surface["10"] );
		AddPath( surface["11"] );
		AddPath( surface["24"] );

		foreach ( var p in surface["14"].Elements() ) AddPath( p );
		foreach ( var p in surface["15"].Elements() ) AddPath( p );

		return paths;
	}

	static Vector3 ReadVec3( byte[] data, int start )
	{
		if ( start < 0 || start + 12 > data.Length )
			return Vector3.Zero;
		return new( BitConverter.ToSingle( data, start ),
			 BitConverter.ToSingle( data, start + 4 ),
			 BitConverter.ToSingle( data, start + 8 ) );
	}

	static Vector2 ReadVec2( byte[] data, int start )
	{
		if ( start < 0 || start + 8 > data.Length )
			return Vector2.Zero;
		return new( BitConverter.ToSingle( data, start ),
			 BitConverter.ToSingle( data, start + 4 ) );
	}

	static float ReadFloat( byte[] data, int start )
		=> start >= 0 && start + 4 <= data.Length ? BitConverter.ToSingle( data, start ) : 0f;

	static Color32 ReadColor32( byte[] data, int start )
	{
		if ( start < 0 || start + 4 > data.Length )
			return new Color32( 0, 0, 0, 0 );
		return new( data[start], data[start + 1], data[start + 2], data[start + 3] );
	}

	static byte RemapBone( byte[] map, byte index )
	{
		return index < map.Length ? map[index] : (byte)0;
	}
}
