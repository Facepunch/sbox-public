using System;

namespace GoldSrc.Mdl;

readonly struct Header( BinaryReader reader )
{
	public readonly int Id = reader.ReadInt32();
	public readonly int Version = reader.ReadInt32();
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 64 );
	public readonly int Length = reader.ReadInt32();
	public readonly Vector3 Eyeposition = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3 Min = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3 Max = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3 BoundsMin = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3 BoundsMax = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly int Flags = reader.ReadInt32();
	public readonly int NumBones = reader.ReadInt32();
	public readonly int BoneIndex = reader.ReadInt32();
	public readonly int NumBoneControllers = reader.ReadInt32();
	public readonly int BoneControllerIndex = reader.ReadInt32();
	public readonly int NumHitBoxes = reader.ReadInt32();
	public readonly int HitBoxIndex = reader.ReadInt32();
	public readonly int NumSeq = reader.ReadInt32();
	public readonly int SeqIndex = reader.ReadInt32();
	public readonly int NumSeqGroups = reader.ReadInt32();
	public readonly int SeqGroupIndex = reader.ReadInt32();
	public readonly int NumTextures = reader.ReadInt32();
	public readonly int TextureIndex = reader.ReadInt32();
	public readonly int TextureDataIndex = reader.ReadInt32();
	public readonly int NumSkinRef = reader.ReadInt32();
	public readonly int NumSkinFamilies = reader.ReadInt32();
	public readonly int SkinIndex = reader.ReadInt32();
	public readonly int NumBodyParts = reader.ReadInt32();
	public readonly int BodyPartIndex = reader.ReadInt32();
	public readonly int NumAttachments = reader.ReadInt32();
	public readonly int AttachmentIndex = reader.ReadInt32();
	public readonly int SoundTable = reader.ReadInt32();
	public readonly int SoundIndex = reader.ReadInt32();
	public readonly int SoundGroups = reader.ReadInt32();
	public readonly int SoundGroupIndex = reader.ReadInt32();
	public readonly int NumTransitions = reader.ReadInt32();
	public readonly int TransitionIndex = reader.ReadInt32();
}

public readonly struct Model( BinaryReader reader )
{
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 64 );
	public readonly int Type = reader.ReadInt32();
	public readonly float BoundingRadius = reader.ReadSingle();
	public readonly int NumMesh = reader.ReadInt32();
	public readonly int MeshIndex = reader.ReadInt32();
	public readonly int NumVerts = reader.ReadInt32();
	public readonly int VertInfoIndex = reader.ReadInt32();
	public readonly int VertIndex = reader.ReadInt32();
	public readonly int NumNorms = reader.ReadInt32();
	public readonly int NormInfoIndex = reader.ReadInt32();
	public readonly int NormIndex = reader.ReadInt32();
	public readonly int NumGroups = reader.ReadInt32();
	public readonly int GroupIndex = reader.ReadInt32();
}

public readonly struct Mesh( BinaryReader reader )
{
	public readonly int NumTris = reader.ReadInt32();
	public readonly int TriIndex = reader.ReadInt32();
	public readonly int SkinRef = reader.ReadInt32();
	public readonly int NumNorms = reader.ReadInt32();
	public readonly int NormIndex = reader.ReadInt32();
}

public readonly struct TriVert( BinaryReader reader )
{
	public readonly short VertIndex = reader.ReadInt16();
	public readonly short NormIndex = reader.ReadInt16();
	public readonly short S = reader.ReadInt16();
	public readonly short T = reader.ReadInt16();
}

public readonly struct BodyParts( BinaryReader reader )
{
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 64 );
	public readonly int NumModels = reader.ReadInt32();
	public readonly int BaseIndex = reader.ReadInt32();
	public readonly int ModelIndex = reader.ReadInt32();
}

public readonly struct TextureData( BinaryReader reader )
{
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 64 );
	public readonly LightingFlags Flags = (LightingFlags)reader.ReadInt32();
	public readonly int Width = reader.ReadInt32();
	public readonly int Height = reader.ReadInt32();
	public readonly int Index = reader.ReadInt32();
}

public readonly struct Attachment( BinaryReader reader )
{
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 32 );
	public readonly int Type = reader.ReadInt32();
	public readonly int Bone = reader.ReadInt32();
	public readonly Vector3 Origin = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3[] Vectors =
	[
		new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
		new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
		new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() )
	];
}

public readonly struct Bone( BinaryReader reader )
{
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 32 );
	public readonly int Parent = reader.ReadInt32();
	public readonly int Flags = reader.ReadInt32();
	public readonly int[] BoneController = [reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()];
	public readonly float[] Value = [reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()];
	public readonly float[] Scale = [reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()];
	public readonly Vector3 Position => new( Value[0], Value[1], Value[2] );
	public readonly Vector3 PositionScale => new( Scale[0], Scale[1], Scale[2] );
	public readonly Vector3 Rotation => new( Value[3], Value[4], Value[5] );
	public readonly Vector3 RotationScale => new( Scale[3], Scale[4], Scale[5] );
}

public readonly struct BoneController( BinaryReader reader )
{
	public readonly int Bone = reader.ReadInt32();
	public readonly int Type = reader.ReadInt32();
	public readonly float Start = reader.ReadSingle();
	public readonly float End = reader.ReadSingle();
	public readonly int Rest = reader.ReadInt32();
	public readonly int Index = reader.ReadInt32();
}

public readonly struct BoundingBox( BinaryReader reader )
{
	public readonly int Bone = reader.ReadInt32();
	public readonly int Group = reader.ReadInt32();
	public readonly Vector3 Min = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3 Max = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
}

public readonly struct SequenceGroup( BinaryReader reader )
{
	public readonly string Label => File.GetNullTerminatedString( LabelBytes );
	private readonly byte[] LabelBytes = reader.ReadBytes( 32 );
	public readonly string Name => File.GetNullTerminatedString( NameBytes );
	private readonly byte[] NameBytes = reader.ReadBytes( 64 );
	public readonly int Unused1 = reader.ReadInt32();
	public readonly int Unused2 = reader.ReadInt32();
}

public readonly struct SequenceDesc( BinaryReader reader )
{
	public readonly string Label => File.GetNullTerminatedString( LabelBytes );
	private readonly byte[] LabelBytes = reader.ReadBytes( 32 );
	public readonly float Fps = reader.ReadSingle();
	public readonly int Flags = reader.ReadInt32();
	public readonly int Activity = reader.ReadInt32();
	public readonly int ActWeight = reader.ReadInt32();
	public readonly int NumEvents = reader.ReadInt32();
	public readonly int EventIndex = reader.ReadInt32();
	public readonly int NumFrames = reader.ReadInt32();
	public readonly int NumPivots = reader.ReadInt32();
	public readonly int PivotIndex = reader.ReadInt32();
	public readonly MotionFlags MotionType = (MotionFlags)reader.ReadInt32();
	public readonly int MotionBone = reader.ReadInt32();
	public readonly Vector3 LinearMovement = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly int AutoMovePosIndex = reader.ReadInt32();
	public readonly int AutoMoveAngleIndex = reader.ReadInt32();
	public readonly Vector3 Min = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly Vector3 Max = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly int NumBlends = reader.ReadInt32();
	public readonly int AnimIndex = reader.ReadInt32();
	public readonly int[] BlendType = [reader.ReadInt32(), reader.ReadInt32()];
	public readonly float[] BlendStart = [reader.ReadSingle(), reader.ReadSingle()];
	public readonly float[] BlendEnd = [reader.ReadSingle(), reader.ReadSingle()];
	public readonly int BlendParent = reader.ReadInt32();
	public readonly int SeqGroup = reader.ReadInt32();
	public readonly int EntryNode = reader.ReadInt32();
	public readonly int ExitNode = reader.ReadInt32();
	public readonly int NodeFlags = reader.ReadInt32();
	public readonly int NextSeq = reader.ReadInt32();
}

public readonly struct Event( BinaryReader reader )
{
	public readonly int Frame = reader.ReadInt32();
	public readonly int Index = reader.ReadInt32();
	public readonly int Type = reader.ReadInt32();
	public readonly string Options => File.GetNullTerminatedString( OptionsBytes );
	private readonly byte[] OptionsBytes = reader.ReadBytes( 64 );
}

public readonly struct Pivot( BinaryReader reader )
{
	public readonly Vector3 Origin = new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	public readonly int Start = reader.ReadInt32();
	public readonly int End = reader.ReadInt32();
}

public readonly struct Anim( BinaryReader reader )
{
	public readonly ushort[] PositionOffsets = [reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16()];
	public readonly ushort[] RotationOffsets = [reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16()];
}

public readonly struct AnimValue( BinaryReader reader )
{
	public readonly byte Valid = reader.ReadByte();
	public readonly byte Total = reader.ReadByte();
	public readonly short Value => BitConverter.ToInt16( [Valid, Total] );
}

[Flags]
public enum LightingFlags : int
{
	FlatShade = 0x0001,
	Chrome = 0x0002,
	FullBright = 0x0004,
	NoMips = 0x0008,
	Alpha = 0x0010,
	Additive = 0x0020,
	Masked = 0x0040,
	RenderFlags = Chrome | Additive | Masked | FullBright
}

[Flags]
public enum MotionFlags : int
{
	X = 0x0001,
	Y = 0x0002,
	Z = 0x0004,
	XR = 0x0008,
	YR = 0x0010,
	ZR = 0x0020,
	LX = 0x0040,
	LY = 0x0080,
	LZ = 0x0100,
	AX = 0x0200,
	AY = 0x0400,
	AZ = 0x0800,
	AXR = 0x1000,
	AYR = 0x2000,
	AZR = 0x4000,
	Types = 0x7FFF,
	ControlFirst = X,
	ControlLast = AZR,
	RLoop = 0x8000
}

public enum SequenceFlags
{
	Looping = 0x0001
}

public enum BoneFlags
{
	HasNormals = 0x0001,
	HasVertices = 0x0002,
	HasBounds = 0x0004,
	HasChrome = 0x0008
}
