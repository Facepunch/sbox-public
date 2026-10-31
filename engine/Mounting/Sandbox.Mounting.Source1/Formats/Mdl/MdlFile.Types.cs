using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct Header
	{
		public int Id;
		public int Version;
		public int Checksum;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 64 )]
		public byte[] _name;

		public int Length;

		public Vector3 EyePosition;
		public Vector3 IllumPosition;
		public Vector3 HullMin;
		public Vector3 HullMax;
		public Vector3 ViewBbMin;
		public Vector3 ViewBbMax;

		public int Flags;

		public int NumBones;
		public int BoneIndex;

		public int NumBoneControllers;
		public int BoneControllerIndex;

		public int NumHitBoxSets;
		public int HitBoxSetIndex;

		public int NumLocalAnims;
		public int LocalAnimIndex;

		public int NumLocalSeq;
		public int LocalSeqIndex;

		public int ActivityListVersion;
		public int EventsIndexed;

		public int NumTextures;
		public int TextureIndex;

		public int NumCdTextures;
		public int CdTextureIndex;

		public int NumSkinRef;
		public int NumSkinFamilies;
		public int SkinIndex;

		public int NumBodyParts;
		public int BodyPartIndex;

		public int NumLocalAttachments;
		public int LocalAttachmentIndex;

		public int NumLocalNodes;
		public int LocalNodeIndex;
		public int LocalNodeNameIndex;

		public int NumFlexDesc;
		public int FlexDescOffset;

		public int NumFlexControllers;
		public int FlexControllerOffset;

		public int NumFlexRules;
		public int FlexRuleOffset;

		public int NumIkChains;
		public int IkChainOffset;

		public int NumMouths;
		public int MouthOffset;

		public int NumLocalPoseParams;
		public int LocalPoseParamOffset;

		public int SurfacePropOffset;

		public int KeyValueOffset;
		public int KeyValueSize;

		public int NumLocalIkAutoplayLocks;
		public int LocalIkAutoplayLockOffset;

		public float Mass;
		public int Contents;

		public int NumIncludeModels;
		public int IncludeModelOffset;
		public int VirtualModel;

		public int AnimBlockNameOffset;
		public int NumAnimBlocks;
		public int AnimBlockOffset;
		public int AnimBlockModel;

		public int BoneTableByNameOffset;

		public int VertexBase;
		public int IndexBase;

		public byte ConstDirectionLightDot;
		public byte RootLod;
		public byte NumAllowedRootLods;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 1 )]
		public byte[] _unused;

		public int _unused4;

		public int NumFlexControllerUi;
		public int FlexControllerUiOffset;

		public float VertAnimFixedPointScale;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 1 )]
		public int[] _unused3;

		public int StudioHdr2Offset;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 1 )]
		public int[] _unused2;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioTexture
	{
		public int NameIndex;
		public int Flags;
		public int Used;

		private int _unused0;

		public int MaterialPtr;
		public int ClientMaterialPtr;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 10 )]
		public int[] _unused;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioBodyPart
	{
		public int NameIndex;
		public int NumModels;
		public int Base;
		public int ModelIndex;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioModel
	{
		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 64 )]
		internal byte[] _name;

		public int Type;
		public float BoundingRadius;
		public int NumMeshes;
		public int MeshIndex;

		public int NumVertices;
		public int VertexIndex;
		public int TangentsIndex;

		public int NumAttachments;
		public int AttachmentIndex;

		public int NumEyeBalls;
		public int EyeBallIndex;

		public StudioModelVertexData VertexData;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 32 )]
		public byte[] _unused;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 8 )]
	public struct StudioModelVertexData
	{

	}

	[StructLayout( LayoutKind.Sequential, Pack = 1, Size = 172 )]
	public struct StudioEyeball
	{
		private int _nameIndex;
		public int Bone;
		public Vector3 Origin;
		public float ZOffset;
		public float Radius;
		public Vector3 Up;
		public Vector3 Forward;
		private int _texture;
		private int _unused;
		public float IrisScale;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioMesh
	{
		public int Material;
		public int ModelIndex;
		public int NumVertices;
		public int VertexOffset;
		public int NumFlexes;
		public int FlexIndex;
		public int MaterialType;
		public int MaterialParam;
		public int MeshId;
		public Vector3 Center;
		public StudioMeshVertexData VertexData;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 8 )]
		private int[] _unused;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioMeshVertexData
	{
		private int _modelVertexData;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 8 )]
		public int[] _numLodVertices;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioBone
	{
		public int NameIndex;
		public int Parent;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 6 )]
		public int[] BoneController;

		public Vector3 Position;
		/// <summary>The stored local bind quaternion, copied verbatim without renormalization.</summary>
		public Rotation Quaternion;
		/// <summary>Local bind RadianEuler (X roll, Y pitch, Z yaw), used as the base for animated rotation channels.</summary>
		public Vector3 Rotation;
		public Vector3 PositionScale;
		public Vector3 RotationScale;
		public Matrix3x4 PoseToBone;
		public Rotation Alignment;
		public int Flags;
		public int ProcType;
		public int ProcIndex;
		public int PhysicsBone;
		public int SurfacePropIndex;
		public int Contents;
		public int SurfacePropLookup;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 7 )]
		private int[] _unused;
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct Matrix3x4
	{
		public float M11, M12, M13, M14;
		public float M21, M22, M23, M24;
		public float M31, M32, M33, M34;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioAttachment
	{
		public int NameIndex;
		public int Flags;
		public int LocalBone;
		public Matrix3x4 Local;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 8 )]
		private int[] _unused;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioAnimDesc
	{
		public int BaseIndex;
		public int NameIndex;
		public float Fps;
		public int Flags;
		public int NumFrames;
		public int NumMovements;
		public int MovementIndex;
		public int IkRuleZeroFrameIndex;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 5 )]
		public int[] _unused1;

		public int AnimBlock;
		public int AnimIndex;
		public int NumIkRules;
		public int IkRuleIndex;
		public int AnimBlockIkRuleIndex;
		public int NumLocalHierarchy;
		public int LocalHierarchyIndex;
		public int SectionIndex;
		public int SectionFrames;
		public short ZeroFrameSpan;
		public short ZeroFrameCount;
		public int ZeroFrameIndex;
		public float ZeroFrameStallTime;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioSeqDesc
	{
		public int BasePtr;
		public int LabelIndex;
		public int ActivityNameIndex;
		public int Flags;
		public int Activity;
		public int ActWeight;
		public int NumEvents;
		public int EventIndex;
		public Vector3 BoundingBoxMin;
		public Vector3 BoundingBoxMax;
		public int NumBlends;
		public int AnimIndexIndex;
		public int MovementIndex;

		public int GroupSize;
		public int GroupSize2;
		public int ParamIndex;
		public int ParamIndex2;
		public float ParamStart;
		public float ParamStart2;
		public float ParamEnd;
		public float ParamEnd2;

		public int ParamParent;
		public float FadeInTime;
		public float FadeOutTime;
		public int LocalEntryNode;
		public int LocalExitNode;
		public int NodeFlags;
		public float EntryPhase;
		public float ExitPhase;
		public float LastFrame;
		public int NextSeq;
		public int Pose;
		public int NumIkRules;
		public int NumAutoLayers;
		public int AutoLayerIndex;
		public int WeightListIndex;
		public int PoseKeyIindex;
		public int NumIkLocks;
		public int IkLockIndex;
		public int KeyValueIndex;
		public int KeyValueSize;
		public int CyclePoseIndex;
		public int ActivityModifierIndex;
		public int NumActivityModifiers;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 5 )]
		public int[] _unused;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioFlexDesc
	{
		public int FACSindex;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioFlex
	{
		public int FlexDesc;
		public float Target0;
		public float Target1;
		public float Target2;
		public float Target3;
		public int NumVerts;
		public int VertIndex;
		public int FlexPair;
		public StudioVertAnimType VertAnimType;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 3 )]
		public byte[] _unsued;

		[MarshalAs( UnmanagedType.ByValArray, SizeConst = 6 )]
		public int[] _unsued1;
	}

	public enum StudioVertAnimType : byte
	{
		Normal = 0,
		Wrinkle = 1,
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	public struct StudioVertAnim
	{
		public ushort Index;
		public byte Speed;
		public byte Side;

		public short DeltaX;
		public short DeltaY;
		public short DeltaZ;
		public short NormalDeltaX;
		public short NormalDeltaY;
		public short NormalDeltaZ;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	private struct StudioVertAnimWrinkle
	{
		public StudioVertAnim Vertex;
		public short WrinkleDelta;
	}

	[Flags]
	public enum StudioAnimType : byte
	{
		RawPos = 0x01,
		RawRot = 0x02,
		AnimPos = 0x04,
		AnimRot = 0x08,
		Delta = 0x10,
		RawRot2 = 0x20,
	}

}
