using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	private string ReadNullTerminatedString( Stream stream )
	{
		var builder = new StringBuilder();

		while ( stream.Position < _header.Length )
		{
			var c = stream.ReadByte();
			if ( c < 0 ) break;
			if ( c == 0 ) return builder.ToString();
			builder.Append( (char)c );
		}
		throw new InvalidDataException( "Unterminated MDL string." );
	}

	private void ValidateRange<T>( Stream stream, long offset, int count ) where T : struct
	{
		LumpReader<T>.ValidateRange( stream, offset, count );
		if ( offset > _header.Length || (long)count * Marshal.SizeOf<T>() > _header.Length - offset )
			throw new InvalidDataException( $"MDL {typeof( T ).Name} range exceeds the declared file length." );
	}

	private string ReadOptionalRelativeString( Stream stream, long start, int offset )
	{
		if ( offset == 0 )
			return string.Empty;
		ValidateRange<byte>( stream, start + offset, 1 );
		stream.Position = start + offset;
		return ReadNullTerminatedString( stream );
	}

	public MdlFile( Stream stream )
	{
		if ( !stream.CanSeek )
			throw new InvalidDataException( "MDL parsing requires a seekable stream." );
		_header = LumpReader<Header>.ReadSingleFromStream( stream );

		if ( _header.Id != 0x54534449 ) throw new InvalidDataException( "Not a MDL file." );
		if ( _header.Version < 44 || _header.Version > 49 )
			throw new InvalidDataException( $"Unsupported MDL version {_header.Version}." );
		if ( _header.Length < Marshal.SizeOf<Header>() || _header.Length > stream.Length )
			throw new InvalidDataException( "Invalid MDL file length." );
		if ( !float.IsFinite( VertAnimFixedPointScale ) || VertAnimFixedPointScale <= 0 )
			throw new InvalidDataException( "Invalid MDL vertex animation scale." );

		ValidateRange<StudioTexture>( stream, _header.TextureIndex, _header.NumTextures );
		ValidateRange<int>( stream, _header.CdTextureIndex, _header.NumCdTextures );
		ValidateRange<StudioBodyPart>( stream, _header.BodyPartIndex, _header.NumBodyParts );
		ValidateRange<StudioBone>( stream, _header.BoneIndex, _header.NumBones );
		ValidateRange<StudioAttachment>( stream, _header.LocalAttachmentIndex, _header.NumLocalAttachments );
		if ( _header.StudioHdr2Offset != 0 )
		{
			ValidateRange<int>( stream, (long)_header.StudioHdr2Offset + 8, 1 );
			stream.Position = (long)_header.StudioHdr2Offset + 8;
			Span<byte> attachment = stackalloc byte[4];
			stream.ReadExactly( attachment );
			IlluminationAttachment = BitConverter.ToInt32( attachment );
			if ( IlluminationAttachment > _header.NumLocalAttachments )
				throw new InvalidDataException( "MDL illumination position references an invalid attachment." );
		}
		ValidateRange<StudioFlexDesc>( stream, _header.FlexDescOffset, _header.NumFlexDesc );
		_mouths = ReadMouths( stream );
		_hitboxSets = ReadHitboxSets( stream );
		SurfaceProperty = ReadOptionalRelativeString( stream, 0, _header.SurfacePropOffset );
		KeyValues = ReadKeyValues( stream );
		if ( _header.NumSkinRef < 0 || _header.NumSkinFamilies < 0 || (long)_header.NumSkinRef * _header.NumSkinFamilies > int.MaxValue )
			throw new InvalidDataException( "Invalid MDL skin table counts." );
		ValidateRange<short>( stream, _header.SkinIndex, _header.NumSkinRef * _header.NumSkinFamilies );
		stream.Seek( _header.SkinIndex, SeekOrigin.Begin );
		_skinFamilies = LumpReader<short>.ReadLumpFromStream( stream, _header.NumSkinFamilies * _header.NumSkinRef );
		_defaultSkin = _skinFamilies.AsSpan( 0, _header.NumSkinFamilies > 0 ? _header.NumSkinRef : 0 ).ToArray();
		foreach ( var material in _skinFamilies )
			if ( material < 0 || material >= _header.NumTextures )
				throw new InvalidDataException( "MDL skin references an invalid texture." );

		_materials = new StudioTexture[_header.NumTextures];
		_materialNames = new string[_header.NumTextures];

		stream.Seek( _header.TextureIndex, SeekOrigin.Begin );
		LumpReader<StudioTexture>.ReadLumpFromStream( stream, _header.NumTextures, ( index, tex ) =>
		{
			_materials[index] = tex;

			stream.Seek( tex.NameIndex, SeekOrigin.Current );
			_materialNames[index] = ReadNullTerminatedString( stream ).Replace( '\\', '/' ) + ".vmt";
		} );

		_materialPaths = new string[_header.NumCdTextures];

		stream.Seek( _header.CdTextureIndex, SeekOrigin.Begin );
		LumpReader<int>.ReadLumpFromStream( stream, _header.NumCdTextures, ( index, cdTex ) =>
		{
			stream.Seek( cdTex, SeekOrigin.Begin );
			_materialPaths[index] = ReadNullTerminatedString( stream ).Replace( '\\', '/' );
		} );

		_bodyParts = new StudioBodyPart[_header.NumBodyParts];
		_bodyPartNames = new string[_header.NumBodyParts];

		var modelList = new List<StudioModel>();
		var meshList = new List<StudioMesh>();
		var eyeballList = new List<StudioEyeball>();
		var flexList = new List<StudioFlex>();
		var vertAnimRanges = new List<(long Offset, int MeshVertexCount)>();
		var vertAnimCount = 0;

		stream.Seek( _header.BodyPartIndex, SeekOrigin.Begin );
		LumpReader<StudioBodyPart>.ReadLumpFromStream( stream, _header.NumBodyParts, ( partIndex, part ) =>
		{
			var partPos = stream.Position;

			stream.Seek( partPos + part.NameIndex, SeekOrigin.Begin );
			_bodyPartNames[partIndex] = ReadNullTerminatedString( stream );

			stream.Seek( partPos + part.ModelIndex, SeekOrigin.Begin );
			ValidateRange<StudioModel>( stream, stream.Position, part.NumModels );

			part.ModelIndex = modelList.Count;

			LumpReader<StudioModel>.ReadLumpFromStream( stream, part.NumModels, ( modelIndex, model ) =>
			{
				var modelPos = stream.Position;
				if ( model.NumEyeBalls < 0 )
					throw new InvalidDataException( "Invalid MDL eyeball count." );
				var eyes = Array.Empty<StudioEyeball>();
				if ( model.NumEyeBalls > 0 )
				{
					ValidateRange<StudioEyeball>( stream, modelPos + model.EyeBallIndex, model.NumEyeBalls );
					stream.Position = modelPos + model.EyeBallIndex;
					eyes = LumpReader<StudioEyeball>.ReadLumpFromStream( stream, model.NumEyeBalls );
				}
				foreach ( var eye in eyes )
				{
					if ( eye.Bone < 0 || eye.Bone >= _header.NumBones
						|| !float.IsFinite( eye.Radius ) || eye.Radius <= 0
						|| !float.IsFinite( eye.IrisScale ) || eye.IrisScale <= 0
						|| !float.IsFinite( eye.ZOffset ) || !IsFinite( eye.Origin )
						|| !IsFinite( eye.Up ) || !IsFinite( eye.Forward ) )
						throw new InvalidDataException( "Invalid MDL eyeball projection." );
				}
				model.EyeBallIndex = eyeballList.Count;
				eyeballList.AddRange( eyes );
				stream.Seek( modelPos + model.MeshIndex, SeekOrigin.Begin );
				ValidateRange<StudioMesh>( stream, stream.Position, model.NumMeshes );
				if ( model.NumVertices < 0 )
					throw new InvalidDataException( "Invalid MDL model vertex count." );

				model.MeshIndex = meshList.Count;

				LumpReader<StudioMesh>.ReadLumpFromStream( stream, model.NumMeshes, ( meshIndex, mesh ) =>
				{
					var meshPos = stream.Position;
					if ( mesh.NumVertices < 0 || mesh.VertexOffset < 0 || mesh.VertexOffset > model.NumVertices - mesh.NumVertices )
						throw new InvalidDataException( "MDL mesh vertices are outside their model." );
					for ( var lod = 0; lod < 8; ++lod )
						if ( mesh.VertexData._numLodVertices[lod] < 0 || mesh.VertexData._numLodVertices[lod] > (lod == 0 ? mesh.NumVertices : mesh.VertexData._numLodVertices[lod - 1]) )
							throw new InvalidDataException( "Invalid MDL mesh LOD vertex count." );
					if ( mesh.Material < 0 || mesh.Material >= (_defaultSkin.Length > 0 ? _defaultSkin.Length : _header.NumTextures) )
						throw new InvalidDataException( "MDL mesh references an invalid material." );
					if ( mesh.MaterialType == 1 && (mesh.MaterialParam < 0 || mesh.MaterialParam >= model.NumEyeBalls) )
						throw new InvalidDataException( "MDL eye mesh references an invalid eyeball." );
					stream.Seek( meshPos + mesh.FlexIndex, SeekOrigin.Begin );
					ValidateRange<StudioFlex>( stream, stream.Position, mesh.NumFlexes );

					mesh.ModelIndex = modelIndex;
					mesh.FlexIndex = flexList.Count;
					meshList.Add( mesh );

					LumpReader<StudioFlex>.ReadLumpFromStream( stream, mesh.NumFlexes, ( flexIndex, flex ) =>
					{
						var flexPos = stream.Position;
						if ( flex.FlexDesc < 0 || flex.FlexDesc >= _header.NumFlexDesc )
							throw new InvalidDataException( "MDL flex references an invalid descriptor." );
						if ( flex.FlexPair < 0 || flex.FlexPair >= _header.NumFlexDesc )
							throw new InvalidDataException( "MDL flex references an invalid paired descriptor." );
						var vertexOffset = flexPos + flex.VertIndex;
						if ( flex.VertAnimType == StudioVertAnimType.Normal )
						{
							ValidateRange<StudioVertAnim>( stream, vertexOffset, flex.NumVerts );
						}
						else if ( flex.VertAnimType == StudioVertAnimType.Wrinkle )
						{
							ValidateRange<StudioVertAnimWrinkle>( stream, vertexOffset, flex.NumVerts );
						}
						else
							throw new InvalidDataException( "Unsupported MDL vertex animation type." );
						if ( flex.NumVerts > Array.MaxLength - vertAnimCount )
							throw new InvalidDataException( "MDL vertex animation count is too large." );

						flex.VertIndex = vertAnimCount;
						vertAnimCount += flex.NumVerts;
						flexList.Add( flex );
						vertAnimRanges.Add( (vertexOffset, mesh.NumVertices) );
					} );
				} );

				modelList.Add( model );
			} );

			_bodyParts[partIndex] = part;
		} );

		_models = modelList.ToArray();
		_eyeballs = eyeballList.ToArray();
		_meshes = meshList.ToArray();
		_flexes = flexList.ToArray();
		_vertAnims = vertAnimCount == 0 ? Array.Empty<StudioVertAnim>() : GC.AllocateUninitializedArray<StudioVertAnim>( vertAnimCount );
		for ( var i = 0; i < _flexes.Length; ++i )
		{
			var flex = _flexes[i];
			var range = vertAnimRanges[i];
			stream.Position = range.Offset;
			var vertices = _vertAnims.AsSpan( flex.VertIndex, flex.NumVerts );
			if ( flex.VertAnimType == StudioVertAnimType.Normal )
			{
				stream.ReadExactly( MemoryMarshal.AsBytes( vertices ) );
			}
			else
			{
				var wrinkles = LumpReader<StudioVertAnimWrinkle>.ReadLumpFromStream( stream, flex.NumVerts );
				for ( var vertex = 0; vertex < vertices.Length; ++vertex )
					vertices[vertex] = wrinkles[vertex].Vertex;
			}
			foreach ( var vertex in vertices )
				if ( vertex.Index >= range.MeshVertexCount )
					throw new InvalidDataException( "MDL flex vertex is outside its mesh." );
		}

		_bones = new StudioBone[_header.NumBones];
		_boneNames = new string[_header.NumBones];
		_boneSurfaceProperties = new string[_header.NumBones];

		stream.Seek( _header.BoneIndex, SeekOrigin.Begin );
		LumpReader<StudioBone>.ReadLumpFromStream( stream, _header.NumBones, ( boneIndex, bone ) =>
		{
			var pos = stream.Position;
			stream.Seek( pos + bone.NameIndex, SeekOrigin.Begin );
			_boneNames[boneIndex] = ReadNullTerminatedString( stream );
			var surface = ReadOptionalRelativeString( stream, pos, bone.SurfacePropIndex );
			_boneSurfaceProperties[boneIndex] = string.IsNullOrWhiteSpace( surface ) ? SurfaceProperty : surface;
			stream.Seek( pos, SeekOrigin.Begin );

			_bones[boneIndex] = bone;
		} );

		_attachments = new StudioAttachment[_header.NumLocalAttachments];
		_attachmentNames = new string[_header.NumLocalAttachments];

		stream.Seek( _header.LocalAttachmentIndex, SeekOrigin.Begin );
		LumpReader<StudioAttachment>.ReadLumpFromStream( stream, _header.NumLocalAttachments, ( attachmentIndex, attachment ) =>
		{
			var pos = stream.Position;
			stream.Seek( pos + attachment.NameIndex, SeekOrigin.Begin );
			_attachmentNames[attachmentIndex] = ReadNullTerminatedString( stream );
			stream.Seek( pos, SeekOrigin.Begin );

			_attachments[attachmentIndex] = attachment;
		} );

		_flexNames = new string[_header.NumFlexDesc];

		stream.Seek( _header.FlexDescOffset, SeekOrigin.Begin );
		LumpReader<StudioFlexDesc>.ReadLumpFromStream( stream, _header.NumFlexDesc, ( flexDescIndex, flexDesc ) =>
		{
			var pos = stream.Position;
			stream.Seek( pos + flexDesc.FACSindex, SeekOrigin.Begin );
			_flexNames[flexDescIndex] = ReadNullTerminatedString( stream );
			stream.Seek( pos, SeekOrigin.Begin );
		} );

	}
}
