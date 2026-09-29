using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Gpu;

/// <summary>
/// Mirrors <c>PerViewConstantBuffer_t</c> (common_shader_defs.h, common.fxc).
/// Row-vector matrices appear transposed to column-major HLSL, matching <c>mul( M, v )</c>.
/// </summary>
[StructLayout( LayoutKind.Sequential, Pack = 4 )]
internal struct ViewConstants
{
	/// <summary>
	/// Native size, validated at startup to catch shader-layout drift.
	/// </summary>
	public const int NativeSize = 1088;

	public Matrix WorldToProjection;
	public Matrix ProjectionToWorld;
	public Matrix WorldToView;
	public Matrix ViewToProjection;
	public Vector4 InvProjRow3;

	public Vector4 ClipPlane0;

	public float ToneMapScalarLinear;
	public float LightMapScalar;
	public float EnvMapScalar;
	public float ToneMapScalarGamma;

	public Vector3 CameraPositionWs;
	public float ViewportMinZ;

	public Vector3 CameraDirWs;
	public float ViewportMaxZ;

	public Vector3 CameraUpDirWs;
	public float Time;

	public Vector3 DepthPsToVsConversion;
	public float NearPlane;

	public float FarPlane;
	public float CameraFov;
	public Vector2 InvViewportSize;

	public Vector2 ViewportToGBufferRatio;
	public Vector2 MorphTextureAtlasSize;

	public Vector4 InvGBufferSize;

	public Vector2 ViewportOffset;
	public Vector2 ViewportSize;
	public Vector2 RenderTargetSize;

	public float FogBlendToBackground;
	public float HenyeyGreensteinCoeff;
	public Vector3 FogColor;
	public float NegFogStartOverFogRange;

	public float InvFogRange;
	public float FogMaxDensity;
	public float FogExponent;

	public float Mod2xIdentity;

	public Vector2 RoughnessParams;

	public int MsaaSampleCount;

	public float StereoCameraIndex;
	public Vector3 MiddleEyePositionWs;
	public float Pad2;
	public Matrix WorldToProjectionMultiview0;
	public Matrix WorldToProjectionMultiview1;
	public Vector4 CameraPositionWsMultiview0;
	public Vector4 CameraPositionWsMultiview1;

	public Vector4 FrameBufferCopyInvSizeAndUvScale;
	public Vector4 CameraAngles;
	public Vector4 WorldToCameraOffset;
	public Vector4 WorldToCameraOffsetMultiview0;
	public Vector4 WorldToCameraOffsetMultiview1;
	public Vector4 PerViewConstantExtraData0;
	public Vector4 PerViewConstantExtraData1;
	public Vector4 PerViewConstantExtraData2;
	public Vector4 PerViewConstantExtraData3;

	public Matrix PrevProjectionToWorld;
	public Matrix ViewToScreen;
	public Matrix ProjectionToView;
	public Matrix CurrFrameViewToPrevFrameProj;

	public Vector4 RandomFloats;

	internal static void ValidateLayout()
	{
		var size = Unsafe.SizeOf<ViewConstants>();
		if ( size != NativeSize )
			throw new InvalidOperationException( $"ViewConstants is {size} bytes, PerViewConstantBuffer_t is {NativeSize}" );
	}
}

/// <summary>
/// Per-pass constant cache key (<c>CreatePerLayerViewConstants</c>, <c>SetTonemapOverrideScaleValue</c>).
/// Depth range controls light-cluster lookup; negative tonemap scale uses view exposure.
/// </summary>
internal readonly record struct ViewConstantsKey( Rect Viewport, Vector2 TargetSize, int Samples, float MinZ, float MaxZ, float ToneMapScalar = -1 );
