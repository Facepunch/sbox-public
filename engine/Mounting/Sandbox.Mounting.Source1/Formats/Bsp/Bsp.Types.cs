using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspPlane
{
	public Vector3 Normal;
	public float Dist;
	public int Type;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspEdge
{
	public ushort V0, V1;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspFace
{
	public ushort PlaneNum;
	public byte Side, OnNode;
	public int FirstEdge;
	public short NumEdges, TexInfo, DispInfo, SurfaceFogVolumeID;
	public byte Style0, Style1, Style2, Style3;
	public int LightOffset;
	public float Area;
	public int LightmapMinsS, LightmapMinsT;
	public int LightmapSizeS, LightmapSizeT;
	public int OrigFace;
	public ushort NumPrims, FirstPrimID;
	public uint SmoothingGroups;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspTexInfo
{
	public Vector4 TextureVecsS, TextureVecsT;
	public Vector4 LightmapVecsS, LightmapVecsT;
	public int Flags, TexData;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspTexData
{
	public Vector3 Reflectivity;
	public int NameStringTableID;
	public int Width, Height;
	public int ViewWidth, ViewHeight;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspModel
{
	public Vector3 Mins, Maxs, Origin;
	public int HeadNode;
	public int FirstFace, FaceCount;
}

internal struct BspDispInfo
{
	public Vector3 StartPosition;
	public int DispVertStart;
	public int DispTriStart;
	public int Power;
	public int Flags;
	public int Contents;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct BspDispVert
{
	public Vector3 Vector;
	public float Dist;
	public float Alpha;
}
