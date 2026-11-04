using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.Mounting.Source1;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct StudioVertex
{
	public StudioBoneWeight BoneWeights;
	public Vector3 Position;
	public Vector3 Normal;
	public float TexCoordX;
	public float TexCoordY;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct StudioBoneWeight
{
	public float Weight0;
	public float Weight1;
	public float Weight2;
	public byte Bone0;
	public byte Bone1;
	public byte Bone2;
	public byte NumBones;
}

internal sealed class VvdFile
{
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct VertexFixup
	{
		public int Lod;
		public int SourceVertexId;
		public int NumVertices;
	}

	private readonly int[] _numVerts;
	private readonly StudioVertex[] _vertices;
	private readonly Vector4[] _tangents;
	private readonly VertexFixup[] _fixups;

	public int NumLods { get; }
	public int Checksum { get; }
	public bool HasTangents { get; }

	public VvdFile( Stream stream )
	{
		if ( !stream.CanSeek )
			throw new InvalidDataException( "VVD parsing requires a seekable stream." );

		using ( var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true ) )
		{
			var id = reader.ReadInt32();
			var version = reader.ReadInt32();

			if ( id != 0x56534449 || version != 4 )
				throw new InvalidDataException( "Unsupported VVD signature or version." );

			Checksum = reader.ReadInt32();

			NumLods = reader.ReadInt32();
			if ( NumLods < 1 || NumLods > 8 )
				throw new InvalidDataException( "Invalid VVD LOD count." );
			_numVerts = new int[NumLods];

			for ( var i = 0; i < 8; ++i )
			{
				var count = reader.ReadInt32();
				if ( i < NumLods )
				{
					if ( count < 0 || (i > 0 && count > _numVerts[i - 1]) )
						throw new InvalidDataException( "Invalid VVD LOD vertex count." );
					_numVerts[i] = count;
				}
			}

			var numFixups = reader.ReadInt32();
			var fixupTableStart = reader.ReadInt32();
			var vertexDataStart = reader.ReadInt32();
			var tangentDataStart = reader.ReadInt32();

			LumpReader<VertexFixup>.ValidateRange( stream, fixupTableStart, numFixups );
			LumpReader<StudioVertex>.ValidateRange( stream, vertexDataStart, _numVerts[0] );
			if ( _numVerts[0] > 0 && vertexDataStart < 64 )
				throw new InvalidDataException( "VVD vertex data overlaps its header." );

			if ( numFixups > 0 )
			{
				if ( fixupTableStart < 64 || (long)fixupTableStart + (long)numFixups * Marshal.SizeOf<VertexFixup>() > vertexDataStart )
					throw new InvalidDataException( "VVD fixup table overlaps vertex data." );
				reader.BaseStream.Seek( fixupTableStart, SeekOrigin.Begin );
				_fixups = LumpReader<VertexFixup>.ReadLumpFromStream( reader.BaseStream, numFixups );

				var lodCounts = new long[NumLods];
				foreach ( var fixup in _fixups )
				{
					if ( fixup.Lod < 0 || fixup.Lod >= NumLods || fixup.NumVertices < 0
						|| fixup.SourceVertexId < 0 || fixup.SourceVertexId > _numVerts[0] - fixup.NumVertices )
						throw new InvalidDataException( "Invalid VVD vertex fixup." );
					for ( var lod = 0; lod <= fixup.Lod; ++lod )
						lodCounts[lod] += fixup.NumVertices;
				}
				for ( var lod = 0; lod < NumLods; ++lod )
					if ( lodCounts[lod] != _numVerts[lod] )
						throw new InvalidDataException( "VVD fixups do not match the LOD vertex counts." );

				var sourceRanges = (VertexFixup[])_fixups.Clone();
				Array.Sort( sourceRanges, ( a, b ) => a.SourceVertexId.CompareTo( b.SourceVertexId ) );
				var sourceEnd = 0;
				foreach ( var fixup in sourceRanges )
				{
					if ( fixup.NumVertices == 0 )
						continue;
					if ( fixup.SourceVertexId != sourceEnd )
						throw new InvalidDataException( "VVD fixups contain overlapping or missing vertex ranges." );
					sourceEnd += fixup.NumVertices;
				}
			}

			HasTangents = tangentDataStart != 0;

			reader.BaseStream.Seek( vertexDataStart, SeekOrigin.Begin );
			_vertices = LumpReader<StudioVertex>.ReadLumpFromStream( reader.BaseStream, _numVerts[0] );

			if ( HasTangents )
			{
				if ( tangentDataStart < (long)vertexDataStart + (long)_numVerts[0] * Marshal.SizeOf<StudioVertex>() )
					throw new InvalidDataException( "VVD tangent data overlaps vertex data." );
				reader.BaseStream.Seek( tangentDataStart, SeekOrigin.Begin );
				_tangents = LumpReader<Vector4>.ReadLumpFromStream( reader.BaseStream, _numVerts[0] );
			}
		}
	}

	public int GetVertexCount( int lod )
	{
		if ( (uint)lod >= (uint)NumLods )
			throw new ArgumentOutOfRangeException( nameof( lod ) );
		return _numVerts[lod];
	}

	internal ReadOnlyMemory<StudioVertex> Vertices => _vertices;
	internal ReadOnlyMemory<Vector4> Tangents => _tangents;

	internal int[] GetVertexIndices( int lod )
	{
		var count = GetVertexCount( lod );
		if ( _fixups == null )
			return null;

		var result = GC.AllocateUninitializedArray<int>( count );
		var offset = 0;
		foreach ( var fixup in _fixups )
		{
			if ( fixup.Lod < lod )
				continue;
			for ( var i = 0; i < fixup.NumVertices; ++i )
				result[offset++] = fixup.SourceVertexId + i;
		}
		return result;
	}

}
