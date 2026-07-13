using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Generates Bend Studio style screen-space contact shadows for a <see cref="Light"/>. Marches the
/// depth buffer in screen space to capture small-scale contact shadows that shadow maps miss.
/// <para>
/// Owned internally by the light component (not user-addable). The generated mask is published to
/// the light's scene object and sampled by the directional light shadow path, so it modulates the
/// directional (CSM) shadow.
/// </para>
/// </summary>
internal sealed class ScreenSpaceShadows
{
	/// <summary>
	/// The light these contact shadows are generated for. Determines the projection direction.
	/// </summary>
	readonly Light Light;

	public ScreenSpaceShadows( Light light )
	{
		Light = light;
	}

	/// <summary>
	/// Assumed thickness of each pixel for shadow casting, as a fraction of the non-linear depth
	/// difference between the sample and the far plane. Scale up/down in multiples of 2 to tune.
	/// </summary>
	public float SurfaceThickness { get; set; } = 0.01f;

	/// <summary>
	/// Depth difference (as a fraction) above which neighbouring samples are treated as an edge
	/// and not interpolated. Enable the edge-mask debug view to tune this.
	/// </summary>
	public float BilinearThreshold { get; set; } = 0.02f;


	/// <summary>
	/// Contrast boost applied to the transition in and out of shadow (higher = harder edge, less
	/// penumbra). Derived from the light's <see cref="Light.ShadowHardness"/> using the same
	/// <c>1 + hardness*4</c> mapping the cascaded shadow map uses, so the contact-shadow penumbra
	/// softness follows the light and stays consistent with its shadow-map penumbra.
	/// </summary>
	float ShadowContrast => 1.0f + Light.ShadowHardness * 4.0f;

	/// <summary>
	/// Don't let detected edge pixels contribute to the shadow. Helps with grazing-angle aliasing on
	/// flat surfaces, but can thin out otherwise valid shadows (e.g. on foliage).
	/// </summary>
	const bool IgnoreEdgePixels = false;

	/// <summary>
	/// Alternate bilinear sampling mode (see Bend reference). Both modes have subtle visual
	/// trade-offs around grazing angles and depth-buffer aliasing.
	/// </summary>

	const bool BilinearSamplingOffsetMode = false;

	/// <summary>
	/// Early-out wavefronts whose pixels fall outside the light's on-screen depth bounds. Cheaper
	/// when only part of the screen needs a shadow term.
	/// </summary>
	const bool UseEarlyOut = true;

	[ConVar( "r.shadows.contact.enabled", Help = "Enable screen-space (contact) shadows for directional lights." )]
	public static bool Enabled { get; set; } = true;

	// Must match WAVE_SIZE / numthreads[WAVE_SIZE,1,1] in screen_space_shadows_cs.shader.
	const int WaveSize = 64;

	// Maximum number of compute dispatches produced by BuildDispatchList for a single light.
	const int MaxDispatches = 8;

	static readonly ComputeShader Compute = new( "screen_space_shadows_cs" );

	readonly DispatchData[] _dispatches = new DispatchData[MaxDispatches];

	// Recorded and executed inline on the render thread for whichever camera is rendering.
	readonly CommandList _commands = new( "Screen Space Shadows" );

	/// <summary>
	/// We're no longer producing a mask - tell the light to stop sampling it.
	/// </summary>
	public void Disable()
	{
		if ( Light.IsValid() )
			Light.SceneObject?.ShadowMaskTextureIndices.Clear();
	}

	// Runs on the render thread for every camera that renders this scene - including the editor
	// viewport camera. That's why we generate the mask here rather than for Scene.Camera.
	public void OnRenderStage( CameraComponent camera, Stage stage )
	{
		// Generate once per view, right after its depth is available. Only the main game view and the
		// editor viewport need contact shadows - skip reflection probes, cubemap captures, thumbnails.
		if ( stage != Stage.AfterDepthPrepass )
			return;

		if ( !Light.IsValid() )
			return;

		// Toggled off - stop publishing a mask so the light stops sampling it.
		if ( !Enabled )
		{
			Disable();
			return;
		}

		GenerateMask();
	}

	void GenerateMask()
	{
		var sceneLight = Light.SceneObject;

		// Masks are per view. Key everything on the view actually being rendered (its size, frustum
		// and managed camera id) - the editor viewport renders through its own SceneCamera, not the
		// CameraComponent's, so the component is the wrong source for any of these.
		var view = Graphics.SceneView;
		int cameraId = view.m_ManagedCameraId;

		// Default to "no mask"; only publish a real index if we actually generate one this frame.
		sceneLight.ShadowMaskTextureIndices[cameraId] = 0;

		int width = (int)Graphics.Viewport.Size.x;
		int height = (int)Graphics.Viewport.Size.y;
		if ( width < 1 || height < 1 )
			return;

		// Homogeneous light coordinate in the same (reverse-Z) clip space as the depth buffer.
		// Directional lights project a direction (w = 0), positional lights project a position (w = 1).
		var viewProjection = view.GetFrustum().GetReverseZViewProjTranspose();

		Vector4 lightProjection = Light is DirectionalLight
			? viewProjection.Transform( new Vector4( -Light.WorldRotation.Forward, 0.0f ) )
			: viewProjection.Transform( new Vector4( Light.WorldPosition, 1.0f ) );

		int dispatchCount = BuildDispatchList( lightProjection, width, height, out var lightCoordinate );
		if ( dispatchCount <= 0 )
			return;

		// Pooled mask, R16F (1 = lit, 0 = shadowed). The stable per-camera target name keeps its
		// bindless index stable across frames, so each view's shadow setup (which snapshots the
		// index during light binning, before this hook runs) still lands on the right texture.
		var mask = RenderTarget.GetTemporary( width, height, ImageFormat.R16F, ImageFormat.None, MultisampleAmount.MultisampleNone, 1, $"SssShadowMask_{Light.Id}_{cameraId}" );

		var constants = new SssConstants
		{
			LightCoordinate = lightCoordinate,
			InvDepthTextureSize = new Vector2( 1.0f / width, 1.0f / height ),
			DepthBounds = new Vector2( 0.0f, 1.0f ),
			SurfaceThickness = SurfaceThickness,
			BilinearThreshold = BilinearThreshold,
			ShadowContrast = ShadowContrast,
			// Reverse-Z depth buffer: far plane = 0, near plane = 1.
			FarDepthValue = 0.0f,
			NearDepthValue = 1.0f,
			IgnoreEdgePixels = IgnoreEdgePixels ? 1 : 0,
			UsePrecisionOffset = 0,
			BilinearSamplingOffsetMode = BilinearSamplingOffsetMode ? 1 : 0,
			UseEarlyOut = UseEarlyOut ? 1 : 0,
		};

		_commands.Reset();

		// The compute writes pixels sparsely (only along wavefronts), so clear unwritten pixels to lit.
		_commands.Clear( mask.ColorTarget, Color.White );
		_commands.ResourceBarrierTransition( mask.ColorTarget, ResourceState.UnorderedAccess );
		_commands.Attributes.Set( "OutputShadow", mask.ColorTarget );

		// One dispatch per entry in the list. Only WaveOffset differs between dispatches, so we just
		// re-upload the constant buffer (recorded in order) before each dispatch.
		for ( int i = 0; i < dispatchCount; i++ )
		{
			ref readonly var dispatch = ref _dispatches[i];

			constants.WaveOffsetX = dispatch.WaveOffsetX;
			constants.WaveOffsetY = dispatch.WaveOffsetY;
			_commands.Attributes.SetData( "SssConstants", constants );

			// Bend dispatches group counts (WaveCount0, WaveCount1, WaveCount2). DispatchCompute takes
			// thread counts and divides by numthreads[WAVE_SIZE,1,1], so multiply the X group count by
			// WAVE_SIZE to land on exactly WaveCount0 groups; Y/Z group counts pass through unchanged.
			_commands.DispatchCompute( Compute,
				dispatch.WaveCount0 * WaveSize,
				dispatch.WaveCount1,
				dispatch.WaveCount2 );
		}

		// Make the mask readable by the opaque lighting pass, then publish its bindless index to the
		// light so DirectionalLightShadow.hlsl can sample it.
		_commands.ResourceBarrierTransition( mask.ColorTarget, ResourceState.PixelShaderResource );
		_commands.ExecuteOnRenderThread();

		sceneLight.ShadowMaskTextureIndices[cameraId] = (uint)mask.ColorTarget.Index;
	}

	/// <summary>
	/// Mirrors the <c>SssConstants</c> cbuffer in screen_space_shadows_cs.shader. Field order and
	/// 16-byte alignment must match the HLSL declaration exactly.
	/// </summary>
	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
	struct SssConstants
	{
		public Vector4 LightCoordinate;
		public int WaveOffsetX;
		public int WaveOffsetY;
		public Vector2 InvDepthTextureSize;
		public Vector2 DepthBounds;
		public float SurfaceThickness;
		public float BilinearThreshold;
		public float ShadowContrast;
		public float FarDepthValue;
		public float NearDepthValue;
		public int IgnoreEdgePixels;
		public int UsePrecisionOffset;
		public int BilinearSamplingOffsetMode;
		public int UseEarlyOut;
		int _pad0;
	}

	struct DispatchData
	{
		public int WaveCount0;
		public int WaveCount1;
		public int WaveCount2;
		public int WaveOffsetX;
		public int WaveOffsetY;
	}

	/// <summary>
	/// Port of Bend Studio's BuildDispatchList (bend_sss_cpu.h, Apache-2.0). Computes the shared light
	/// coordinate and the list of compute dispatches (wave counts + per-dispatch wave offset) needed to
	/// generate a screen-space shadow for the light. Bounds are the full viewport for this first pass.
	/// </summary>
	int BuildDispatchList( Vector4 lightProjection, int viewportWidth, int viewportHeight, out Vector4 lightCoordinate )
	{
		int dispatchCount = 0;

		// Floating point division in the shader has a practical precision limit when the light is very
		// far off screen, so use an adjusted w when computing the light XY coordinate.
		float xyLightW = lightProjection.w;
		float fpLimit = 0.000002f * WaveSize;

		if ( xyLightW >= 0 && xyLightW < fpLimit ) xyLightW = fpLimit;
		else if ( xyLightW < 0 && xyLightW > -fpLimit ) xyLightW = -fpLimit;

		lightCoordinate = new Vector4(
			((lightProjection.x / xyLightW) * +0.5f + 0.5f) * viewportWidth,
			((lightProjection.y / xyLightW) * -0.5f + 0.5f) * viewportHeight,
			lightProjection.w == 0 ? 0 : (lightProjection.z / lightProjection.w),
			lightProjection.w > 0 ? 1 : -1 );

		Span<int> lightXY = stackalloc int[2];
		lightXY[0] = (int)(lightCoordinate.x + 0.5f);
		lightXY[1] = (int)(lightCoordinate.y + 0.5f);

		// Inclusive render bounds (full viewport), made relative to the light.
		Span<int> biasedBounds = stackalloc int[4];
		biasedBounds[0] = 0 - lightXY[0];
		biasedBounds[1] = -(viewportHeight - lightXY[1]);
		biasedBounds[2] = viewportWidth - lightXY[0];
		biasedBounds[3] = -(0 - lightXY[1]);

		// Process the 4 quadrants around the light. Each is a rectangle with one corner on the light XY.
		Span<int> bounds = stackalloc int[4];
		for ( int q = 0; q < 4; q++ )
		{
			bool vertical = q == 0 || q == 3;

			bounds[0] = Math.Max( 0, ((q & 1) != 0 ? biasedBounds[0] : -biasedBounds[2]) ) / WaveSize;
			bounds[1] = Math.Max( 0, ((q & 2) != 0 ? biasedBounds[1] : -biasedBounds[3]) ) / WaveSize;
			bounds[2] = Math.Max( 0, (((q & 1) != 0 ? biasedBounds[2] : -biasedBounds[0]) + WaveSize * (vertical ? 1 : 2) - 1) ) / WaveSize;
			bounds[3] = Math.Max( 0, (((q & 2) != 0 ? biasedBounds[3] : -biasedBounds[1]) + WaveSize * (vertical ? 2 : 1) - 1) ) / WaveSize;

			if ( (bounds[2] - bounds[0]) <= 0 || (bounds[3] - bounds[1]) <= 0 )
				continue;

			int biasX = (q == 2 || q == 3) ? 1 : 0;
			int biasY = (q == 1 || q == 3) ? 1 : 0;

			int dispIndex = dispatchCount++;
			ref var disp = ref _dispatches[dispIndex];

			disp.WaveCount0 = WaveSize;
			disp.WaveCount1 = bounds[2] - bounds[0];
			disp.WaveCount2 = bounds[3] - bounds[1];
			disp.WaveOffsetX = ((q & 1) != 0 ? bounds[0] : -bounds[2]) + biasX;
			disp.WaveOffsetY = ((q & 2) != 0 ? -bounds[3] : bounds[1]) + biasY;

			// Far corner of this quadrant relative to the light: where the diagonal light ray meets the
			// bounds edge. If the quadrant rectangle isn't square it must be split on the larger axis.
			int axisDelta = +biasedBounds[0] - biasedBounds[1];
			if ( q == 1 ) axisDelta = +biasedBounds[2] + biasedBounds[1];
			if ( q == 2 ) axisDelta = -biasedBounds[0] - biasedBounds[3];
			if ( q == 3 ) axisDelta = -biasedBounds[2] + biasedBounds[3];

			axisDelta = (axisDelta + WaveSize - 1) / WaveSize;

			if ( axisDelta <= 0 )
				continue;

			int disp2Index = dispatchCount++;
			ref var disp2 = ref _dispatches[disp2Index];
			disp2 = disp;

			if ( q == 0 )
			{
				// Split on Y, split becomes -1 larger on X.
				disp2.WaveCount2 = Math.Min( disp.WaveCount2, axisDelta );
				disp.WaveCount2 -= disp2.WaveCount2;
				disp2.WaveOffsetY = disp.WaveOffsetY + disp.WaveCount2;
				disp2.WaveOffsetX--;
				disp2.WaveCount1++;
			}
			if ( q == 1 )
			{
				// Split on X, split becomes +1 larger on Y.
				disp2.WaveCount1 = Math.Min( disp.WaveCount1, axisDelta );
				disp.WaveCount1 -= disp2.WaveCount1;
				disp2.WaveOffsetX = disp.WaveOffsetX + disp.WaveCount1;
				disp2.WaveCount2++;
			}
			if ( q == 2 )
			{
				// Split on X, split becomes -1 larger on Y.
				disp2.WaveCount1 = Math.Min( disp.WaveCount1, axisDelta );
				disp.WaveCount1 -= disp2.WaveCount1;
				disp.WaveOffsetX += disp2.WaveCount1;
				disp2.WaveCount2++;
				disp2.WaveOffsetY--;
			}
			if ( q == 3 )
			{
				// Split on Y, split becomes +1 larger on X.
				disp2.WaveCount2 = Math.Min( disp.WaveCount2, axisDelta );
				disp.WaveCount2 -= disp2.WaveCount2;
				disp.WaveOffsetY += disp2.WaveCount2;
				disp2.WaveCount1++;
			}

			// Remove either dispatch if it ended up empty. Order matters: handle disp2 then disp,
			// compacting against the end of the list (mirrors the reference's swap-with-last).
			if ( disp2.WaveCount1 <= 0 || disp2.WaveCount2 <= 0 )
			{
				disp2 = _dispatches[--dispatchCount];
			}
			if ( disp.WaveCount1 <= 0 || disp.WaveCount2 <= 0 )
			{
				disp = _dispatches[--dispatchCount];
			}
		}

		// The shader expects the wave offsets scaled by the wave count.
		for ( int i = 0; i < dispatchCount; i++ )
		{
			_dispatches[i].WaveOffsetX *= WaveSize;
			_dispatches[i].WaveOffsetY *= WaveSize;
		}

		return dispatchCount;
	}
}
