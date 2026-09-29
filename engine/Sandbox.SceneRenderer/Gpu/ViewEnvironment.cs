using System.Linq;
using System.Runtime.InteropServices;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Gpu;

/// <summary>
/// Standard-shader environment inputs beyond <see cref="ViewConstants"/> (<c>CreatePerViewConstantBuffer</c>).
/// Defaults must be explicit: zero fog-culling parameters produce full fog.
/// </summary>
internal sealed class ViewEnvironment : IDisposable
{
	static readonly StringToken ViewConstantsVR = new( "PerViewConstantBufferVR_t" );
	static readonly StringToken LightingConfig = new( "ViewLightingConfigV2" );
	static readonly StringToken DirectionalLight = new( "DirectionalLightCB" );
	static readonly StringToken CubemapFogTexture = new( "CubemapFogTexture" );
	static readonly StringToken CubemapFogColor = new( "CubemapFogColor" );

	/// <summary>
	/// Empty fallback buffers; null descriptors are not supported on every GPU.
	/// </summary>
	static readonly string[] EmptyBuffers =
	[
		"DecalsBuffer", "DecalsExtraDataBuffer",
	];

	/// <summary>
	/// Fog/wind constants: native <c>PerViewConstantBuffer_t</c>, shader <c>PerViewConstantBufferVR_t</c> (vr_common.fxc).
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct ViewConstantsVRData
	{
		public int VolumetricFogEnabled, GradientFogEnabled, CubemapFogEnabled, SphericalVignetteEnabled;
		public int AmbientOcclusionProxiesEnabled, ReflectionEnabled, Unused0, Unused1;
		public Vector4 Unused2, Unused3;
		public Vector4 WindDirection, WindStrengthFreqMulHighStrength;
		public Vector4 InteractionProjectionOrigin, InteractionVolumeInvExtents, InteractionTriggerVolumeInvMins, InteractionTriggerVolumeWorldToVolumeScale;
		public Vector4 GradientFogBiasAndScale, GradientFogExponent, GradientFogColorOpacity, GradientFogCullingParams;
		public Vector4 CubeFogOffsetScaleBiasExponent, CubeFogHeightOffsetScaleExponentLog2Mip;
		public Matrix CubeFogSkyWsToOs;
		public Vector4 CubeFogCullingParams;
		public Vector4 SphericalVignetteBiasAndScale, SphericalVignetteOriginExponent, SphericalVignetteColorOpacity;
		public Vector4 VolFogVolumeScaleShiftNearRange, VolFogDitherScaleBias, VolFogPostWorldToFrustumScale, VolFogPostWorldToFrustumBias;
		public Matrix VolFogFromWorld0, VolFogFromWorld1;
		public Vector4 HighPrecisionLightingOffsetWs;
	}

	/// <summary>
	/// Mirror of <c>cbuffer ViewLightingConfigV2</c> (common/lightbinner.hlsl).
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	unsafe struct LightingConfigData
	{
		public fixed int NumLights[4];
		public fixed int BakedLightIndexMapping[256 * 4];
		public Vector4 AmbientLightColor;
	}

	GpuBuffer<uint> emptyBuffer;

	/// <summary>
	/// Required per-view lookup tables (renderingpipeline_standard.cpp). Missing BRDF lookup produces reflection rings.
	/// </summary>
	static readonly (string Attribute, string Path)[] ViewTextures =
	[
		("BRDFLookup", "textures/dev/ggx_integrate_brdf_lut_schlick.vtex"),
		("BlueNoise", "textures/dev/blue_noise_256.vtex"),
		("LtcAmplitudeLookup", "textures/dev/ltc_amp.vtex"),
		("LtcMatrixLookup", "textures/dev/ltc_mat.vtex"),
	];

	Texture[] viewTextures;

	public unsafe void Apply( RenderContext context, SceneLighting lighting, Features.LightBinnerFeature binner, in GPUDirectionalLight sun, in ViewSpace space = default, Vector3 lightingOffset = default, VolumetricFog fog = null, RenderView view = null )
	{
		Apply( context, lighting, binner.DynamicCount, binner.BakedCount, binner.BakedIndexMapping, binner.EnvMapCount, binner.DecalCount, sun, space, lightingOffset, fog, view );
	}

	/// <summary>
	/// Main-world ambient and fog transformed into skybox space (<c>CRenderingPipelineStandard::Add3DSkyboxLayers</c>).
	/// </summary>
	public readonly struct ViewSpace
	{
		/// <summary>The lighting the ambient light and fog are taken from, or null for the view's world's own.</summary>
		public SceneLighting Lighting { get; init; }

		/// <summary>Main world units per unit of the view's world.</summary>
		public float Scale { get; init; }

		/// <summary>The height in the view's world that the main world's zero is at.</summary>
		public float OffsetZ { get; init; }

		/// <summary>
		/// Main-world fog and projection override; null uses the view's own fog (<c>Add3DSkyboxLayers</c>).
		/// </summary>
		public VolumetricFog Fog { get; init; }
		public RenderView FogView { get; init; }

		/// <summary>
		/// Main-world origin in skybox space (<c>mSkyboxToWorld</c>).
		/// </summary>
		public Vector3 Origin { get; init; }
	}

	/// <summary>
	/// Row-vector skybox-to-world matrix (<c>mSkyboxToWorld</c>, <c>Add3DSkyboxLayers</c>).
	/// </summary>
	static Matrix SkyboxToWorld( in ViewSpace space )
	{
		var translation = space.Origin * -space.Scale;
		return Matrix.CreateScale( space.Scale ) * Matrix.CreateTranslation( translation );
	}

	/// <summary>
	/// Bind lighting, fog, light-index mappings and the high-precision lighting origin.
	/// </summary>
	public unsafe void Apply( RenderContext context, SceneLighting lighting, int dynamicLights, int bakedLights, ReadOnlySpan<int> bakedIndexMapping, int envMaps, int decals, in GPUDirectionalLight sun, in ViewSpace space = default, Vector3 lightingOffset = default, VolumetricFog fog = null, RenderView view = null )
	{
		var attributes = context.Attributes;
		var viewLighting = space.Lighting ?? lighting;
		var scale = space.Lighting is null ? 1.0f : space.Scale;

		var vr = new ViewConstantsVRData();

		// Glass derives its view ray from camera-relative vPositionWithOffsetWs.
		vr.HighPrecisionLightingOffsetWs = new( lightingOffset.x, lightingOffset.y, lightingOffset.z, 0 );

		// Skyboxes inherit main-world wind.
		vr.WindDirection = viewLighting.WindDirection;
		vr.WindStrengthFreqMulHighStrength = viewLighting.WindStrengthFreq;
		SetupGradientFog( ref vr, viewLighting.GradientFog, scale, space.OffsetZ );
		var fogCube = SetupCubemapFog( ref vr, viewLighting.CubemapFog, scale, space.OffsetZ );

		// Bind fog or opaque black. Skybox positions map into the main world's fog volume.
		var skyboxFog = space.Fog is not null;
		if ( skyboxFog )
		{
			fog = space.Fog;
			view = space.FogView;
		}

		var fogged = fog is { Active: true } && view is not null;
		if ( fogged ) fog.FillShadingConstants( ref vr, view, skyboxFog ? SkyboxToWorld( space ) : null );
		attributes.Set( FogVolume, fogged ? fog.FogVolumeTexture : VolumetricFog.OpaqueBlack );
		attributes.SetCombo( VolumetricFogCombo, fogged ? 1 : 0 );

		context.SetConstants( ViewConstantsVR, vr );

		blackCube ??= Texture.Load( "dev/env_cubemap_black.vtex" );
		attributes.Set( CubemapFogTexture, fogCube ?? blackCube );
		attributes.Set( CubemapFogColor, viewLighting.CubemapFog.Tint );

		// Binned light and envmap counts, and the ambient light
		var config = new LightingConfigData();
		config.NumLights[0] = dynamicLights;
		config.NumLights[1] = bakedLights;
		config.NumLights[2] = envMaps;
		config.NumLights[3] = decals;

		// int4s, the index in x (LightIndexMapping)
		for ( int i = 0; i < bakedIndexMapping.Length && i < 256; i++ )
			config.BakedLightIndexMapping[i * 4] = bakedIndexMapping[i];
		var ambient = viewLighting.AmbientColor;
		// Alpha blends probe ambient (0) with flat ambient (1).
		config.AmbientLightColor = new( ambient.r, ambient.g, ambient.b, Math.Clamp( ambient.a, 0, 1 ) );
		context.SetConstants( LightingConfig, config );

		// The sun and its cascades, from the shadow system
		context.SetConstants( DirectionalLight, sun );

		if ( emptyBuffer is null )
		{
			// New buffers aren't cleared
			emptyBuffer = new GpuBuffer<uint>( 64, GpuBuffer.UsageFlags.Structured, "SceneRenderer empty" );
			emptyBuffer.SetData( new uint[64].AsSpan() );
		}
		foreach ( var name in EmptyBuffers )
			attributes.Set( name, emptyBuffer );

		viewTextures ??= ViewTextures.Select( x => Texture.Load( x.Path ) ).ToArray();
		for ( int i = 0; i < viewTextures.Length; i++ )
			attributes.Set( ViewTextures[i].Attribute, viewTextures[i] );

		// Black fallback; an active BloomLayer replaces this binding.
		attributes.Set( QuarterResBloomInput, Texture.Black );

		// Required sprite-sheet metadata (CSceneSystem::InitializeRenderAttributes).
		attributes.Set( SheetTexture, g_pRenderDevice.GetSheetMetaDataTexture() );

	}

	Texture blackCube;
	static readonly StringToken FogVolume = new( "FogVolume" );
	static readonly StringToken VolumetricFogCombo = new( "D_ENABLE_VOLUMETRIC_FOG" );
	static readonly StringToken QuarterResBloomInput = new( "QuarterResEffectsBloomInputTexture" );
	static readonly StringToken SheetTexture = new( "SheetTexture" );

	/// <summary>
	/// Pack gradient fog (<c>SetupGradientFog</c>), scaling distances and offsetting heights for skyboxes.
	/// Disabled fog must reject all pixels; zero culling parameters fog everything.
	/// </summary>
	internal static void SetupGradientFog( ref ViewConstantsVRData vr, in GradientFogSetup fog, float scale = 1.0f, float offsetZ = 0.0f )
	{
		if ( !fog.Enabled )
		{
			vr.GradientFogCullingParams = new( float.PositiveInfinity, float.NegativeInfinity, 0, 0 );
			return;
		}

		float start = fog.StartDistance / scale, end = fog.EndDistance / scale;
		float startHeight = fog.StartHeight / scale + offsetZ, endHeight = fog.EndHeight / scale + offsetZ;
		var color = fog.Color;

		vr.GradientFogEnabled = 1;
		vr.GradientFogBiasAndScale = new( -start / (end - start), -endHeight / (startHeight - endHeight), 1.0f / (end - start), 1.0f / (startHeight - endHeight) );
		vr.GradientFogExponent = new( fog.DistanceFalloffExponent, fog.VerticalFalloffExponent, 0, 0 );
		vr.GradientFogCullingParams = new( start * start, endHeight, 0, 0 );

		// The color's alpha scales it, as GradientFogSetup.Apply writes it
		vr.GradientFogColorOpacity = new( color.r * color.a, color.g * color.a, color.b * color.a, fog.MaximumOpacity );
	}

	/// <summary>
	/// Pack scaled/offset cubemap fog (<c>SetupCubemapFog</c>). Returns its cubemap, or null when disabled.
	/// </summary>
	static Texture SetupCubemapFog( ref ViewConstantsVRData vr, CubemapFogController fog, float scale, float offsetZ )
	{
		if ( !fog.Enabled || !RenderContext.HasData( fog.Texture ) )
		{
			vr.CubeFogCullingParams = new( float.PositiveInfinity, float.PositiveInfinity, 0, 0 );
			return null;
		}

		float start = fog.StartDistance / scale, end = fog.EndDistance / scale;

		vr.CubemapFogEnabled = 1;

		// Native reads the transform back as a VMatrix and transposes it into the buffer
		vr.CubeFogSkyWsToOs = Matrix.FromTransform( fog.Transform ).Transpose();
		vr.CubeFogOffsetScaleBiasExponent = new( -start / (end - start), 1.0f / (end - start), fog.LodBias, fog.FalloffExponent );
		vr.CubeFogCullingParams = new( start * start, float.PositiveInfinity, 0, 0 );

		// Height fog, when it has a width to fade over
		var heightWidth = fog.HeightWidth / scale;
		var heightStart = fog.HeightStart / scale + offsetZ;
		var log2Mip = MathF.Log2( fog.Texture.Width ) - 3.0f;
		if ( heightWidth > 0 )
		{
			vr.CubeFogCullingParams.y = heightStart;
			vr.CubeFogHeightOffsetScaleExponentLog2Mip = new( -heightStart / heightWidth, 1.0f / heightWidth, fog.HeightExponent, log2Mip );
		}
		else
		{
			vr.CubeFogHeightOffsetScaleExponentLog2Mip = new( 0, 0, 1, log2Mip );
		}

		return fog.Texture;
	}

	public void Dispose()
	{
		emptyBuffer?.Dispose();
		emptyBuffer = null;
	}
}
