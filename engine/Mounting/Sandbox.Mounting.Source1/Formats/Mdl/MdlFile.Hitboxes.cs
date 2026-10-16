using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	public readonly record struct StudioHitbox( string Name, int Bone, int Group, Vector3 Min, Vector3 Max );
	public readonly record struct StudioHitboxSet( string Name, IReadOnlyList<StudioHitbox> Hitboxes );

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	private struct HitboxSetHeader
	{
		public int NameIndex;
		public int NumHitboxes;
		public int HitboxIndex;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 68 )]
	private struct HitboxHeader
	{
		public int Bone;
		public int Group;
		public Vector3 Min;
		public Vector3 Max;
		public int NameIndex;
	}

	private readonly StudioHitboxSet[] _hitboxSets;

	public int HitboxSetCount => _hitboxSets.Length;
	public bool HasAmbiguousHitboxNames { get; private set; }
	public StudioHitboxSet GetHitboxSet( int index ) => _hitboxSets[index];

	private StudioHitboxSet[] ReadHitboxSets( Stream stream )
	{
		ValidateRange<HitboxSetHeader>( stream, _header.HitBoxSetIndex, _header.NumHitBoxSets );
		stream.Position = _header.HitBoxSetIndex;
		var sets = new StudioHitboxSet[_header.NumHitBoxSets];
		LumpReader<HitboxSetHeader>.ReadLumpFromStream( stream, sets.Length, ( setIndex, header ) =>
		{
			var start = stream.Position;
			var name = ReadOptionalRelativeString( stream, start, header.NameIndex );
			var hitboxOffset = start + header.HitboxIndex;
			ValidateRange<HitboxHeader>( stream, hitboxOffset, header.NumHitboxes );
			stream.Position = hitboxOffset;
			var hitboxes = new StudioHitbox[header.NumHitboxes];
			LumpReader<HitboxHeader>.ReadLumpFromStream( stream, hitboxes.Length, ( index, hitbox ) =>
			{
				if ( hitbox.Bone < 0 || hitbox.Bone >= _header.NumBones )
					throw new InvalidDataException( "MDL hitbox references an invalid bone." );
				if ( !IsFinite( hitbox.Min ) || !IsFinite( hitbox.Max )
					|| hitbox.Min.x > hitbox.Max.x || hitbox.Min.y > hitbox.Max.y || hitbox.Min.z > hitbox.Max.z )
					throw new InvalidDataException( "MDL hitbox bounds are non-finite or inverted." );
				HasAmbiguousHitboxNames |= Version == 44 && hitbox.NameIndex != 0;
				var hitboxName = Version == 44 ? string.Empty : ReadOptionalRelativeString( stream, stream.Position, hitbox.NameIndex );
				hitboxes[index] = new StudioHitbox( hitboxName, hitbox.Bone, hitbox.Group, hitbox.Min, hitbox.Max );
			} );
			sets[setIndex] = new StudioHitboxSet( name, Array.AsReadOnly( hitboxes ) );
		} );
		return sets;
	}
}
