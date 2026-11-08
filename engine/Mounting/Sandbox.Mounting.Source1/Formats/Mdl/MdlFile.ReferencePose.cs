using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	internal Transform[] ReadPhysicsReferencePose( Stream stream, Func<string, Stream> openFile )
		=> ReadReferencePose( stream, openFile, 0 );

	internal Transform[] ReadSequenceReferencePose( Stream stream, Func<string, Stream> openFile )
	{
		if ( _header.NumLocalSeq == 0 || BoneCount == 0 )
			return null;
		var offset = _header.LocalSeqIndex;
		ValidateRange<StudioSeqDesc>( stream, offset, 1 );
		stream.Position = offset;
		var sequence = LumpReader<StudioSeqDesc>.ReadSingleFromStream( stream );
		if ( sequence.NumBlends != 1 || sequence.NumAutoLayers != 0 )
			throw new NotSupportedException( "The initial sequence requires animation blends or layers." );
		if ( sequence.AnimIndexIndex <= 0 )
			throw new InvalidDataException( "Missing initial sequence animation index." );
		using var data = new ReferencePoseData( stream, 0, _header.Length );
		return ReadReferencePose( stream, openFile, data.Short( (long)offset + sequence.AnimIndexIndex ) );
	}

	private Transform[] ReadReferencePose( Stream stream, Func<string, Stream> openFile, int animationIndex )
	{
		if ( _header.NumLocalAnims == 0 || BoneCount == 0 )
			return null;
		if ( animationIndex < 0 || animationIndex >= _header.NumLocalAnims )
			throw new InvalidDataException( "Invalid reference animation index." );
		using var data = new ReferencePoseData( stream, 0, _header.Length );
		var descriptorOffset = _header.LocalAnimIndex + (long)animationIndex * System.Runtime.InteropServices.Marshal.SizeOf<StudioAnimDesc>();
		ValidateRange<StudioAnimDesc>( stream, descriptorOffset, 1 );
		stream.Position = descriptorOffset;
		var animation = LumpReader<StudioAnimDesc>.ReadSingleFromStream( stream );
		if ( animation.NumFrames < 1 || animation.SectionFrames < 0 )
			throw new InvalidDataException( "Invalid reference animation frame count or section size." );
		if ( (animation.Flags & 0x0004) != 0 || animation.NumLocalHierarchy != 0
			|| animation.NumIkRules != 0 || animation.NumMovements != 0 )
			throw new NotSupportedException( "The reference animation requires additive poses, reparenting, IK or root motion." );
		var pose = Bones.Select( bone => new Transform( bone.Position, bone.Quaternion ) ).ToArray();
		if ( (animation.Flags & 0x0020) == 0 )
		{
			var block = animation.AnimBlock;
			var index = animation.AnimIndex;
			if ( animation.SectionFrames > 0 )
			{
				if ( animation.SectionIndex <= 0 )
					throw new InvalidDataException( "Missing reference animation section table." );
				block = data.Int( descriptorOffset + animation.SectionIndex );
				index = data.Int( descriptorOffset + animation.SectionIndex + 4 );
			}
			if ( block == -1 || (block == 0 && index == 0) )
				throw new NotSupportedException( "The first reference frame has no complete animation data." );
			if ( block == 0 )
				ReadReferenceBones( data, descriptorOffset + index, animation.Flags, pose );
			else
			{
				if ( block < 1 || block >= _header.NumAnimBlocks || index < 0 )
					throw new InvalidDataException( "Invalid reference animation block." );
				var start = data.Int( _header.AnimBlockOffset + block * 8L );
				var end = data.Int( _header.AnimBlockOffset + block * 8L + 4 );
				var path = ReadOptionalRelativeString( stream, 0, _header.AnimBlockNameOffset ).Replace( '\\', '/' );
				if ( string.IsNullOrWhiteSpace( path ) || path.StartsWith( '/' ) || path.Contains( ':' )
					|| path.Split( '/' ).Any( part => part == ".." ) )
					throw new InvalidDataException( "Invalid reference animation file path." );
				using var external = openFile( path );
				using var externalData = new ReferencePoseData( external, start, end );
				ReadReferenceBones( externalData, index, animation.Flags, pose );
			}
		}
		for ( var i = 0; i < pose.Length; i++ )
		{
			var first = pose[i];
			var length = first.Rotation.x * first.Rotation.x + first.Rotation.y * first.Rotation.y
				+ first.Rotation.z * first.Rotation.z + first.Rotation.w * first.Rotation.w;
			if ( !float.IsFinite( first.Position.x ) || !float.IsFinite( first.Position.y ) || !float.IsFinite( first.Position.z )
				|| !float.IsFinite( length ) || length < 1e-12f )
				throw new InvalidDataException( "Non-finite or degenerate reference pose." );
			pose[i].Rotation = first.Rotation.Normal;
		}
		return pose;
	}

	private void ReadReferenceBones( ReferencePoseData data, long offset, int animationFlags, Transform[] pose )
	{
		if ( (animationFlags & 0x0040) != 0 )
		{
			ReadReferenceFrame( data, offset, pose );
			return;
		}
		var previous = -1;
		while ( true )
		{
			var index = data.Byte( offset );
			var flags = data.Byte( offset + 1 );
			var next = data.Short( offset + 2 );
			if ( index == 255 && flags == 0 && next == 0 )
				break;
			if ( index >= BoneCount || index <= previous || next < 0 || (next != 0 && next < 4) )
				throw new InvalidDataException( "Invalid reference animation bone record." );
			if ( (flags & ~0x3f) != 0 || (flags & 5) == 5
				|| ((flags & 2) != 0 && (flags & 0x28) != 0) || (flags & 0x28) == 0x28 )
				throw new InvalidDataException( "Invalid reference animation channel flags." );
			if ( (flags & 0x10) != 0 )
				throw new NotSupportedException( "The reference bone track is additive." );
			var bone = Bones[index];
			var position = bone.Position;
			var rotation = bone.Quaternion;
			var payload = offset + 4;
			if ( (flags & 2) != 0 ) rotation = data.Quaternion48( payload );
			if ( (flags & 0x20) != 0 ) rotation = data.Quaternion64( payload );
			if ( (flags & 1) != 0 )
				position = data.Vector48( payload + ((flags & 2) != 0 ? 6 : (flags & 0x20) != 0 ? 8 : 0) );
			if ( (flags & 4) != 0 )
				position += data.Channels( payload + ((flags & 8) != 0 ? 6 : 0) ) * bone.PositionScale;
			if ( (flags & 8) != 0 )
			{
				var angles = bone.Rotation + data.Channels( payload ) * bone.RotationScale;
				rotation = Rotation.From( new Angles( angles.y.RadianToDegree(), angles.z.RadianToDegree(), angles.x.RadianToDegree() ) );
			}
			pose[index] = new Transform( position, rotation );
			if ( next == 0 )
				break;
			previous = index;
			offset += next;
		}
	}

	private void ReadReferenceFrame( ReferencePoseData data, long offset, Transform[] pose )
	{
		var constants = data.Int( offset );
		var frames = data.Int( offset + 4 );
		var stride = data.Int( offset + 8 );
		var constantSize = 0;
		var frameSize = 0;
		for ( var i = 0; i < BoneCount; i++ )
		{
			var flags = data.Byte( offset + 24 + i );
			var positionFlags = flags & 0x35;
			var rotationFlags = flags & 0xca;
			if ( (positionFlags & (positionFlags - 1)) != 0 || (rotationFlags & (rotationFlags - 1)) != 0 )
				throw new InvalidDataException( "Conflicting reference frame channels." );
			if ( (flags & 0x88) != 0 ) frameSize += 6;
			if ( (flags & 4) != 0 ) frameSize += 6;
			if ( (flags & 0x10) != 0 ) frameSize += 12;
			if ( (flags & 1) != 0 ) constantSize += 6;
			if ( (flags & 0x42) != 0 ) constantSize += 6;
			if ( (flags & 0x20) != 0 ) constantSize += 12;
		}
		if ( stride < frameSize || constants < 0 || frames < 0
			|| (constantSize > 0 && constants < 24 + BoneCount)
			|| (frameSize > 0 && frames < 24 + BoneCount) )
			throw new InvalidDataException( "Invalid reference frame offsets or stride." );
		var constant = offset + constants;
		var frame = offset + frames;
		for ( var i = 0; i < BoneCount; i++ )
		{
			var flags = data.Byte( offset + 24 + i );
			var position = Bones[i].Position;
			var rotation = Bones[i].Quaternion;
			if ( (flags & 0x40) != 0 ) { rotation = data.Quaternion48S( constant ); constant += 6; }
			if ( (flags & 0x20) != 0 ) { position = data.Vector96( constant ); constant += 12; }
			if ( (flags & 2) != 0 ) { rotation = data.Quaternion48( constant ); constant += 6; }
			if ( (flags & 1) != 0 ) { position = data.Vector48( constant ); constant += 6; }
			if ( (flags & 0x88) != 0 )
			{
				rotation = (flags & 0x80) != 0 ? data.Quaternion48S( frame ) : data.Quaternion48( frame );
				frame += 6;
			}
			if ( (flags & 0x14) != 0 )
			{
				position = (flags & 0x10) != 0 ? data.Vector96( frame ) : data.Vector48( frame );
				frame += (flags & 0x10) != 0 ? 12 : 6;
			}
			pose[i] = new Transform( position, rotation );
		}
	}

	private sealed class ReferencePoseData : IDisposable
	{
		private readonly BinaryReader reader;
		private readonly long start;
		private readonly long length;

		public ReferencePoseData( Stream stream, long start, long end )
		{
			if ( !stream.CanSeek || start < 0 || end < start || end > stream.Length )
				throw new InvalidDataException( "Invalid reference pose block extent." );
			reader = new BinaryReader( stream, System.Text.Encoding.UTF8, leaveOpen: true );
			this.start = start;
			length = end - start;
		}

		public void Dispose() => reader.Dispose();
		private void Seek( long offset, int size )
		{
			if ( offset < 0 || offset > length || size > length - offset )
				throw new InvalidDataException( "Reference pose read exceeds its MDL/ANI block." );
			reader.BaseStream.Position = start + offset;
		}
		public byte Byte( long offset ) { Seek( offset, 1 ); return reader.ReadByte(); }
		public short Short( long offset ) { Seek( offset, 2 ); return reader.ReadInt16(); }
		public int Int( long offset ) { Seek( offset, 4 ); return reader.ReadInt32(); }
		public float Float( long offset )
		{
			var value = BitConverter.Int32BitsToSingle( Int( offset ) );
			return float.IsFinite( value ) ? value : throw new InvalidDataException( "Non-finite reference pose value." );
		}
		public Vector3 Vector48( long offset ) => new(
			(float)BitConverter.UInt16BitsToHalf( (ushort)Short( offset ) ),
			(float)BitConverter.UInt16BitsToHalf( (ushort)Short( offset + 2 ) ),
			(float)BitConverter.UInt16BitsToHalf( (ushort)Short( offset + 4 ) ) );
		public Vector3 Vector96( long offset ) => new( Float( offset ), Float( offset + 4 ), Float( offset + 8 ) );
		public Vector3 Channels( long offset ) => new( Channel( offset, 0 ), Channel( offset, 1 ), Channel( offset, 2 ) );
		private float Channel( long offset, int axis )
		{
			var relative = Short( offset + axis * 2 );
			if ( relative == 0 ) return 0;
			if ( relative < 6 )
				throw new InvalidDataException( "Invalid reference pose RLE channel pointer." );
			var run = offset + relative;
			var valid = Byte( run );
			var total = Byte( run + 1 );
			if ( valid == 0 || total == 0 || valid > total )
				throw new InvalidDataException( "Invalid reference pose RLE run." );
			return Short( run + 2 );
		}
		public Rotation Quaternion48( long offset )
		{
			var z = (ushort)Short( offset + 4 );
			return Quaternion( ((ushort)Short( offset ) - 32768) / 32768f,
				((ushort)Short( offset + 2 ) - 32768) / 32768f, ((z & 32767) - 16384) / 16384f, (z & 32768) != 0, 0.0002f );
		}
		public Rotation Quaternion64( long offset )
		{
			Seek( offset, 8 );
			var bits = reader.ReadUInt64();
			return Quaternion( ((int)(bits & 0x1fffff) - 1048576) / 1048576.5f,
				((int)((bits >> 21) & 0x1fffff) - 1048576) / 1048576.5f,
				((int)((bits >> 42) & 0x1fffff) - 1048576) / 1048576.5f, (bits >> 63) != 0, 0.000003f );
		}
		public Rotation Quaternion48S( long offset )
		{
			var a = (ushort)Short( offset );
			var b = (ushort)Short( offset + 2 );
			var c = (ushort)Short( offset + 4 );
			var q = Quaternion( ((a & 0x7fff) - 16384) / 23168f, ((b & 0x7fff) - 16384) / 23168f,
				((c & 0x7fff) - 16384) / 23168f, (c & 0x8000) != 0, 0.0002f );
			return ((a >> 14) & 2 | b >> 15) switch
			{
				0 => q,
				1 => new Rotation( q.w, q.x, q.y, q.z ),
				2 => new Rotation( q.z, q.w, q.x, q.y ),
				_ => new Rotation( q.y, q.z, q.w, q.x )
			};
		}
		private static Rotation Quaternion( float x, float y, float z, bool negativeW, float tolerance )
		{
			var w2 = 1 - x * x - y * y - z * z;
			if ( w2 < -tolerance )
				throw new InvalidDataException( "Invalid packed reference pose quaternion." );
			var w = MathF.Sqrt( MathF.Max( 0, w2 ) );
			return new Rotation( x, y, z, negativeW ? -w : w );
		}
	}
}
