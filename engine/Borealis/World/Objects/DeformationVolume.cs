using System.Runtime.InteropServices;

namespace Borealis;

/// <summary>
/// A deformation volume as <c>skinning_cs</c> reads it, per skinned mesh: model space into the volume and back, its size,
/// how it deforms, and its transform. Laid out as s&amp;box's <c>DeformationVolume</c>.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal record struct DeformationVolume
{
	internal Vector4 ModelToVolumeRow0;
	internal Vector4 ModelToVolumeRow1;
	internal Vector4 ModelToVolumeRow2;
	internal Vector4 VolumeToModelRow0;
	internal Vector4 VolumeToModelRow1;
	internal Vector4 VolumeToModelRow2;
	internal Vector4 SizeRadius;

	// Stretch ratio, inflation, reserved, edge transition
	internal Vector4 AmountFalloff;
	internal Vector4 WeightOperation;
	internal Vector4 CenterShape;
	internal Vector4 TransformRow0;
	internal Vector4 TransformRow1;
	internal Vector4 TransformRow2;
}
