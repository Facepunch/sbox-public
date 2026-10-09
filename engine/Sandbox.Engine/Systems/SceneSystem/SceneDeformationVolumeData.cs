using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A deformation operation packed in the same 208-byte layout as the GPU shader buffer.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal record struct SceneDeformationVolumeData
{
	internal Vector4 ModelToVolumeRow0;
	internal Vector4 ModelToVolumeRow1;
	internal Vector4 ModelToVolumeRow2;
	internal Vector4 VolumeToModelRow0;
	internal Vector4 VolumeToModelRow1;
	internal Vector4 VolumeToModelRow2;
	internal Vector4 SizeRadius;
	internal Vector4 AmountFalloff; // stretch ratio, inflation, reserved, edge transition
	internal Vector4 WeightOperation;
	internal Vector4 CenterShape;
	internal Vector4 TransformRow0;
	internal Vector4 TransformRow1;
	internal Vector4 TransformRow2;
}
