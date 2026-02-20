using System;
using System.Text;

namespace GoldSrc.Mdl;

class File
{
	public Header Header => _header;

	private readonly Header _header;
	private readonly BodyParts[] _bodyParts;
	private readonly Model[] _models;
	private readonly Mesh[] _meshes;
	private readonly Vector3[] _vertices;
	private readonly Vector3[] _normals;
	private readonly TriVert[] _triVerts;
	private readonly byte[] _boneVertices;
	private readonly byte[] _boneNormals;
	private readonly Bone[] _bones;
	private readonly Attachment[] _attachments;
	private readonly TextureData[] _textureDatas;
	private readonly Texture[] _textures;
	private readonly BoundingBox[] _hitBoxes;
	private readonly SequenceDesc[] _sequences;
	private readonly SequenceGroup[] _sequenceGroups;
	private readonly short[] _skinRefs;

	private readonly int[] _bodyPartModelOffsets;
	private readonly int[] _modelMeshOffsets;
	private readonly int[] _modelVertexOffsets;
	private readonly int[] _modelNormalOffsets;
	private readonly int[] _meshTriVertOffsets;

	public int NumBodyParts => _bodyParts.Length;

	public IEnumerable<Bone> Bones => _bones;
	public IEnumerable<SequenceDesc> Sequences => _sequences;

	public IEnumerable<BoundingBox> HitBoxes => _hitBoxes;

	public class BoneTransforms
	{
		public Transform[] Transforms;
	}

	public BoneTransforms[] SequenceTransforms;

	public static string GetNullTerminatedString( byte[] bytes )
	{
		int length = Array.IndexOf( bytes, (byte)0 );
		if ( length == -1 ) return Encoding.ASCII.GetString( bytes );
		return Encoding.ASCII.GetString( bytes, 0, length );
	}

	public BodyParts GetBodyPart( int bodyPartIndex )
	{
		return _bodyParts[bodyPartIndex];
	}

	public Model GetModel( int bodyPartIndex, int modelIndex )
	{
		return _models[GetModelIndex( bodyPartIndex, modelIndex )];
	}

	public Mesh GetMesh( int bodyPartIndex, int modelIndex, int meshIndex )
	{
		return _meshes[GetMeshIndex( bodyPartIndex, modelIndex, meshIndex )];
	}

	private int GetModelIndex( int bodyPartIndex, int modelIndex )
	{
		return _bodyPartModelOffsets[bodyPartIndex] + modelIndex;
	}

	private int GetMeshIndex( int bodyPartIndex, int modelIndex, int meshIndex )
	{
		return _modelMeshOffsets[GetModelIndex( bodyPartIndex, modelIndex )] + meshIndex;
	}

	public int GetVertices( int bodyPartIndex, int modelIndex, Vector3[] destArray, int destOffset = 0 )
	{
		var modelOffset = GetModelIndex( bodyPartIndex, modelIndex );
		var model = _models[modelOffset];

		if ( destArray != null )
		{
			Array.Copy( _vertices, _modelVertexOffsets[modelOffset], destArray, destOffset, model.NumVerts );
		}

		return model.NumVerts;
	}

	public int GetNormals( int bodyPartIndex, int modelIndex, Vector3[] destArray, int destOffset = 0 )
	{
		var modelOffset = GetModelIndex( bodyPartIndex, modelIndex );
		var model = _models[modelOffset];

		if ( destArray != null )
		{
			Array.Copy( _normals, _modelNormalOffsets[modelOffset], destArray, destOffset, model.NumNorms );
		}

		return model.NumNorms;
	}

	public int GetBoneVertices( int bodyPartIndex, int modelIndex, byte[] destArray, int destOffset = 0 )
	{
		var modelOffset = GetModelIndex( bodyPartIndex, modelIndex );
		var model = _models[modelOffset];

		if ( destArray != null )
		{
			Array.Copy( _boneVertices, _modelVertexOffsets[modelOffset], destArray, destOffset, model.NumVerts );
		}

		return model.NumVerts;
	}

	public int GetBoneNormals( int bodyPartIndex, int modelIndex, byte[] destArray, int destOffset = 0 )
	{
		var modelOffset = GetModelIndex( bodyPartIndex, modelIndex );
		var model = _models[modelOffset];

		if ( destArray != null )
		{
			Array.Copy( _boneNormals, _modelVertexOffsets[modelOffset], destArray, destOffset, model.NumVerts );
		}

		return model.NumVerts;
	}

	public int GetTriVerts( int bodyPartIndex, int modelIndex, int meshIndex, TriVert[] destArray, int destOffset = 0 )
	{
		var meshOffset = GetMeshIndex( bodyPartIndex, modelIndex, meshIndex );
		var mesh = _meshes[meshOffset];

		if ( destArray != null )
		{
			Array.Copy( _triVerts, _meshTriVertOffsets[meshOffset] * 3, destArray, destOffset, mesh.NumTris * 3 );
		}

		return mesh.NumTris * 3;
	}

	public int GetTextureIndex( int skinRef, int skinRefFamily = 0 )
	{
		var index = (skinRefFamily * _header.NumSkinRef) + skinRef;
		if ( index < 0 || index >= _skinRefs.Length ) return -1;

		return _skinRefs[index];
	}

	public Texture GetTexture( int index )
	{
		if ( index < 0 || index >= _textures.Length ) return null;

		return _textures[index];
	}

	public void GetTextureData( int index, out string name, out int width, out int height, out LightingFlags flags )
	{
		if ( index < 0 || index >= _textureDatas.Length )
		{
			name = "Null";
			width = 64;
			height = 64;
			flags = 0;
		}
		else
		{
			var textureData = _textureDatas[index];
			name = textureData.Name;
			width = textureData.Width;
			height = textureData.Height;
			flags = textureData.Flags;
		}
	}

	public string GetTextureName( int index )
	{
		if ( index < 0 || index >= _textureDatas.Length )
		{
			return null;
		}
		else
		{
			var textureData = _textureDatas[index];
			return textureData.Name;
		}
	}

	private static Vector3 GetAnimValue( BinaryReader reader, int frame, ushort[] offsets, int baseOffset )
	{
		var value = new float[3];

		for ( var i = 0; i < 3; ++i )
		{
			if ( offsets[i] == 0 ) continue;

			var offset = baseOffset + offsets[i];
			reader.BaseStream.Seek( offset, SeekOrigin.Begin );
			var valid = reader.ReadByte();
			var total = reader.ReadByte();

			var span = frame;
			while ( total <= span )
			{
				span -= total;
				offset += (valid + 1) * 2;
				reader.BaseStream.Seek( offset, SeekOrigin.Begin );
				valid = reader.ReadByte();
				total = reader.ReadByte();
			}

			if ( valid > span )
			{
				offset += (span + 1) * 2;
			}
			else
			{
				offset += valid * 2;
			}

			reader.BaseStream.Seek( offset, SeekOrigin.Begin );
			value[i] = reader.ReadInt16();
		}

		return new Vector3( value[0], value[1], value[2] );
	}


	private static Vector3 GetRotationAnimValue( BinaryReader reader, int frame, ushort[] offsets, int baseOffset, Bone bone )
	{
		var value = new float[3];

		for ( var i = 0; i < 3; ++i )
		{
			if ( offsets[i] == 0 )
			{
				value[i] = bone.Rotation[i]; // default;
			}
			else
			{
				var offset = baseOffset + offsets[i];
				reader.BaseStream.Seek( offset, SeekOrigin.Begin );
				var valid = reader.ReadByte();
				var total = reader.ReadByte();

				var span = frame;
				while ( total <= span )
				{
					span -= total;
					offset += (valid + 1) * 2;
					reader.BaseStream.Seek( offset, SeekOrigin.Begin );
					valid = reader.ReadByte();
					total = reader.ReadByte();
				}

				if ( valid > span )
				{
					offset += (span + 1) * 2;
				}
				else
				{
					offset += valid * 2;
				}

				reader.BaseStream.Seek( offset, SeekOrigin.Begin );
				value[i] = bone.Rotation[i] + (reader.ReadInt16() * bone.RotationScale[i]);
			}
		}

		return new Vector3( value[0], value[1], value[2] );
	}


	private static Rotation CalcBoneRotation( BinaryReader reader, int frame, Bone bone, Anim anim, int baseOffset )
	{
		var value = GetRotationAnimValue( reader, frame, anim.RotationOffsets, baseOffset, bone );
		return AngleQuaternion( value );
	}

	private static Rotation AngleQuaternion( Vector3 angles )
	{
		float angle;
		float sr, sp, sy, cr, cp, cy;

		angle = angles[2] * 0.5f; // Z (yaw)
		sy = MathF.Sin( angle );
		cy = MathF.Cos( angle );
		angle = angles[1] * 0.5f; // Y (pitch)
		sp = MathF.Sin( angle );
		cp = MathF.Cos( angle );
		angle = angles[0] * 0.5f; // X (roll)
		sr = MathF.Sin( angle );
		cr = MathF.Cos( angle );

		var quaternion = new Rotation
		{
			x = (sr * cp * cy) - (cr * sp * sy),
			y = (cr * sp * cy) + (sr * cp * sy),
			z = (cr * cp * sy) - (sr * sp * cy),
			w = (cr * cp * cy) + (sr * sp * sy)
		};
		return quaternion;
	}

	private static Vector3 CalcBonePosition( BinaryReader reader, int frame, Bone bone, Anim anim, int baseOffset )
	{
		var value = bone.Position + (GetAnimValue( reader, frame, anim.PositionOffsets, baseOffset ) * bone.PositionScale);
		return value;
	}

	public static File FromStream( Stream stream )
	{
		return new File( stream );
	}

	private File( Stream stream )
	{
		var ms = stream;
		using var br = new BinaryReader( ms );

		_header = new Header( br );

		ms.Seek( _header.BodyPartIndex, SeekOrigin.Begin );
		_bodyParts = new BodyParts[_header.NumBodyParts];
		for ( var i = 0; i < _bodyParts.Length; i++ )
		{
			_bodyParts[i] = new BodyParts( br );
		}

		_bodyPartModelOffsets = new int[_bodyParts.Length];
		_models = new Model[_bodyParts.Sum( x => x.NumModels )];
		_modelMeshOffsets = new int[_models.Length];
		_modelVertexOffsets = new int[_models.Length];
		_modelNormalOffsets = new int[_models.Length];

		var modelsRead = 0;

		for ( var index = 0; index < _bodyParts.Length; index++ )
		{
			var bodyPart = _bodyParts[index];
			_bodyPartModelOffsets[index] = modelsRead;

			ms.Seek( bodyPart.ModelIndex, SeekOrigin.Begin );
			for ( var i = 0; i < bodyPart.NumModels; i++ ) _models[modelsRead + i] = new Model( br );
			modelsRead += bodyPart.NumModels;
		}

		_meshes = new Mesh[_models.Sum( x => x.NumMesh )];
		_vertices = new Vector3[_models.Sum( x => x.NumVerts )];
		_boneVertices = new byte[_vertices.Length];
		_boneNormals = new byte[_vertices.Length];
		_normals = new Vector3[_models.Sum( x => x.NumNorms )];
		_meshTriVertOffsets = new int[_meshes.Length];

		var meshesRead = 0;
		var verticesRead = 0;
		var normalsRead = 0;

		for ( var index = 0; index < _models.Length; index++ )
		{
			var model = _models[index];
			_modelMeshOffsets[index] = meshesRead;
			_modelVertexOffsets[index] = verticesRead;
			_modelNormalOffsets[index] = normalsRead;

			ms.Seek( model.MeshIndex, SeekOrigin.Begin );
			for ( var i = 0; i < model.NumMesh; i++ ) _meshes[meshesRead + i] = new Mesh( br );
			meshesRead += model.NumMesh;

			ms.Seek( model.VertIndex, SeekOrigin.Begin );
			for ( var i = 0; i < model.NumVerts; i++ ) _vertices[verticesRead + i] = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );

			ms.Seek( model.VertInfoIndex, SeekOrigin.Begin );
			for ( var i = 0; i < model.NumVerts; i++ ) _boneVertices[verticesRead + i] = br.ReadByte();

			ms.Seek( model.NormInfoIndex, SeekOrigin.Begin );
			for ( var i = 0; i < model.NumVerts; i++ ) _boneNormals[verticesRead + i] = br.ReadByte();

			verticesRead += model.NumVerts;

			ms.Seek( model.NormIndex, SeekOrigin.Begin );
			for ( var i = 0; i < model.NumNorms; i++ ) _normals[normalsRead + i] = new Vector3( br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );

			normalsRead += model.NumNorms;
		}

		_triVerts = new TriVert[_meshes.Sum( x => x.NumTris ) * 3];
		var trisRead = 0;

		for ( var index = 0; index < _meshes.Length; index++ )
		{
			var mesh = _meshes[index];

			_meshTriVertOffsets[index] = trisRead;

			ms.Seek( mesh.TriIndex, SeekOrigin.Begin );
			while ( true )
			{
				var triTypeNum = br.ReadInt16();
				if ( triTypeNum == 0 ) break;

				var indexCount = Math.Abs( triTypeNum );
				var triangleCount = indexCount - 2;

				var triVerts = new TriVert[indexCount];
				for ( var i = 0; i < indexCount; ++i )
				{
					triVerts[i] = new TriVert( br );
				}

				var triangleIndices = new int[3];

				for ( var vertexIndex = 0; vertexIndex < triangleCount; ++vertexIndex )
				{
					if ( triTypeNum < 0 )
					{
						triangleIndices[0] = vertexIndex + 2;
						triangleIndices[1] = vertexIndex + 1;
						triangleIndices[2] = 0;
					}
					else if ( (vertexIndex % 2) == 0 )
					{
						triangleIndices[0] = vertexIndex + 2;
						triangleIndices[1] = vertexIndex + 1;
						triangleIndices[2] = vertexIndex;
					}
					else
					{
						triangleIndices[0] = vertexIndex;
						triangleIndices[1] = vertexIndex + 1;
						triangleIndices[2] = vertexIndex + 2;
					}

					for ( var triangleIndex = 0; triangleIndex < 3; ++triangleIndex )
					{
						var dstOffset = (trisRead * 3) + (vertexIndex * 3) + triangleIndex;
						_triVerts[dstOffset] = triVerts[triangleIndices[triangleIndex]];
					}
				}

				trisRead += triangleCount;
			}
		}

		_textureDatas = new TextureData[_header.NumTextures];
		ms.Seek( _header.TextureIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _textureDatas.Length; i++ ) _textureDatas[i] = new TextureData( br );

		_textures = new Texture[_header.NumTextures];

		for ( var i = 0; i < _textureDatas.Length; ++i )
		{
			var textureData = _textureDatas[i];
			var pixelCount = textureData.Width * textureData.Height;

			ms.Seek( textureData.Index, SeekOrigin.Begin );
			var pixelData = br.ReadBytes( pixelCount );

			ms.Seek( textureData.Index + pixelCount, SeekOrigin.Begin );
			var palette = br.ReadBytes( 256 * 3 );

			if ( (textureData.Flags & LightingFlags.Masked) != 0 )
			{
				palette[(255 * 3) + 0] = 0;
				palette[(255 * 3) + 1] = 0;
				palette[(255 * 3) + 2] = 0;
			}

			var imageData = new byte[pixelCount * 4];
			var imageDataSpan = imageData.AsSpan();
			var paletteSpan = palette.AsSpan();

			for ( var j = 0; j < pixelCount; ++j )
			{
				var palIndex = pixelData[j] * 3;
				var destIndex = j * 4;

				imageDataSpan[destIndex] = paletteSpan[palIndex];
				imageDataSpan[destIndex + 1] = paletteSpan[palIndex + 1];
				imageDataSpan[destIndex + 2] = paletteSpan[palIndex + 2];
				imageDataSpan[destIndex + 3] = 255;
			}

			var width = textureData.Width;
			var height = textureData.Height;

			_textures[i] = Texture.Create( width, height )
				.WithData( imageData )
				.WithMips()
				.Finish();
		}

		_skinRefs = new short[_header.NumSkinFamilies * _header.NumSkinRef];
		ms.Seek( _header.SkinIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _skinRefs.Length; i++ ) _skinRefs[i] = br.ReadInt16();

		_bones = new Bone[_header.NumBones];
		ms.Seek( _header.BoneIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _bones.Length; i++ ) _bones[i] = new Bone( br );

		_attachments = new Attachment[_header.NumAttachments];
		ms.Seek( _header.AttachmentIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _attachments.Length; i++ ) _attachments[i] = new Attachment( br );

		_hitBoxes = new BoundingBox[_header.NumHitBoxes];
		ms.Seek( _header.HitBoxIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _hitBoxes.Length; i++ ) _hitBoxes[i] = new BoundingBox( br );

		_sequenceGroups = new SequenceGroup[_header.NumSeqGroups];
		ms.Seek( _header.SeqGroupIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _sequenceGroups.Length; i++ ) _sequenceGroups[i] = new SequenceGroup( br );

		_sequences = new SequenceDesc[_header.NumSeq];
		ms.Seek( _header.SeqIndex, SeekOrigin.Begin );
		for ( var i = 0; i < _sequences.Length; i++ ) _sequences[i] = new SequenceDesc( br );
	}

	public void LoadAnimations( List<BinaryReader> readers )
	{
		SequenceTransforms = new BoneTransforms[_sequences.Length];

		for ( var sequenceIndex = 0; sequenceIndex < _sequences.Length; ++sequenceIndex )
		{
			var seq = _sequences[sequenceIndex];
			var br = readers[seq.SeqGroup];

			var anims = new Anim[_header.NumBones];
			br.BaseStream.Seek( seq.AnimIndex, SeekOrigin.Begin );
			for ( var i = 0; i < anims.Length; i++ )
			{
				anims[i] = new Anim( br );
			}

			SequenceTransforms[sequenceIndex] = new BoneTransforms { Transforms = new Transform[_header.NumBones * seq.NumFrames] };

			const int animSize = 12;

			for ( var boneIndex = 0; boneIndex < _header.NumBones; ++boneIndex )
			{
				var bone = _bones[boneIndex];
				var anim = anims[boneIndex];
				var animOffset = seq.AnimIndex + (boneIndex * animSize);

				for ( var frameIndex = 0; frameIndex < seq.NumFrames; ++frameIndex )
				{
					var position = CalcBonePosition( br, frameIndex, bone, anim, animOffset );
					var rotation = CalcBoneRotation( br, frameIndex, bone, anim, animOffset );

					if ( seq.MotionBone == boneIndex )
					{
						if ( seq.MotionType.Contains( MotionFlags.X ) ) position = position.WithX( 0 );
						if ( seq.MotionType.Contains( MotionFlags.Y ) ) position = position.WithY( 0 );
						if ( seq.MotionType.Contains( MotionFlags.Z ) ) position = position.WithZ( 0 );
					}

					SequenceTransforms[sequenceIndex].Transforms[(frameIndex * _header.NumBones) + boneIndex] = new Transform( position, rotation );
				}
			}
		}
	}
}
