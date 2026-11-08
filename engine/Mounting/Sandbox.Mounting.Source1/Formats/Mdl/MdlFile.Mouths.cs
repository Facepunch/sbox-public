using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioMouth
	{
		public int Bone;
		public Vector3 Forward;
		public int FlexDesc;
	}

	private readonly StudioMouth[] _mouths;

	public int MouthCount => _mouths.Length;

	public StudioMouth GetMouth( int index )
	{
		if ( (uint)index >= _mouths.Length )
			throw new ArgumentOutOfRangeException( nameof( index ) );
		return _mouths[index];
	}

	private StudioMouth[] ReadMouths( Stream stream )
	{
		ValidateRange<StudioMouth>( stream, _header.MouthOffset, _header.NumMouths );
		stream.Position = _header.MouthOffset;
		var mouths = LumpReader<StudioMouth>.ReadLumpFromStream( stream, _header.NumMouths );
		foreach ( var mouth in mouths )
		{
			if ( mouth.Bone < 0 || mouth.Bone >= _header.NumBones )
				throw new InvalidDataException( "MDL mouth references an invalid bone." );
			if ( mouth.FlexDesc < 0 || mouth.FlexDesc >= _header.NumFlexDesc )
				throw new InvalidDataException( "MDL mouth references an invalid flex descriptor." );
			if ( !IsFinite( mouth.Forward ) || !float.IsFinite( mouth.Forward.LengthSquared ) || mouth.Forward.LengthSquared < 1e-12f )
				throw new InvalidDataException( "MDL mouth has an invalid forward vector." );
		}
		return mouths;
	}
}
