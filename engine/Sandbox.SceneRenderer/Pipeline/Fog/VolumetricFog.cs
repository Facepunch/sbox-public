using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Per-camera fog, ported from <c>CVolumetricFog</c>/<c>CVolumetricFogRenderer</c> (scenesystem/utils/volumetricfog.cpp).
/// Lights, temporally blends and integrates froxels with <c>cs_volumetric_fog.shader</c> for the next frame.
/// </summary>
internal sealed class VolumetricFog : IDisposable
{
	// Native volume_fog_* defaults; width and height are thread-group counts.
	const int ThreadGroupSize = 8;
	const int Width = 240 & ~(ThreadGroupSize - 1);
	const int Height = 160 & ~(ThreadGroupSize - 1);
	const int Depth = 64;
	const float DitherScale = 3.0f;

	const int FramesAtZeroScatteringBeforeSwitchingOff = 100;
	const int MaxVolumes = 128;

	static readonly StringToken ConstantBufferName = new( "VolumetricFogConstantBuffer_t" );
	static readonly StringToken JitterOffsetName = new( "g_nJitterOffset" );
	static readonly StringToken BakedFogCombo = new( "D_ENABLE_BAKED_FOG_TEXTURE" );
	static readonly StringToken IblIndirectCombo = new( "D_INDIRECT_LIGHTING_FROM_IBL" );
	static readonly StringToken PassCombo = new( "D_PASS" );
	static readonly StringToken InBakedFogName = new( "InBakedFogTexture" );
	static readonly StringToken InLightingName = new( "InLightingTexture" );
	static readonly StringToken InAccumulationName = new( "InAccumulationTexture" );
	static readonly StringToken OutLightingName = new( "OutLightingTexture" );
	static readonly StringToken OutAccumulationName = new( "OutAccumulationTexture" );
	static readonly StringToken OutScatteringName = new( "OutScatteringTexture" );

	/// <summary>
	/// Mirrors <c>CVolumetricFog::VolumetricFogConstantBuffer_t</c>, including native VMatrix layout.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	unsafe struct Constants
	{
		public Vector4 ViewDX, ViewDY, ViewCorner, ViewPos, ViewDir;
		public Matrix LastViewFrustum;
		public Vector4 NearFarClipPlaneReciprocal, VolumeScaleShiftNearFar, AnisotropyScatteringXYJitterScale;
		public Vector4 IndirectBBScaleStrength, IndirectBBOffset;
		public Vector4 PostWorldToFrustumScale, PostWorldToFrustumBias;
		public Vector4 ThreadToVoxelScale, ThreadToVoxelBias;
		public Vector4 FogFadeInScaleBias;
		public fixed int NumFogVolumeBoundingBoxes[4];
		public fixed float BoxMin[MaxVolumes * 4];
		public fixed float BoxMax[MaxVolumes * 4];
		public fixed float BoxColorExponent[MaxVolumes * 4];
		public fixed float BoxWorldToVolume[MaxVolumes * 16];
		public fixed uint WidthHeightDepth[4];
		public Vector4 OoWidthHeightDepth;
	}

	Constants constants;
	IntPtr constantBuffer;
	Texture lighting, fogVolume;
	readonly Texture[] accumulated = new Texture[2];
	static Texture opaqueBlack;
	Material computeShader;

	uint frameIndex;
	uint frameOffset;
	int scatteringAtZeroCount;
	bool renderingDisabled;

	float scattering, anisotropy, farClip = 1000, fadeInStart = 20, fadeInEnd = 100, indirectStrength;
	Texture bakedTexture;
	Vector3 boxMin, boxMax;

	// Last frame's world to projection, row-vector: native keeps its transpose (m_lastViewFrustum, GetViewProjTranspose)
	Matrix lastWorldToProjection;

	/// <summary>
	/// Whether fog is enabled, has volumes and remains active (<c>CRenderingPipelineStandard::AddLayersToView</c>).
	/// </summary>
	public bool Active { get; private set; }

	/// <summary>
	/// Forward fog texture, or opaque black when not scattering (<c>GetFogVolumeTexture</c>).
	/// </summary>
	public Texture FogVolumeTexture => Active && scattering != 0.0f ? fogVolume : OpaqueBlack;

	/// <summary>
	/// No-fog fallback: opaque black 2x2x2 (<c>SCENE_TXTR_OPAQUE_BLACK_2x2x2</c>).
	/// </summary>
	public static Texture OpaqueBlack => opaqueBlack ??= Texture.CreateVolume( 2, 2, 2 ).WithData( OpaqueBlackData() ).WithName( "SceneRenderer opaque black 2x2x2" ).Finish();

	static byte[] OpaqueBlackData()
	{
		var data = new byte[2 * 2 * 2 * 4];
		for ( int i = 3; i < data.Length; i += 4 ) data[i] = 255;
		return data;
	}

	/// <summary>
	/// Update settings and history, then create resources on the main thread before setup
	/// (<c>SetParams</c>, <c>FrameUpdate</c>, <c>CreateRendererSingleUse</c>). Disabling fog releases resources.
	/// </summary>
	public void BeginFrame( VolumetricFogSettings settings, bool hasVolumes )
	{
		Active = false;
		if ( !settings.Enabled )
		{
			Release();
			return;
		}

		// SetParams( SceneVolumetricFogParameters2_t ): scattering scaled into what the shader wants
		scattering = settings.Scattering * 0.0022f;
		farClip = settings.DrawDistance;
		fadeInStart = settings.FadeInStart;
		fadeInEnd = settings.FadeInEnd;
		indirectStrength = settings.IndirectStrength;
		bakedTexture = settings.BakedIndirectTexture;
		anisotropy = settings.Anisotropy;

		// FrameUpdate: a fog that has scattered nothing for 100 frames switches itself off
		renderingDisabled = false;
		frameIndex++;
		if ( scattering == 0.0f )
		{
			scatteringAtZeroCount++;
			if ( scatteringAtZeroCount > FramesAtZeroScatteringBeforeSwitchingOff ) renderingDisabled = true;
		}
		else
		{
			scatteringAtZeroCount = 0;
		}

		// Native makes a renderer only for a view with fog volumes (CCameraRenderer::AddLayersToView)
		if ( !hasVolumes ) return;

		EnsureResources();
		frameOffset = frameIndex & 1;
		Active = !renderingDisabled;
	}

	/// <summary>
	/// Create and clear volumes (<c>EnsureGPUResources</c>). Resets scattering until next frame's settings, matching native.
	/// </summary>
	void EnsureResources()
	{
		if ( fogVolume is not null ) return;

		// volume_fog_intermediate_textures_hdr is on by default
		lighting = MakeVolume( "lighting_array_fog_rt0" );
		accumulated[0] = MakeVolume( "accumulated_fog_rt0" );
		accumulated[1] = MakeVolume( "accumulated_fog_rt1" );
		fogVolume = MakeVolume( "volumetric_fog_rt0" );

		frameIndex = 0;
		scattering = 0.0f;
		anisotropy = 0.2f;
		scatteringAtZeroCount = FramesAtZeroScatteringBeforeSwitchingOff;
		renderingDisabled = false;

		lighting.Clear( Color.Transparent );
		accumulated[0].Clear( Color.Transparent );
		accumulated[1].Clear( Color.Transparent );
		fogVolume.Clear( Color.Transparent );

		computeShader ??= Material.Load( "materials/dev/cs_volumetric_fog.vmat" );
		if ( constantBuffer == IntPtr.Zero ) constantBuffer = RenderContext.CreateConstantBuffer( System.Runtime.CompilerServices.Unsafe.SizeOf<Constants>() );
	}

	static Texture MakeVolume( string name ) => Texture.CreateVolume( Width, Height, Depth, ImageFormat.RGBA16161616F ).WithUAVBinding().WithName( $"SceneRenderer {name}" ).Finish();

	void Release()
	{
		lighting?.Dispose();
		accumulated[0]?.Dispose();
		accumulated[1]?.Dispose();
		fogVolume?.Dispose();
		lighting = fogVolume = accumulated[0] = accumulated[1] = null;

		if ( constantBuffer != IntPtr.Zero ) RenderContext.DestroyConstantBuffer( constantBuffer );
		constantBuffer = IntPtr.Zero;
	}

	float NearClip => farClip / Depth;

	/// <summary>
	/// World-to-fog shading constants for <c>PerViewConstantBufferVR_t</c> (<c>GetShadingConstants</c>).
	/// </summary>
	public void FillShadingConstants( ref ViewEnvironment.ViewConstantsVRData vr, RenderView view, Matrix? fromWorld = null )
	{
		var near = NearClip;
		var clipRange = farClip - near;
		var dither = farClip * DitherScale / 600.0f;

		vr.VolumetricFogEnabled = 1;
		vr.VolFogVolumeScaleShiftNearRange = new( 1.0f / Depth, 0.0f, near, clipRange );
		vr.VolFogDitherScaleBias = new( dither, -0.5f * dither, 0.0f, 0.0f );
		vr.VolFogPostWorldToFrustumScale = new( 0.5f, 0.5f, 1.0f / clipRange, 0.0f );
		vr.VolFogPostWorldToFrustumBias = new( 0.5f, 0.5f, -near / clipRange, 0.0f );

		// GetViewProjTranspose matches row-vector layout; Z is unused, so reverse-Z works.
		// Transform skybox positions into the main world first (skyboxToWorld).
		var toFog = fromWorld is { } toWorld ? toWorld * view.WorldToProjection : view.WorldToProjection;
		vr.VolFogFromWorld0 = toFog;
		vr.VolFogFromWorld1 = toFog;
	}

	/// <summary>
	/// <c>CVolumetricFogRenderer::Render</c>: the constants for this view, then the lighting and integrate passes over the froxels.
	/// </summary>
	public void Render( RenderContext rc, RenderView view, List<FogVolume> volumes )
	{
		if ( renderingDisabled ) return;

		SetupConstants( view, volumes );

		var attributes = rc.Attributes;

		// volume_fog_jitter_offset_random is on: a random blue noise offset each frame
		attributes.Set( JitterOffsetName, Random.Shared.Next( 0, 128 ) );
		// Too big for a dynamic buffer: its own, as native's fog has (m_hFogLightingConstantBuffer)
		rc.SetConstantBuffer( ConstantBufferName, constantBuffer, constants );
		attributes.SetCombo( BakedFogCombo, bakedTexture is not null ? 1 : 0 );
		attributes.SetCombo( IblIndirectCombo, 1 );

		attributes.Set( InBakedFogName, bakedTexture ?? OpaqueBlack );
		attributes.Set( InLightingName, lighting );
		attributes.Set( InAccumulationName, accumulated[frameOffset] );
		attributes.Set( OutLightingName, lighting );
		attributes.Set( OutAccumulationName, accumulated[frameOffset ^ 1] );
		attributes.Set( OutScatteringName, fogVolume );

		for ( int pass = 0; pass < 2; pass++ )
		{
			attributes.SetCombo( PassCombo, pass );
			rc.Dispatch( computeShader, Width, Height, 1 );
		}

		attributes.SetCombo( PassCombo, 0 );
		attributes.SetCombo( BakedFogCombo, 0 );
		attributes.SetCombo( IblIndirectCombo, 0 );
	}

	/// <summary>
	/// Fill froxel rays, reprojection, depth slices, fade and up to 128 visible volumes (<c>SetupConstantBuffer</c>).
	/// </summary>
	unsafe void SetupConstants( RenderView view, List<FogVolume> volumes )
	{
		CalculateBounds( volumes );

		// Unit-depth corner rays: bottom left, bottom right, top left (ComputeScreenSpaceVertices, CFrustum::BuildRay).
		var rotation = view.Rotation;
		var forward = rotation.Forward;
		var right = rotation.Right;
		var up = rotation.Up;
		var tanX = MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
		var tanY = tanX * view.Viewport.Height / view.Viewport.Width;
		var ray0 = forward - right * tanX - up * tanY;
		var ray1 = forward + right * tanX - up * tanY;
		var ray2 = forward - right * tanX + up * tanY;

		var camera = view.Position;
		constants.ViewDX = new( (ray1 - ray0) / Width, 0.0f );
		constants.ViewDY = new( (ray2 - ray0) / Height, 0.0f );
		constants.ViewCorner = new( ray0, 0.0f );
		constants.ViewPos = new( camera, 0.0f );
		constants.ViewDir = new( forward, 0.0f );

		// Reproject camera-relative positions using the current camera offset and previous projection.
		constants.LastViewFrustum = Matrix.CreateTranslation( camera ) * lastWorldToProjection;
		lastWorldToProjection = view.WorldToProjection;

		constants.AnisotropyScatteringXYJitterScale = new( anisotropy, scattering, (ray1 - ray0).Length, (ray2 - ray0).Length );

		var near = NearClip;
		var clipRange = farClip - near;
		constants.NearFarClipPlaneReciprocal = new( near, 1.0f / clipRange, farClip, 0.0f );
		constants.PostWorldToFrustumScale = new( 0.5f, 0.5f, 1.0f / clipRange, 0.0f );
		constants.PostWorldToFrustumBias = new( 0.5f, 0.5f, -near / clipRange, 0.0f );
		constants.ThreadToVoxelScale = new( 2.0f / Width, 2.0f / Height, clipRange / Depth, -near * farClip / clipRange );
		constants.ThreadToVoxelBias = new( 1.0f / Width - 1.0f, 1.0f / Height - 1.0f, near + 0.5f * clipRange / Depth, (1.0f + (farClip + near) / clipRange) / 2.0f );

		var bbScale = new Vector3( 1.0f / (boxMax.x - boxMin.x), 1.0f / (boxMax.y - boxMin.y), 1.0f / (boxMax.z - boxMin.z) );
		constants.IndirectBBScaleStrength = new( bbScale, indirectStrength );
		constants.IndirectBBOffset = new( -(boxMin * bbScale), 0.0f );
		constants.VolumeScaleShiftNearFar = new( 1.0f / Depth, 0.0f, near, clipRange );

		// The volumes the view sees, in the world's order
		var count = 0;
		var frustum = view.Frustum;
		fixed ( Constants* c = &constants )
			for ( int i = 0; i < volumes.Count && count < MaxVolumes; i++ )
			{
				var volume = volumes[i];
				var localToWorld = Matrix.CreateRotation( volume.Transform.Rotation ) * Matrix.CreateTranslation( volume.Transform.Position );
				var bounds = volume.Bounds;
				TransformBox( localToWorld, bounds.Mins, bounds.Maxs, out var worldMin, out var worldMax );
				if ( !frustum.Intersects( (worldMin + worldMax) * 0.5f, (worldMax - worldMin) * 0.5f ) ) continue;

				var worldToVolume = localToWorld.Inverted.Transpose();
				var color = volume.Color;

				Set4( c->BoxMin, count, new( bounds.Mins, volume.Spherical ? 1.0f : 0.0f ) );
				Set4( c->BoxMax, count, new( bounds.Maxs, volume.Strength ) );
				Set4( c->BoxColorExponent, count, new( color.r, color.g, color.b, volume.Exponent ) );
				*(Matrix*)(c->BoxWorldToVolume + count * 16) = worldToVolume;
				count++;
			}
		constants.NumFogVolumeBoundingBoxes[0] = count;

		var fadeStart = fadeInStart < 0.0f ? fadeInEnd + fadeInStart : fadeInStart;
		if ( fadeInEnd > 0.0f && fadeStart < fadeInEnd )
		{
			var scale = 1.0f / (fadeInEnd - fadeStart);
			constants.FogFadeInScaleBias = new( scale, -fadeStart * scale, 0.0f, 0.0f );
		}
		else
		{
			constants.FogFadeInScaleBias = new( 0.0f, 1.0f, 0.0f, 0.0f );
		}

		constants.WidthHeightDepth[0] = Width;
		constants.WidthHeightDepth[1] = Height;
		constants.WidthHeightDepth[2] = Depth;
		constants.WidthHeightDepth[3] = 0;
		constants.OoWidthHeightDepth = new( 1.0f / Width, 1.0f / Height, 1.0f / Depth, 0.0f );
	}

	static unsafe void Set4( float* array, int index, Vector4 value ) => *(Vector4*)(array + index * 4) = value;

	/// <summary>
	/// <c>CalculateBounds</c>: the box around every volume's corners in the world, which a baked fog texture covers.
	/// </summary>
	void CalculateBounds( List<FogVolume> volumes )
	{
		boxMin = new( float.MaxValue );
		boxMax = new( -float.MaxValue );
		for ( int i = 0; i < volumes.Count; i++ )
		{
			var volume = volumes[i];
			var localToWorld = Matrix.CreateRotation( volume.Transform.Rotation ) * Matrix.CreateTranslation( volume.Transform.Position );
			TransformBox( localToWorld, volume.Bounds.Mins, volume.Bounds.Maxs, out var min, out var max );
			boxMin = Vector3.Min( boxMin, min );
			boxMax = Vector3.Max( boxMax, max );
		}
	}

	/// <summary>
	/// A box's eight corners through <paramref name="matrix"/>, and the box around them.
	/// </summary>
	static void TransformBox( in Matrix matrix, Vector3 mins, Vector3 maxs, out Vector3 min, out Vector3 max )
	{
		min = new( float.MaxValue );
		max = new( -float.MaxValue );
		for ( int c = 0; c < 8; c++ )
		{
			var corner = new Vector3( (c & 1) != 0 ? maxs.x : mins.x, (c & 2) != 0 ? maxs.y : mins.y, (c & 4) != 0 ? maxs.z : mins.z );
			var point = matrix.Transform( corner );
			min = Vector3.Min( min, point );
			max = Vector3.Max( max, point );
		}
	}

	public void Dispose() => Release();
}
