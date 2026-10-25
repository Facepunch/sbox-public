using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// Per-view analytic fog buffer, matching FogConstants in common/classes/Fog.hlsl.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct FogConstants
{
	internal static readonly StringToken BufferName = new( "FogConstants" );

	internal Vector4 Control;
	internal Vector4 Distance;
	internal Vector4 Height;
	internal Vector3 Color;
	internal float OpacityScale;
	internal Vector4 HeightColor;
	internal Vector2 LayerParams;
	internal Vector2 Sky;
	internal Vector4 SkyBlurRamp;
	internal Vector2 Sun;
	internal Vector2 HeightSky;
	internal Vector4 SunSettings;
	internal Vector2 SkyFormat;
	internal Vector2 HeightSkyFormat;
	internal int SkyTextureIndex;
	internal int HeightSkyTextureIndex;
	internal float HeightSkyShared;
	internal float Padding;
	internal Matrix SkyTransform;
	internal Matrix HeightSkyTransform;
}
