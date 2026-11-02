using System;
using System.IO;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	public Vector3[] Vertices;
	public BspPlane[] Planes;
	public BspEdge[] Edges;
	public int[] SurfEdges;
	public BspFace[] Faces;
	public BspTexInfo[] TexInfos;
	public BspTexData[] TexDatas;
	public BspModel[] Models;
	public byte[] LightingData;
	public bool Hdr;
	public bool LightstylesWithCsm;
	public uint MapFlags;
	public int[] TexDataStringTable;
	public byte[] TexDataStringData;
	public BspDispInfo[] DispInfos;
	public BspDispVert[] DispVerts;
	public ushort[] DispTriTags;
	public byte[] PhysCollideData;
	public Vector3[] VertNormals;
	public ushort[] VertNormalIndices;
	public int[] FaceVertexNormalOffsets;
	public BspFile( Stream stream )
	{
		var header = new BspHeader( stream );
		var fileStart = header.Start;
		var lumps = header.Lumps;

		Vertices = ReadLump<Vector3>( stream, fileStart, lumps[3] );
		Planes = ReadLump<BspPlane>( stream, fileStart, lumps[1] );
		Edges = ReadLump<BspEdge>( stream, fileStart, lumps[12] );
		SurfEdges = ReadLump<int>( stream, fileStart, lumps[13] );
		TexInfos = ReadLump<BspTexInfo>( stream, fileStart, lumps[6] );
		TexDatas = ReadLump<BspTexData>( stream, fileStart, lumps[2] );
		Models = ReadLump<BspModel>( stream, fileStart, lumps[14] );
		TexDataStringTable = ReadLump<int>( stream, fileStart, lumps[44] );
		bool useHdr = lumps[53].length > 0;
		Hdr = useHdr;
		var faceLump = useHdr && lumps[58].length > 0 ? lumps[58] : lumps[7];
		Faces = ReadLump<BspFace>( stream, fileStart, faceLump, 1 );
		var lightLump = useHdr ? lumps[53] : lumps[8];
		LightingData = ReadRaw( stream, fileStart, lightLump, 1 );
		var mapFlags = ReadRaw( stream, fileStart, lumps[59] );
		if ( mapFlags.Length != 0 && mapFlags.Length != 4 )
			throw new InvalidDataException( "Invalid BSP map flags lump." );
		MapFlags = mapFlags.Length == 4 ? BitConverter.ToUInt32( mapFlags ) : 0;
		LightstylesWithCsm = (MapFlags & 0x40) != 0;
		TexDataStringData = ReadRaw( stream, fileStart, lumps[43] );
		DispInfos = ReadDispInfos( stream, fileStart, lumps[26] );
		DispVerts = ReadLump<BspDispVert>( stream, fileStart, lumps[33] );
		DispTriTags = ReadLump<ushort>( stream, fileStart, lumps[48] );
		PhysCollideData = ReadRaw( stream, fileStart, lumps[29] );
		VertNormals = ReadLump<Vector3>( stream, fileStart, lumps[30] );
		VertNormalIndices = ReadLump<ushort>( stream, fileStart, lumps[31] );
		ValidateGeometry();
		Entities = ReadEntities( ReadRaw( stream, fileStart, lumps[0] ) );
		ReadSkyboxAreas( stream, fileStart, lumps );
		ReadLeafAmbient( stream, fileStart, lumps );
		StaticProps = ReadStaticProps( stream, fileStart, lumps[35] );
		ReadCollisionBrushes( stream, fileStart, lumps );
		ReadPropLighting( stream, fileStart, lumps );
	}

	public string GetTexDataString( int texDataIndex )
	{
		ValidateRange( texDataIndex, 1, TexDatas.Length, "Texture data index" );
		int tableIdx = TexDatas[texDataIndex].NameStringTableID;
		ValidateRange( tableIdx, 1, TexDataStringTable.Length, "Texture string table index" );
		int offset = TexDataStringTable[tableIdx];
		ValidateRange( offset, 1, TexDataStringData.Length, "Texture string offset" );
		int end = Array.IndexOf( TexDataStringData, (byte)0, offset );
		if ( end < 0 )
			throw new InvalidDataException( "Unterminated BSP texture name." );
		return System.Text.Encoding.ASCII.GetString( TexDataStringData, offset, end - offset );
	}

	internal static void ValidateRange( long offset, long length, long total, string name )
	{
		if ( offset < 0 || length < 0 || offset > total || length > total - offset )
			throw new InvalidDataException( $"{name} is outside its data range." );
	}

	internal static void ValidateLump( (int offset, int length, int version, int uncompressedSize) lump, int? version )
	{
		if ( lump.uncompressedSize < 0 || (lump.length == 0 && lump.uncompressedSize != 0) )
			throw new InvalidDataException( "Invalid BSP lump uncompressed size." );
		if ( lump.length == 0 )
			return;
		if ( version is { } maximum && (lump.version < 0 || lump.version > maximum) )
			throw new NotSupportedException( $"Unsupported BSP lump version {lump.version}; expected 0-{maximum}." );
	}

	internal static T[] ReadLump<T>( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize) l, int version = 0 )
		where T : unmanaged
	{
		ValidateLump( l, version );
		if ( l.length == 0 )
			return Array.Empty<T>();
		int size = Marshal.SizeOf<T>();
		int length = l.uncompressedSize != 0 ? l.uncompressedSize : l.length;
		if ( length % size != 0 )
			throw new InvalidDataException( $"BSP {typeof( T ).Name} lump contains a partial record." );
		if ( l.uncompressedSize != 0 )
			return MemoryMarshal.Cast<byte, T>( ReadRaw( stream, fileStart, l, version ) ).ToArray();
		var result = new T[length / size];
		stream.Position = fileStart + l.offset;
		stream.ReadExactly( MemoryMarshal.AsBytes( result.AsSpan() ) );
		return result;
	}

	internal static byte[] ReadRaw( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize) l, int? version = 0 )
	{
		ValidateLump( l, version );
		if ( l.length == 0 )
			return Array.Empty<byte>();
		var result = new byte[l.length];
		stream.Position = fileStart + l.offset;
		stream.ReadExactly( result );
		return l.uncompressedSize != 0 ? DecodeLump( result, l.uncompressedSize ) : result;
	}

	internal static BspDispInfo[] ReadDispInfos( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize) l )
	{
		ValidateLump( l, 0 );
		if ( l.length == 0 )
			return Array.Empty<BspDispInfo>();
		const int STRUCT_SIZE = 176;
		int length = l.uncompressedSize != 0 ? l.uncompressedSize : l.length;
		if ( length % STRUCT_SIZE != 0 )
			throw new InvalidDataException( "BSP displacement info lump contains a partial record." );
		var data = ReadRaw( stream, fileStart, l );
		int count = data.Length / STRUCT_SIZE;
		var result = new BspDispInfo[count];
		for ( int i = 0; i < count; i++ )
		{
			int off = i * STRUCT_SIZE;
			result[i].StartPosition = new Vector3( BitConverter.ToSingle( data, off ), BitConverter.ToSingle( data, off + 4 ), BitConverter.ToSingle( data, off + 8 ) );
			result[i].DispVertStart = BitConverter.ToInt32( data, off + 12 );
			result[i].DispTriStart = BitConverter.ToInt32( data, off + 16 );
			result[i].Power = BitConverter.ToInt32( data, off + 20 );
			int minTess = BitConverter.ToInt32( data, off + 24 );
			result[i].Flags = minTess < 0 ? minTess & int.MaxValue : 0;
			result[i].Contents = BitConverter.ToInt32( data, off + 32 );
		}

		return result;
	}

	internal void ValidateGeometry()
	{
		if ( Models.Length == 0 )
			throw new InvalidDataException( "BSP has no world model." );
		foreach ( var model in Models )
			ValidateRange( model.FirstFace, model.FaceCount, Faces.Length, "Model faces" );
		foreach ( var vertex in Vertices )
			if ( !IsFinite( vertex ) )
				throw new InvalidDataException( "BSP contains a non-finite vertex." );
		foreach ( var plane in Planes )
			if ( !IsFinite( plane.Normal ) || !float.IsFinite( plane.Dist ) )
				throw new InvalidDataException( "BSP contains a non-finite plane." );
		foreach ( var edge in Edges )
		{
			ValidateRange( edge.V0, 1, Vertices.Length, "Edge vertex" );
			ValidateRange( edge.V1, 1, Vertices.Length, "Edge vertex" );
		}

		foreach ( var edge in SurfEdges )
			ValidateRange( Math.Abs( (long)edge ), 1, Edges.Length, "Surface edge" );
		long normalCount = 0;
		FaceVertexNormalOffsets = new int[Faces.Length];
		for ( int i = 0; i < Faces.Length; i++ )
		{
			ref var face = ref Faces[i];
			FaceVertexNormalOffsets[i] = (int)normalCount;
			if ( face.NumEdges < 3 )
				throw new InvalidDataException( "BSP face has fewer than three edges." );
			if ( face.Side > 1 || face.LightOffset < -1 )
				throw new InvalidDataException( "BSP face has an invalid side or light offset." );
			ValidateRange( face.FirstEdge, face.NumEdges, SurfEdges.Length, "Face surface edges" );
			ValidateRange( face.PlaneNum, 1, Planes.Length, "Face plane" );
			if ( face.TexInfo != -1 )
				ValidateRange( face.TexInfo, 1, TexInfos.Length, "Face texture info" );
			if ( face.DispInfo != -1 )
			{
				ValidateRange( face.DispInfo, 1, DispInfos.Length, "Face displacement" );
				if ( face.NumEdges != 4 )
					throw new InvalidDataException( "Displacement face must be a quad." );
			}

			normalCount += face.NumEdges;
			if ( normalCount > int.MaxValue )
				throw new InvalidDataException( "BSP face vertex count is too large." );
		}

		foreach ( var info in TexInfos )
		{
			if ( info.TexData != -1 )
				ValidateRange( info.TexData, 1, TexDatas.Length, "Texture info data" );
			if ( !IsFinite( info.TextureVecsS ) || !IsFinite( info.TextureVecsT ) || !IsFinite( info.LightmapVecsS ) || !IsFinite( info.LightmapVecsT ) )
				throw new InvalidDataException( "BSP contains non-finite texture vectors." );
		}

		foreach ( var disp in DispInfos )
		{
			if ( disp.Power < 2 || disp.Power > 4 )
				throw new NotSupportedException( $"Unsupported displacement power {disp.Power} (expected 2-4)." );
			int size = (1 << disp.Power) + 1;
			ValidateRange( disp.DispVertStart, size * size, DispVerts.Length, "Displacement vertices" );
			if ( DispTriTags.Length > 0 )
				ValidateRange( disp.DispTriStart, (size - 1) * (size - 1) * 2, DispTriTags.Length, "Displacement triangles" );
			if ( !IsFinite( disp.StartPosition ) )
				throw new InvalidDataException( "BSP contains a non-finite displacement start position." );
		}

		foreach ( var vertex in DispVerts )
			if ( !IsFinite( vertex.Vector ) || !float.IsFinite( vertex.Dist ) || !float.IsFinite( vertex.Alpha ) )
				throw new InvalidDataException( "BSP contains a non-finite displacement vertex." );
		if ( VertNormalIndices.Length > 0 )
		{
			ValidateRange( 0, normalCount, VertNormalIndices.Length, "Face vertex normals" );
			foreach ( var index in VertNormalIndices )
				ValidateRange( index, 1, VertNormals.Length, "Vertex normal index" );
		}

		foreach ( var normal in VertNormals )
			if ( !IsFinite( normal ) )
				throw new InvalidDataException( "BSP contains a non-finite vertex normal." );
	}

	internal static bool IsFinite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
	internal static bool IsFinite( Vector4 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z ) && float.IsFinite( value.w );
}
