using System;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VtxFile
{
	public int NumLods { get; }

	private readonly BodyPartHeader[] _bodyParts;
	private readonly ModelHeader[] _models;
	private readonly ModelLodHeader[] _modelLods;
	private readonly MeshData[] _meshes;
	private readonly int[] _indices;
	private readonly ReadOnlyMemory<StudioVertex> _vertices;
	private readonly ReadOnlyMemory<Vector4> _tangents;
	private readonly int[][] _vertexIndices;

	public bool HasTangents { get; }
	internal readonly record struct LightingMesh( int BodyPart, int Model, int Lod, int Mesh, ushort[] Vertices );
	internal IReadOnlyList<LightingMesh> LightingMeshes { get; }

	public VtxFile( Stream stream, MdlFile mdl, VvdFile vvd, int maxLods = 8, bool readLighting = false )
	{
		if ( !stream.CanSeek )
			throw new InvalidDataException( "VTX parsing requires a seekable stream." );
		if ( maxLods < 1 || maxLods > 8 )
			throw new ArgumentOutOfRangeException( nameof( maxLods ) );
		var stripData = new List<(OptimizedVertex[] Vertices, ushort[] Indices, StripHeader Strip)>();
		var indexCount = 0;
		var lightingMeshes = new List<LightingMesh>();

		using ( var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true ) )
		{
			var version = reader.ReadInt32();

			if ( version != 7 )
				throw new InvalidDataException( $"Unsupported VTX version {version}." );

			var vertCacheSize = reader.ReadInt32();
			var maxBonesPerStrip = reader.ReadUInt16();
			var maxBonesPerTri = reader.ReadUInt16();
			var maxBonesPerVert = reader.ReadInt32();

			var checksum = reader.ReadInt32();
			if ( checksum != vvd.Checksum || checksum != mdl.Checksum )
				throw new InvalidDataException( "MDL, VVD and VTX checksums do not match." );

			var fileLodCount = reader.ReadInt32();
			if ( fileLodCount < 1 || fileLodCount > vvd.NumLods )
				throw new InvalidDataException( "Invalid VTX LOD count." );
			var numLods = NumLods = Math.Min( fileLodCount, maxLods );
			var matReplacementListOffset = reader.ReadInt32();
			_lodSwitchPoints = new float[numLods];
			var haveLodSwitchPoints = false;

			var lodVertexCounts = new int[numLods];
			var lodVertexOffsets = new int[numLods];
			var vertexIndices = new int[numLods][];

			for ( var i = 0; i < numLods; ++i )
			{
				lodVertexCounts[i] = vvd.GetVertexCount( i );
				vertexIndices[i] = vvd.GetVertexIndices( i );
			}

			var numBodyParts = reader.ReadInt32();
			var bodyPartOffset = reader.ReadInt32();
			if ( numBodyParts != mdl.BodyPartCount )
				throw new InvalidDataException( "MDL and VTX body part counts do not match." );
			LumpReader<BodyPartHeader>.ValidateRange( stream, bodyPartOffset, numBodyParts );
			_materialReplacements = ReadMaterialReplacements( stream, matReplacementListOffset, fileLodCount, mdl.MaterialCount );

			_bodyParts = new BodyPartHeader[numBodyParts];

			var modelList = new List<ModelHeader>();
			var modelLodList = new List<ModelLodHeader>();
			var meshList = new List<MeshData>();

			reader.BaseStream.Seek( bodyPartOffset, SeekOrigin.Begin );
			LumpReader<BodyPartHeader>.ReadLumpFromStream( reader.BaseStream, numBodyParts, ( bodyPartIndex, bodyPart ) =>
			{
				if ( bodyPart.NumModels != mdl.GetBodyPart( bodyPartIndex ).NumModels )
					throw new InvalidDataException( "MDL and VTX model counts do not match." );
				reader.BaseStream.Seek( bodyPart.ModelOffset, SeekOrigin.Current );

				bodyPart.ModelOffset = modelList.Count;

				LumpReader<ModelHeader>.ReadLumpFromStream( reader.BaseStream, bodyPart.NumModels, ( modelIndex, model ) =>
				{
					if ( model.NumLods != fileLodCount )
						throw new InvalidDataException( "VTX model LOD counts do not match the header." );
					reader.BaseStream.Seek( model.LodOffset, SeekOrigin.Current );
					LumpReader<ModelLodHeader>.ValidateRange( stream, stream.Position, model.NumLods );

					model.LodOffset = modelLodList.Count;
					model.NumLods = numLods;
					var mdlModel = mdl.GetModel( bodyPartIndex, modelIndex );

					LumpReader<ModelLodHeader>.ReadLumpFromStream( reader.BaseStream, model.NumLods, ( lodIndex, lod ) =>
					{
						if ( !float.IsFinite( lod.SwitchPoint )
							|| (lod.SwitchPoint < 0 && (lodIndex == 0 || lodIndex != fileLodCount - 1)) )
							throw new InvalidDataException( "Invalid VTX LOD switch point." );
						if ( !haveLodSwitchPoints )
							_lodSwitchPoints[lodIndex] = lod.SwitchPoint;
						else if ( mdlModel.NumVertices > 0 && _lodSwitchPoints[lodIndex] != lod.SwitchPoint )
							throw new InvalidDataException( "VTX models have inconsistent LOD switch points." );
						if ( lod.NumMeshes != mdlModel.NumMeshes )
							throw new InvalidDataException( "MDL and VTX mesh counts do not match." );
						reader.BaseStream.Seek( lod.MeshOffset, SeekOrigin.Current );

						lod.MeshOffset = meshList.Count;

						var lodVertexCount = lodVertexCounts[lodIndex];

						LumpReader<MeshHeader>.ReadLumpFromStream( reader.BaseStream, lod.NumMeshes, ( meshIndex, mesh ) =>
						{
							var meshData = new MeshData
							{
								IndexOffset = indexCount,
								VertexOffset = lodVertexOffsets[lodIndex]
							};

							var meshInfo = mdl.GetMesh( bodyPartIndex, modelIndex, meshIndex );
							var meshVertexCount = meshInfo.VertexData._numLodVertices[lodIndex];
							if ( meshVertexCount < 0 || lodVertexOffsets[lodIndex] > lodVertexCount - meshVertexCount )
								throw new InvalidDataException( "MDL mesh vertices exceed the VVD LOD vertex data." );
							meshData.VertexCount = meshVertexCount;

							reader.BaseStream.Seek( mesh.StripGroupHeaderOffset, SeekOrigin.Current );
							LumpReader<StripGroupHeader>.ReadLumpFromStream( reader.BaseStream, mesh.NumStripGroups, stripGroup =>
							{
								var start = reader.BaseStream.Position;
								reader.BaseStream.Seek( start + stripGroup.VertOffset, SeekOrigin.Begin );
								var verts = LumpReader<OptimizedVertex>.ReadLumpFromStream( reader.BaseStream, stripGroup.NumVerts );

								for ( var i = 0; i < verts.Length; ++i )
								{
									if ( verts[i].OrigMeshVertId >= meshVertexCount )
										throw new InvalidDataException( "VTX vertex is outside its MDL mesh." );
								}

								if ( readLighting )
									lightingMeshes.Add( new LightingMesh( bodyPartIndex, modelIndex, lodIndex, meshIndex,
										verts.Select( vertex => vertex.OrigMeshVertId ).ToArray() ) );

								reader.BaseStream.Seek( start + stripGroup.IndexOffset, SeekOrigin.Begin );
								var indices = LumpReader<ushort>.ReadLumpFromStream( reader.BaseStream, stripGroup.NumIndices );

								reader.BaseStream.Seek( start + stripGroup.StripOffset, SeekOrigin.Begin );
								LumpReader<StripHeader>.ReadLumpFromStream( reader.BaseStream, stripGroup.NumStrips, strip =>
								{
									if ( strip.IndexOffset < 0 || strip.NumIndices < 0 || strip.IndexOffset > indices.Length - strip.NumIndices
										|| strip.VertOffset < 0 || strip.NumVerts < 0 || strip.VertOffset > verts.Length - strip.NumVerts )
										throw new InvalidDataException( "VTX strip range is outside its strip group." );
									if ( strip.Flags != StripHeaderFlags.IsTriList && strip.Flags != StripHeaderFlags.IsTriStrip )
										throw new InvalidDataException( "Unsupported VTX strip topology." );
									if ( strip.Flags == StripHeaderFlags.IsTriList && strip.NumIndices % 3 != 0 )
										throw new InvalidDataException( "VTX triangle list has an incomplete triangle." );

									for ( var i = 0; i < strip.NumIndices; ++i )
									{
										if ( indices[strip.IndexOffset + i] >= verts.Length )
											throw new InvalidDataException( "VTX index is outside its strip group." );
									}
									long count = strip.NumIndices;
									if ( strip.Flags == StripHeaderFlags.IsTriStrip )
									{
										count = 0;
										for ( var i = 2; i < strip.NumIndices; ++i )
										{
											var a = indices[strip.IndexOffset + i - 2];
											var b = indices[strip.IndexOffset + i - 1];
											var c = indices[strip.IndexOffset + i];
											if ( a != b && b != c && a != c )
												count += 3;
										}
									}
									if ( count > Array.MaxLength - indexCount )
										throw new InvalidDataException( "VTX index count is too large." );
									indexCount += (int)count;
									stripData.Add( (verts, indices, strip) );
								} );
							} );

							meshData.IndexCount = indexCount - meshData.IndexOffset;

							meshList.Add( meshData );

							lodVertexOffsets[lodIndex] += meshVertexCount;
						} );
						modelLodList.Add( lod );
					} );
					if ( mdlModel.NumVertices > 0 )
						haveLodSwitchPoints = true;

					modelList.Add( model );
				} );

				_bodyParts[bodyPartIndex] = bodyPart;
			} );
			for ( var lod = 0; lod < numLods; ++lod )
				if ( lodVertexOffsets[lod] != lodVertexCounts[lod] )
					throw new InvalidDataException( "MDL mesh counts do not cover the VVD LOD vertices." );
			for ( var lod = 2; lod < RenderLodCount; ++lod )
				_lodSwitchPoints[lod] = Math.Max( _lodSwitchPoints[lod - 1], _lodSwitchPoints[lod] );

			_models = modelList.ToArray();
			_modelLods = modelLodList.ToArray();
			_meshes = meshList.ToArray();

			_indices = indexCount == 0 ? Array.Empty<int>() : GC.AllocateUninitializedArray<int>( indexCount );
			var output = 0;
			foreach ( var (vertices, indices, strip) in stripData )
			{
				if ( strip.Flags == StripHeaderFlags.IsTriList )
				{
					for ( var i = 0; i < strip.NumIndices; ++i )
						_indices[output++] = vertices[indices[strip.IndexOffset + i]].OrigMeshVertId;
				}
				else
				{
					for ( var i = 2; i < strip.NumIndices; ++i )
					{
						var a = indices[strip.IndexOffset + i - 2];
						var b = indices[strip.IndexOffset + i - 1];
						var c = indices[strip.IndexOffset + i];
						if ( a == b || b == c || a == c )
							continue;
						_indices[output++] = vertices[(i & 1) == 0 ? a : b].OrigMeshVertId;
						_indices[output++] = vertices[(i & 1) == 0 ? b : a].OrigMeshVertId;
						_indices[output++] = vertices[c].OrigMeshVertId;
					}
				}
			}
			_vertices = vvd.Vertices;
			_tangents = vvd.Tangents;
			_vertexIndices = vertexIndices;
			HasTangents = vvd.HasTangents;
			LightingMeshes = lightingMeshes;
		}
	}

	public int GetIndexCount( int bodyPart, int model, int lod, int mesh )
	{
		return GetIndices( bodyPart, model, lod, mesh, null );
	}

	public int GetIndices( int bodyPart, int model, int lod, int mesh, int[] destArray, int offset = 0 )
	{
		var meshData = GetMeshData( bodyPart, model, lod, mesh );

		if ( destArray != null )
		{
			Array.Copy( _indices, meshData.IndexOffset, destArray, offset, meshData.IndexCount );
		}

		return meshData.IndexCount;
	}

	public VertexData<StudioVertex> GetVertices( int bodyPart, int model, int lod, int mesh )
		=> GetVertexData( _vertices, bodyPart, model, lod, mesh );

	public VertexData<Vector4> GetTangents( int bodyPart, int model, int lod, int mesh )
	{
		if ( !HasTangents )
			return default;

		return GetVertexData( _tangents, bodyPart, model, lod, mesh );
	}

	private MeshData GetMeshData( int bodyPart, int model, int lod, int mesh )
	{
		var bodyPartHdr = _bodyParts[bodyPart];
		var modelHdr = _models[bodyPartHdr.ModelOffset + model];
		var lodHdr = _modelLods[modelHdr.LodOffset + lod];
		return _meshes[lodHdr.MeshOffset + mesh];
	}

	private VertexData<T> GetVertexData<T>( ReadOnlyMemory<T> source, int bodyPart, int model, int lod, int mesh ) where T : unmanaged
	{
		var data = GetMeshData( bodyPart, model, lod, mesh );
		var indices = _vertexIndices[lod];
		if ( indices == null || data.VertexCount == 0 )
			return new( source.Span.Slice( data.VertexOffset, data.VertexCount ), default );
		return new( source.Span, indices.AsSpan( data.VertexOffset, data.VertexCount ) );
	}

	public readonly ref struct VertexData<T>( ReadOnlySpan<T> values, ReadOnlySpan<int> indices ) where T : unmanaged
	{
		private readonly ReadOnlySpan<T> _values = values;
		private readonly ReadOnlySpan<int> _indices = indices;

		public int Length => _indices.IsEmpty ? _values.Length : _indices.Length;
		public bool IsEmpty => Length == 0;
		public ref readonly T this[int index] => ref _values[_indices.IsEmpty ? index : _indices[index]];

	}
}
