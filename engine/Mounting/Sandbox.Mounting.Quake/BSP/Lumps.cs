using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Quake.BSP;

internal enum LumpType : int
{
	Entities,
	Planes,
	Textures,
	Vertices,
	Visibility,
	Nodes,
	TexInfo,
	Faces,
	Lighting,
	ClipNodes,
	Leafs,
	MarkSurfaces,
	Edges,
	SurfEdges,
	Models,

	Count,
}

[Flags]
internal enum SurfaceFlags
{
	PlaneBack = 2,
	DrawSky = 4,
	DrawSprite = 8,
	DrawTurb = 0x10,
	DrawTiled = 0x20,
	DrawBackground = 0x40,
	Underwater = 0x80,
	NoTexture = 0x100,
	DrawFence = 0x200,
	DrawLava = 0x400,
	DrawSlime = 0x800,
	DrawTele = 0x1000,
	DrawWater = 0x2000
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Plane
{
	public readonly Vector3 Normal;
	public readonly float Distance;
	public readonly int Type;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct MipTexture
{
	public readonly byte[] Name;

	public readonly uint Width;
	public readonly uint Height;

	public readonly uint[] Offsets;

	public MipTexture( byte[] name, uint width, uint height, uint[] offsets )
	{
		Name = name;
		Width = width;
		Height = height;
		Offsets = offsets;
	}
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct TexInfo
{
	public readonly Vector3 S;
	public readonly float OffsetS;
	public readonly Vector3 T;
	public readonly float OffsetT;
	public readonly uint MipTex;
	public readonly uint Flags;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Face
{
	public readonly uint Plane;
	public readonly uint PlaneSide;
	public readonly uint FirstEdge;
	public readonly uint EdgeCount;
	public readonly uint TextureInfo;

	public readonly byte Style0;
	public readonly byte Style1;
	public readonly byte Style2;
	public readonly byte Style3;

	public readonly int LightmapOffset;

	public Face( uint plane, uint planeSide, uint firstEdge, uint edgeCount, uint textureInfo, byte style0, byte style1, byte style2, byte style3, int lightmapOffset )
	{
		Plane = plane;
		PlaneSide = planeSide;
		FirstEdge = firstEdge;
		EdgeCount = edgeCount;
		TextureInfo = textureInfo;
		Style0 = style0;
		Style1 = style1;
		Style2 = style2;
		Style3 = style3;
		LightmapOffset = lightmapOffset;
	}
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Edge
{
	public readonly uint Vertex0;
	public readonly uint Vertex1;

	public Edge( uint vertex0, uint vertex1 )
	{
		Vertex0 = vertex0;
		Vertex1 = vertex1;
	}
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Model
{
	public readonly Vector3 Mins;
	public readonly Vector3 Maxs;
	public readonly Vector3 Origin;

	public readonly uint HeadNode0;
	public readonly uint HeadNode1;
	public readonly uint HeadNode2;
	public readonly uint HeadNode3;

	public readonly int VisLeafCount;
	public readonly int FirstFace;
	public readonly int FaceCount;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Lump
{
	public readonly int Offset;
	public readonly int Length;

	public Lump( int offset, int length )
	{
		Offset = offset;
		Length = length;
	}

	public static Lump ReadFromStream( BinaryReader reader )
	{
		int offset = reader.ReadInt32();
		int length = reader.ReadInt32();
		return new Lump( offset, length );
	}
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal readonly struct Header
{
	public readonly int Version;

	public readonly Lump Entities;
	public readonly Lump Planes;
	public readonly Lump Textures;
	public readonly Lump Vertices;
	public readonly Lump Visibility;
	public readonly Lump Nodes;
	public readonly Lump TexInfo;
	public readonly Lump Faces;
	public readonly Lump Lighting;
	public readonly Lump ClipNodes;
	public readonly Lump Leafs;
	public readonly Lump MarkSurfaces;
	public readonly Lump Edges;
	public readonly Lump SurfEdges;
	public readonly Lump Models;

	public Header(
		int version,
		Lump entities,
		Lump planes,
		Lump textures,
		Lump vertices,
		Lump visibility,
		Lump nodes,
		Lump texInfo,
		Lump faces,
		Lump lighting,
		Lump clipNodes,
		Lump leafs,
		Lump markSurfaces,
		Lump edges,
		Lump surfEdges,
		Lump models )
	{
		Version = version;
		Entities = entities;
		Planes = planes;
		Textures = textures;
		Vertices = vertices;
		Visibility = visibility;
		Nodes = nodes;
		TexInfo = texInfo;
		Faces = faces;
		Lighting = lighting;
		ClipNodes = clipNodes;
		Leafs = leafs;
		MarkSurfaces = markSurfaces;
		Edges = edges;
		SurfEdges = surfEdges;
		Models = models;
	}

	public static Header ReadFromStream( BinaryReader reader )
	{
		int version = reader.ReadInt32();

		var entities = Lump.ReadFromStream( reader );
		var planes = Lump.ReadFromStream( reader );
		var textures = Lump.ReadFromStream( reader );
		var vertices = Lump.ReadFromStream( reader );
		var visibility = Lump.ReadFromStream( reader );
		var nodes = Lump.ReadFromStream( reader );
		var texInfo = Lump.ReadFromStream( reader );
		var faces = Lump.ReadFromStream( reader );
		var lighting = Lump.ReadFromStream( reader );
		var clipNodes = Lump.ReadFromStream( reader );
		var leafs = Lump.ReadFromStream( reader );
		var markSurfaces = Lump.ReadFromStream( reader );
		var edges = Lump.ReadFromStream( reader );
		var surfEdges = Lump.ReadFromStream( reader );
		var models = Lump.ReadFromStream( reader );

		return new Header(
			version,
			entities,
			planes,
			textures,
			vertices,
			visibility,
			nodes,
			texInfo,
			faces,
			lighting,
			clipNodes,
			leafs,
			markSurfaces,
			edges,
			surfEdges,
			models );
	}
}

internal struct TextureData
{
	public byte[] Data;
	public int Scale;
}

internal class File
{
	public Header Header { get; private set; }
	public Plane[] Planes { get; private set; }
	public MipTexture[] Textures { get; private set; }
	public Vector3[] Vertices { get; private set; }
	public TexInfo[] TexInfos { get; private set; }
	public Face[] Faces { get; private set; }
	public ushort[] MarkSurfaces { get; private set; }
	public Edge[] Edges { get; private set; }
	public int[] SurfEdges { get; private set; }
	public Model[] Models { get; private set; }
	public TextureData[] TextureData { get; private set; }
	public byte[] LightmapData { get; private set; }
	public List<ObjectEntry> Entities { get; private set; } = [];

	public static readonly int[] StructSizes =
	[
		0,
		20,
		40,
		12,
		0,
		32,
		40,
		20,
		0,
		12,
		28,
		4,
		4,
		4,
		64,
	];

	public File( Stream stream )
	{
		stream.Seek( 0, SeekOrigin.Begin );
		using var reader = new BinaryReader( stream );

		Header = Header.ReadFromStream( reader );

		bool bsp2;
		switch ( Header.Version )
		{
			case 29:
				bsp2 = false;
				break;
			case Bsp2Magic:
			case Bsp2psbMagic:
				bsp2 = true;
				break;
			default:
				throw new ArgumentException( "Not a Quake 1 BSP file" );
		}

		Planes = ReadLump<Plane>( stream, Header.Planes, LumpType.Planes );
		Vertices = ReadLump<Vector3>( stream, Header.Vertices, LumpType.Vertices );
		TexInfos = ReadLump<TexInfo>( stream, Header.TexInfo, LumpType.TexInfo );
		Faces = ReadFaces( stream, bsp2 );
		MarkSurfaces = ReadLump<ushort>( stream, Header.MarkSurfaces, LumpType.MarkSurfaces );
		Edges = ReadEdges( stream, bsp2 );
		SurfEdges = ReadLump<int>( stream, Header.SurfEdges, LumpType.SurfEdges );
		Models = ReadLump<Model>( stream, Header.Models, LumpType.Models );

		LoadTextures( reader );
		LoadLightmaps( stream );

		var entityData = new byte[Header.Entities.Length];
		stream.Seek( Header.Entities.Offset, SeekOrigin.Begin );
		stream.ReadExactly( entityData );
		ParseEntities( Encoding.UTF8.GetString( entityData ) );
	}

	private void LoadLightmaps( Stream stream )
	{
		var lumpInfo = Header.Lighting;
		LightmapData = new byte[lumpInfo.Length];
		stream.Seek( lumpInfo.Offset, SeekOrigin.Begin );
		stream.ReadExactly( LightmapData, 0, lumpInfo.Length );
	}

	private void LoadTextures( BinaryReader reader )
	{
		var lumpInfo = Header.Textures;

		reader.BaseStream.Seek( lumpInfo.Offset, SeekOrigin.Begin );
		var textureCount = reader.ReadInt32();

		byte[] bytes = reader.ReadBytes( textureCount * sizeof( int ) );
		var offsets = new int[textureCount];
		Buffer.BlockCopy( bytes, 0, offsets, 0, bytes.Length );

		Textures = new MipTexture[textureCount];
		TextureData = new TextureData[textureCount];
		for ( int i = 0; i < textureCount; i++ )
		{
			if ( offsets[i] < 0 )
			{
				Textures[i] = new MipTexture( Encoding.ASCII.GetBytes( $"__missing_{i}" ), 1, 1, new uint[4] );
				TextureData[i] = new TextureData { Scale = 1, Data = new byte[1] };
				continue;
			}

			reader.BaseStream.Seek( lumpInfo.Offset + offsets[i], SeekOrigin.Begin );
			var texture = ReadMipTextureFromStream( reader );
			Textures[i] = texture;

			var totalBytes = texture.Width * texture.Height;
			var data = new byte[totalBytes];

			int mip = 0;
			int scale = 1;
			reader.BaseStream.Seek( lumpInfo.Offset + offsets[i] + (int)texture.Offsets[mip], SeekOrigin.Begin );
			var mipWidth = texture.Width >> mip;
			var mipHeight = texture.Height >> mip;
			var mipSize = mipWidth * mipHeight;
			reader.BaseStream.ReadExactly( data, 0, (int)mipSize );

			TextureData[i] = new TextureData { Scale = scale, Data = ResizeNearestNeighbor( data, (int)texture.Width, (int)texture.Height, (int)texture.Width * scale, (int)texture.Height * scale ) };
		}
	}

	public static byte[] ResizeNearestNeighbor( byte[] source, int srcWidth, int srcHeight, int tgtWidth, int tgtHeight )
	{
		byte[] target = new byte[tgtWidth * tgtHeight];

		float xRatio = srcWidth / (float)tgtWidth;
		float yRatio = srcHeight / (float)tgtHeight;

		for ( int y = 0; y < tgtHeight; y++ )
		{
			for ( int x = 0; x < tgtWidth; x++ )
			{
				int nearestX = (int)(x * xRatio);
				int nearestY = (int)(y * yRatio);

				target[(y * tgtWidth) + x] = source[(nearestY * srcWidth) + nearestX];
			}
		}

		return target;
	}

	public static MipTexture ReadMipTextureFromStream( BinaryReader reader )
	{
		var name = reader.ReadBytes( 16 );
		var width = reader.ReadUInt32();
		var height = reader.ReadUInt32();

		var offsets = new uint[4];
		offsets[0] = reader.ReadUInt32();
		offsets[1] = reader.ReadUInt32();
		offsets[2] = reader.ReadUInt32();
		offsets[3] = reader.ReadUInt32();

		return new MipTexture( name, width, height, offsets );
	}

	[ThreadStatic]
	private static StringBuilder _sStringBuilder;

	public static string GetString( byte[] data )
	{
		if ( _sStringBuilder == null ) _sStringBuilder = new StringBuilder( 128 );
		else _sStringBuilder.Remove( 0, _sStringBuilder.Length );

		for ( var i = 0; i < data.Length; ++i )
		{
			var c = (char)data[i];
			if ( c == '\0' ) break;

			_sStringBuilder.Append( c );
		}

		return _sStringBuilder.ToString();
	}

	private const int Bsp2Magic = 0x32505342;   // "BSP2"
	private const int Bsp2psbMagic = 0x42535032; // "2PSB"

	private Edge[] ReadEdges( Stream stream, bool bsp2 )
	{
		var lump = Header.Edges;
		var stride = bsp2 ? 8 : 4;
		var count = lump.Length / stride;
		var edges = new Edge[count];

		var buffer = new byte[lump.Length];
		stream.Seek( lump.Offset, SeekOrigin.Begin );
		stream.ReadExactly( buffer );
		using var bs = ByteStream.CreateReader( buffer );

		for ( var i = 0; i < count; ++i )
		{
			edges[i] = bsp2
				? new Edge( bs.Read<uint>(), bs.Read<uint>() )
				: new Edge( bs.Read<ushort>(), bs.Read<ushort>() );
		}

		return edges;
	}

	private Face[] ReadFaces( Stream stream, bool bsp2 )
	{
		var lump = Header.Faces;
		var stride = bsp2 ? 28 : 20;
		var count = lump.Length / stride;
		var faces = new Face[count];

		var buffer = new byte[lump.Length];
		stream.Seek( lump.Offset, SeekOrigin.Begin );
		stream.ReadExactly( buffer );
		using var bs = ByteStream.CreateReader( buffer );

		for ( var i = 0; i < count; ++i )
		{
			uint plane, side, firstEdge, edgeCount, texInfo;

			if ( bsp2 )
			{
				plane = bs.Read<uint>();
				side = bs.Read<uint>();
				firstEdge = bs.Read<uint>();
				edgeCount = bs.Read<uint>();
				texInfo = bs.Read<uint>();
			}
			else
			{
				plane = bs.Read<ushort>();
				side = bs.Read<ushort>();
				firstEdge = bs.Read<uint>();
				edgeCount = bs.Read<ushort>();
				texInfo = bs.Read<ushort>();
			}

			var style0 = bs.Read<byte>();
			var style1 = bs.Read<byte>();
			var style2 = bs.Read<byte>();
			var style3 = bs.Read<byte>();
			var lightmapOffset = bs.Read<int>();

			faces[i] = new Face( plane, side, firstEdge, edgeCount, texInfo, style0, style1, style2, style3, lightmapOffset );
		}

		return faces;
	}

	private static T[] ReadLump<T>( Stream stream, Lump lumpInfo, LumpType lumpType ) where T : unmanaged
	{
		var size = StructSizes[(int)lumpType];
		var count = lumpInfo.Length / size;
		var items = new T[count];

		var buffer = new byte[lumpInfo.Length];
		stream.Seek( lumpInfo.Offset, SeekOrigin.Begin );
		stream.ReadExactly( buffer );
		using var bs = ByteStream.CreateReader( buffer );

		for ( var i = 0; i < count; ++i )
		{
			items[i] = bs.Read<T>();
		}

		return items;
	}

	private void ParseEntities( string content )
	{
		Dictionary<string, string> currentEntity = null;
		var lines = content.Split( ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries );

		foreach ( var line in lines )
		{
			var trimmedLine = line.Trim();

			if ( trimmedLine.StartsWith( "{" ) )
			{
				currentEntity = [];
			}
			else if ( trimmedLine.StartsWith( "}" ) )
			{
				if ( currentEntity != null )
				{
					Entities.Add( new ObjectEntry( currentEntity ) );
					currentEntity = null;
				}
			}
			else if ( currentEntity != null )
			{
				var separatorIndex = trimmedLine.IndexOf( '"', 1 );
				if ( separatorIndex > 0 )
				{
					var key = trimmedLine[1..separatorIndex].Trim();
					var value = trimmedLine[(separatorIndex + 1)..].Trim().Trim( '"' );
					currentEntity[key] = value;
				}
			}
		}
	}

	internal readonly struct ObjectEntry
	{
		public string TypeName { get; init; }
		public string TargetName { get; init; }
		public string ParentName { get; init; }
		public Vector3 Position { get; init; }
		public float Angle { get; init; }
		public Rotation Rotation { get; init; }
		public Vector3 Scales { get; init; }
		public Transform Transform { get; init; }

		private readonly Dictionary<string, string> KeyValues;

		internal ObjectEntry( Dictionary<string, string> keyValues )
		{
			KeyValues = keyValues;
			TypeName = GetValueString( "classname", null );
			TargetName = GetValueString( "targetname", null );
			ParentName = GetValueString( "parentname", null );
			Position = GetValue<Vector3>( "origin" );
			Angle = GetValue<float>( "angle" );
			Scales = GetValue( "scales", Vector3.One );
			Rotation = Rotation.FromYaw( Angle );
			Transform = new( Position, Rotation, Scales );
		}

		private string GetValueString( string key, string defaultValue )
		{
			if ( KeyValues.TryGetValue( key, out var value ) )
			{
				return value;
			}

			return defaultValue;
		}

		public readonly T GetValue<T>( string key, T defaultValue = default )
		{
			var value = GetValueString( key, null );
			if ( string.IsNullOrWhiteSpace( value ) )
				return defaultValue;

			return (T)value.ToType( typeof( T ) );
		}

		public readonly string GetString( string key, string defaultValue = default )
		{
			return GetValueString( key, defaultValue );
		}
	}
}

