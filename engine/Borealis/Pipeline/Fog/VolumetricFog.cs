using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Borealis;

/// <summary>
/// Per-camera fog, ported from <c>CVolumetricFog</c>/<c>CVolumetricFogRenderer</c> (scenesystem/utils/volumetricfog.cpp).
/// Lights, temporally blends and integrates froxels with <c>cs_volumetric_fog.shader</c> for the next frame.
/// </summary>
internal sealed class VolumetricFog : IDisposable
{
	const int ThreadGroupSize = 8;

	/// <summary>Native's <c>volume_fog_*</c> defaults: froxels across, down and deep.</summary>
	public static Vector3Int DefaultSize => new( 240, 160, 64 );

	// The froxels it's made with (RenderHost.VolumetricFogSize), width and height a whole number of thread groups
	int width = 240, height = 160, depth = 64;
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
	/// Mirrors <c>CVolumetricFog::VolumetricFogConstantBuffer_t</c> with Borealis's froxel bounds and ray origins.
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
		public fixed int FroxelMin[4];
		public fixed int FroxelMax[4];
		public Vector4 ViewOriginDX, ViewOriginDY, ViewOriginCorner;
	}

	Constants constants;
	Vector3Int litFroxels;
	IntPtr constantBuffer;
	GpuTexture lighting, fogVolume;
	readonly GpuTexture[] accumulated = new GpuTexture[2];
	static readonly ConditionalWeakTable<GpuDevice, GpuTexture> opaqueBlack = new();
	GpuMaterial computeShader;

	uint frameOffset;

	// The jitter's random offsets, from the same seed every time, so the fog's frames repeat run to run
	Random jitter = new( 0 );
	int scatteringAtZeroCount;
	bool renderingDisabled;

	float scattering, anisotropy, farClip = 1000, fadeInStart = 20, fadeInEnd = 100, indirectStrength;
	GpuTexture bakedTexture;
	Vector3 boxMin, boxMax;

	// Last rendered frame's world-to-fog projection, with linear view depth in Z.
	Matrix? lastFogProjection;

	/// <summary>
	/// Whether fog is enabled, has volumes and remains active (<c>CRenderingPipelineStandard::AddLayersToView</c>).
	/// </summary>
	public bool Active { get; private set; }

	/// <summary>
	/// Forward fog texture, or opaque black when not scattering (<c>GetFogVolumeTexture</c>).
	/// </summary>
	public GpuTexture FogVolumeTexture => Active && scattering != 0.0f ? fogVolume : OpaqueBlack;

	/// <summary>
	/// No-fog fallback: opaque black 2x2x2 (<c>SCENE_TXTR_OPAQUE_BLACK_2x2x2</c>), one per device: a texture of one device
	/// isn't another's.
	/// </summary>
	public static GpuTexture OpaqueBlack => opaqueBlack.GetValue( RenderHost.Current.Device, static device => device.CreateTexture(
		TextureDesc.Volume( 2, 2, 2, TextureFormat.RGBA8888 ) with { Data = OpaqueBlackData() }, "Borealis opaque black 2x2x2" ) );

	static byte[] OpaqueBlackData()
	{
		var data = new byte[2 * 2 * 2 * 4];
		for ( int i = 3; i < data.Length; i += 4 ) data[i] = 255;
		return data;
	}

	/// <summary>
	/// Update settings and history, then create resources on the main thread before setup
	/// (<c>SetParams</c>, <c>FrameUpdate</c>, <c>CreateRendererSingleUse</c>). Disabling fog, in its settings or by the host
	/// not <paramref name="allowed"/> it (<c>r_enable_volume_fog</c>), releases resources.
	/// </summary>
	public void BeginFrame( VolumetricFogSettings settings, bool hasVolumes, bool allowed = true )
	{
		Active = false;
		if ( !settings.Enabled || !allowed )
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

		Resize( RenderHost.Current.VolumetricFogSize );
		EnsureResources();
		Active = !renderingDisabled;
	}

	/// <summary>
	/// Take the froxels the host asks for (the fog quality's), width and height rounded down to whole thread groups; a change
	/// lets the volumes go, to be made again at the new size.
	/// </summary>
	void Resize( Vector3Int size )
	{
		var x = Math.Max( size.x & ~(ThreadGroupSize - 1), ThreadGroupSize );
		var y = Math.Max( size.y & ~(ThreadGroupSize - 1), ThreadGroupSize );
		var z = Math.Clamp( size.z, 1, 256 );
		if ( x == width && y == height && z == depth ) return;

		Release();
		(width, height, depth) = (x, y, z);
	}

	/// <summary>The froxels across, down and deep it draws with now.</summary>
	internal Vector3Int Size => new( width, height, depth );

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

		frameOffset = 0;
		lastFogProjection = null;
		jitter = new Random( 0 );
		scattering = 0.0f;
		anisotropy = 0.2f;
		scatteringAtZeroCount = FramesAtZeroScatteringBeforeSwitchingOff;
		renderingDisabled = false;

		var device = RenderHost.Current.Device;
		device.Clear( lighting, Color.Transparent );
		device.Clear( accumulated[0], Color.Transparent );
		device.Clear( accumulated[1], Color.Transparent );
		device.Clear( fogVolume, Color.Transparent );

		computeShader ??= RenderHost.Current.Device.LoadMaterial( "materials/dev/cs_volumetric_fog.vmat" );
		if ( constantBuffer == IntPtr.Zero ) constantBuffer = GpuContext.CreateConstantBuffer( System.Runtime.CompilerServices.Unsafe.SizeOf<Constants>() );
	}

	GpuTexture MakeVolume( string name ) => RenderHost.Current.Device.CreateTexture( TextureDesc.Volume( width, height, depth, TextureFormat.RGBA16161616F ) with { Storage = true }, $"Borealis {name}" );

	void Release()
	{
		lighting?.Dispose();
		accumulated[0]?.Dispose();
		accumulated[1]?.Dispose();
		fogVolume?.Dispose();
		lighting = fogVolume = accumulated[0] = accumulated[1] = null;
		parameters = null;

		if ( constantBuffer != IntPtr.Zero ) GpuContext.DestroyConstantBuffer( constantBuffer );
		constantBuffer = IntPtr.Zero;
	}

	float NearClip => farClip / depth;

	/// <summary>
	/// World-to-fog shading constants for <c>PerViewConstantBufferVR_t</c> (<c>GetShadingConstants</c>).
	/// </summary>
	public void FillShadingConstants( ref ViewEnvironment.ViewConstantsVRData vr, RenderView view, Matrix? fromWorld = null )
	{
		var near = NearClip;
		var clipRange = farClip - near;
		var dither = farClip * DitherScale / 600.0f;

		vr.VolumetricFogEnabled = 1;
		vr.VolFogVolumeScaleShiftNearRange = new( 1.0f / depth, 0.0f, near, clipRange );
		vr.VolFogDitherScaleBias = new( dither, -0.5f * dither, 0.0f, 0.0f );
		vr.VolFogPostWorldToFrustumScale = new( 0.5f, 0.5f, 1.0f / clipRange, 1.0f );
		vr.VolFogPostWorldToFrustumBias = new( 0.5f, 0.5f, -near / clipRange, 0.0f );

		// The fog uses linear view depth rather than device Z.
		// Transform skybox positions into the main world first (skyboxToWorld).
		var projection = FogProjection( view );
		var toFog = fromWorld is { } toWorld ? toWorld * projection : projection;
		vr.VolFogFromWorld0 = toFog;
		vr.VolFogFromWorld1 = toFog;
	}

	static Matrix FogProjection( RenderView view )
	{
		var projection = view.UnjitteredWorldToProjection;
		var forward = view.Rotation.Forward;
		projection.M13 = forward.x;
		projection.M23 = forward.y;
		projection.M33 = forward.z;
		projection.M43 = -Vector3.Dot( view.Position, forward );
		return projection;
	}

	/// <summary>
	/// <c>CVolumetricFogRenderer::Render</c>: the constants for this view, then the lighting and integrate passes over the froxels.
	/// </summary>
	public void Render( GpuContext rc, RenderView view, List<FogVolume> volumes )
	{
		if ( renderingDisabled ) return;

		SetupConstants( view, volumes );

		// Its own parameters over the context's (the view's lights, shadows, depth chain): a dispatch given them binds the
		// froxels it writes as storage, which one with the context's alone binds as their placeholder (it writes nothing)
		var parameters = this.parameters ??= RenderHost.Current.Device.CreateParameters();

		// volume_fog_jitter_offset_random is on: a random blue noise offset each frame
		parameters.Set( JitterOffsetName, jitter.Next( 0, 128 ) );
		// Too big for a dynamic buffer: its own, as native's fog has (m_hFogLightingConstantBuffer)
		rc.SetConstantBuffer( ConstantBufferName, constantBuffer, constants );
		parameters.SetCombo( BakedFogCombo, bakedTexture is not null ? 1 : 0 );
		parameters.SetCombo( IblIndirectCombo, 1 );

		parameters.Set( InBakedFogName, bakedTexture ?? OpaqueBlack );
		parameters.Set( InLightingName, lighting );
		parameters.Set( InAccumulationName, accumulated[frameOffset] );
		parameters.Set( OutLightingName, lighting );
		parameters.Set( OutAccumulationName, accumulated[frameOffset ^ 1] );
		parameters.Set( OutScatteringName, fogVolume );

		for ( int pass = 0; pass < 2; pass++ )
		{
			var scope = rc.BeginGpuScope( PassNames[pass] );
			parameters.SetCombo( PassCombo, pass );
			// Lighting is a thread per froxel the volumes cover, integrating a thread per column
			if ( pass == 1 ) rc.Dispatch( computeShader, parameters, width, height, 1 );
			else if ( litFroxels.x > 0 && litFroxels.y > 0 && litFroxels.z > 0 ) rc.Dispatch( computeShader, parameters, litFroxels.x, litFroxels.y, litFroxels.z );
			rc.EndGpuScope( scope );
		}

		// What the next frame's draws read, sampled
		rc.BarrierComputeToPixelShaderRead( fogVolume );
		frameOffset ^= 1;
	}

	ShaderParameters parameters;

	// GPU scopes of the two dispatches (FogPasses::Lighting, FogPasses::Integrate)
	static readonly string[] PassNames = ["VolumetricFog Lighting", "VolumetricFog Integrate"];

	/// <summary>
	/// Fill froxel rays, reprojection, depth slices, fade and up to 128 visible volumes (<c>SetupConstantBuffer</c>).
	/// </summary>
	unsafe void SetupConstants( RenderView view, List<FogVolume> volumes )
	{
		CalculateBounds( volumes );

		// Unproject each corner at unit view depth, keeping the ray's origin separately for parallel projections.
		var forward = view.Rotation.Forward;
		var projection = view.UnjitteredViewToProjection;
		var projectionToView = projection.Inverted;
		var viewToWorld = view.WorldToViewRotation.Transpose();
		var clipZ = projectionToView.Transform( new Vector4( 0, 0, 1, 0 ) );
		var varyingClipW = projection.M14 != 0.0f || projection.M24 != 0.0f;

		void CornerRay( float x, float y, out Vector4 origin, out Vector4 direction )
		{
			var corner = projectionToView.Transform( new Vector4( x, y, 0, 1 ) );
			// Eliminate clip Z so (origin + direction * depth) / origin.w lies at the requested view depth.
			origin = viewToWorld.Transform( corner * clipZ.z - clipZ * corner.z );
			direction = viewToWorld.Transform( corner * clipZ.w - clipZ * corner.w );
		}

		CornerRay( -1, -1, out var origin0, out var ray0 );
		CornerRay( 1, -1, out var origin1, out var ray1 );
		CornerRay( -1, 1, out var origin2, out var ray2 );

		var rayScale = varyingClipW ? 1.0f : 1.0f / origin0.w;
		var dx = (ray1 - ray0) * rayScale;
		var dy = (ray2 - ray0) * rayScale;

		var camera = view.Position;
		constants.ViewDX = dx * (1.0f / width);
		constants.ViewDY = dy * (1.0f / height);
		constants.ViewCorner = ray0 * rayScale;
		constants.ViewOriginDX = (origin1 - origin0) * (rayScale / width);
		constants.ViewOriginDY = (origin2 - origin0) * (rayScale / height);
		constants.ViewOriginCorner = origin0 * rayScale;
		constants.ViewPos = new( camera, varyingClipW ? 1.0f : 0.0f );
		constants.ViewDir = new( forward, varyingClipW || projection.M13 != 0.0f || projection.M23 != 0.0f ? 1.0f : 0.0f );

		// Reproject camera-relative positions using the current camera offset and previous projection.
		var fogProjection = FogProjection( view );
		constants.LastViewFrustum = Matrix.CreateTranslation( camera ) * (lastFogProjection ?? fogProjection);
		lastFogProjection = fogProjection;

		constants.AnisotropyScatteringXYJitterScale = new( anisotropy, scattering, dx.Length, dy.Length );

		var near = NearClip;
		var clipRange = farClip - near;
		constants.NearFarClipPlaneReciprocal = new( near, 1.0f / clipRange, farClip, 0.0f );
		constants.PostWorldToFrustumScale = new( 0.5f, 0.5f, 1.0f / clipRange, 0.0f );
		constants.PostWorldToFrustumBias = new( 0.5f, 0.5f, -near / clipRange, 0.0f );
		constants.ThreadToVoxelScale = new( 2.0f / width, 2.0f / height, clipRange / depth, -near * farClip / clipRange );
		constants.ThreadToVoxelBias = new( 1.0f / width - 1.0f, 1.0f / height - 1.0f, near + 0.5f * clipRange / depth, (1.0f + (farClip + near) / clipRange) / 2.0f );

		var bbScale = new Vector3( 1.0f / (boxMax.x - boxMin.x), 1.0f / (boxMax.y - boxMin.y), 1.0f / (boxMax.z - boxMin.z) );
		constants.IndirectBBScaleStrength = new( bbScale, indirectStrength );
		constants.IndirectBBOffset = new( -(boxMin * bbScale), 0.0f );
		constants.VolumeScaleShiftNearFar = new( 1.0f / depth, 0.0f, near, clipRange );

		// The volumes the view sees, in the world's order, and the froxels they cover
		var count = 0;
		var cameraToProjection = view.WorldToViewRotation * projection;
		var froxelMin = new Vector3( float.MaxValue );
		var froxelMax = new Vector3( -float.MaxValue );

		// A world box's corners in froxels, through the froxel rays and the fog's square root slices; one behind the near plane could be anywhere
		void GrowFroxels( Vector3 worldMin, Vector3 worldMax )
		{
			for ( int c = 0; c < 8; c++ )
			{
				var point = new Vector3( (c & 1) != 0 ? worldMax.x : worldMin.x, (c & 2) != 0 ? worldMax.y : worldMin.y, (c & 4) != 0 ? worldMax.z : worldMin.z ) - camera;
				var viewDepth = Vector3.Dot( point, forward );
				var froxel = new Vector3( width, height, 0 );
				var behind = viewDepth < near;
				if ( !behind )
				{
					var clip = cameraToProjection.Transform( new Vector4( point, 1.0f ) );
					behind = clip.w <= 0.0f;
					if ( !behind )
					{
						var invW = 0.5f / clip.w;
						froxel = new( (clip.x * invW + 0.5f) * width, (clip.y * invW + 0.5f) * height, MathF.Sqrt( (viewDepth - near) / clipRange ) * depth );
					}
				}
				froxelMin = Vector3.Min( froxelMin, behind ? Vector3.Zero : froxel );
				froxelMax = Vector3.Max( froxelMax, froxel );
			}
		}
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

				GrowFroxels( worldMin, worldMax );
			}
		constants.NumFogVolumeBoundingBoxes[0] = count;

		// One froxel wider each way for the TAA neighbourhood, two further back for the depth jitter. With no volumes min ends past max: nothing's lit
		constants.FroxelMin[0] = Froxel( MathF.Floor( froxelMin.x ) - 1, width );
		constants.FroxelMin[1] = Froxel( MathF.Floor( froxelMin.y ) - 1, height );
		constants.FroxelMin[2] = Froxel( MathF.Floor( froxelMin.z ) - 1, depth );
		constants.FroxelMax[0] = Froxel( MathF.Ceiling( froxelMax.x ) + 1, width );
		constants.FroxelMax[1] = Froxel( MathF.Ceiling( froxelMax.y ) + 1, height );
		constants.FroxelMax[2] = Froxel( MathF.Ceiling( froxelMax.z ) + 2, depth );
		litFroxels = new( constants.FroxelMax[0] - constants.FroxelMin[0], constants.FroxelMax[1] - constants.FroxelMin[1], constants.FroxelMax[2] - constants.FroxelMin[2] );

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

		constants.WidthHeightDepth[0] = (uint)width;
		constants.WidthHeightDepth[1] = (uint)height;
		constants.WidthHeightDepth[2] = (uint)depth;
		constants.WidthHeightDepth[3] = 0;
		constants.OoWidthHeightDepth = new( 1.0f / width, 1.0f / height, 1.0f / depth, 0.0f );
	}

	static unsafe void Set4( float* array, int index, Vector4 value ) => *(Vector4*)(array + index * 4) = value;

	static int Froxel( float froxel, int size ) => (int)Math.Clamp( froxel, 0, size );

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
