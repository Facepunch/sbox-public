using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// Bend Studio contact shadows. Called after Hi-Z (depth chain ready); bindless index set in CSM setup.
/// </summary>
internal partial class ShadowMapper
{
	[ConVar( "r.shadows.contact.enabled", Help = "Enable screen-space (contact) shadows for directional lights." )]
	public static bool ContactShadowsEnabled { get; set; } = true;

	// Must match WAVE_SIZE / numthreads in screen_space_shadows_cs.shader.
	const int WaveSize = 64;
	const int MaxDispatches = 8;

	static readonly ComputeShader ContactShadowCompute = new( "screen_space_shadows_cs" );

	internal void RenderScreenSpaceShadows( ISceneView view )
	{
		if ( SceneView != view || DirectionalLight is null )
			return;

		var light = DirectionalLight;
		if ( !ContactShadowsEnabled || !light.IsValid() || !light.ContactShadows )
			return;

		var mask = light.GetShadowMask( view );
		if ( mask is null )
			return;

		int width = mask.Width;
		int height = mask.Height;

		// Directional WorldDirection is already -Forward (CSceneLightObject::SetWorldDirection).
		var viewProjection = view.GetFrustum().GetReverseZViewProjTranspose();
		var lightProjection = viewProjection.Transform( new Vector4( light.WorldDirection, 0.0f ) );

		Span<DispatchData> dispatches = stackalloc DispatchData[MaxDispatches];
		int dispatchCount = BuildDispatchList( lightProjection, width, height, dispatches, out var lightCoordinate );
		if ( dispatchCount <= 0 )
			return;

		var constants = new SssConstants
		{
			LightCoordinate = lightCoordinate,
			InvDepthTextureSize = new Vector2( 1.0f / width, 1.0f / height ),
			DepthBounds = new Vector2( 0.0f, 1.0f ),
			SurfaceThickness = 0.01f,
			BilinearThreshold = 0.02f,
			ShadowContrast = 1.0f + light.ShadowHardness * 4.0f, // same as CSM penumbra
			FarDepthValue = 0.0f,  // reverse-Z
			NearDepthValue = 1.0f,
			IgnoreEdgePixels = 0,
			UsePrecisionOffset = 0,
			BilinearSamplingOffsetMode = 0,
			UseEarlyOut = 1,
		};

		// Sparse wavefront writes — unwritten pixels stay lit.
		mask.Clear( Color.White );
		Graphics.ResourceBarrierTransition( mask, ResourceState.UnorderedAccess );

		var attrs = Graphics.Attributes;
		attrs.Set( "OutputShadow", mask );

		for ( int i = 0; i < dispatchCount; i++ )
		{
			ref readonly var d = ref dispatches[i];
			constants.WaveOffsetX = d.WaveOffsetX;
			constants.WaveOffsetY = d.WaveOffsetY;
			attrs.SetData( "SssConstants", constants );

			// Bend group counts: Dispatch divides by numthreads[WAVE_SIZE,1,1].
			ContactShadowCompute.DispatchWithAttributes( attrs, d.WaveCount0 * WaveSize, d.WaveCount1, d.WaveCount2 );
		}

		Graphics.ResourceBarrierTransition( mask, ResourceState.PixelShaderResource );
	}

	[StructLayout( LayoutKind.Sequential )]
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
		public int WaveCount0, WaveCount1, WaveCount2;
		public int WaveOffsetX, WaveOffsetY;
	}

	/// <summary>Port of Bend Studio BuildDispatchList (bend_sss_cpu.h, Apache-2.0).</summary>
	static int BuildDispatchList( Vector4 lightProjection, int viewportWidth, int viewportHeight, Span<DispatchData> dispatches, out Vector4 lightCoordinate )
	{
		int dispatchCount = 0;

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

		Span<int> biasedBounds = stackalloc int[4];
		biasedBounds[0] = 0 - lightXY[0];
		biasedBounds[1] = -(viewportHeight - lightXY[1]);
		biasedBounds[2] = viewportWidth - lightXY[0];
		biasedBounds[3] = -(0 - lightXY[1]);

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

			ref var disp = ref dispatches[dispatchCount++];
			disp.WaveCount0 = WaveSize;
			disp.WaveCount1 = bounds[2] - bounds[0];
			disp.WaveCount2 = bounds[3] - bounds[1];
			disp.WaveOffsetX = ((q & 1) != 0 ? bounds[0] : -bounds[2]) + biasX;
			disp.WaveOffsetY = ((q & 2) != 0 ? -bounds[3] : bounds[1]) + biasY;

			int axisDelta = +biasedBounds[0] - biasedBounds[1];
			if ( q == 1 ) axisDelta = +biasedBounds[2] + biasedBounds[1];
			if ( q == 2 ) axisDelta = -biasedBounds[0] - biasedBounds[3];
			if ( q == 3 ) axisDelta = -biasedBounds[2] + biasedBounds[3];
			axisDelta = (axisDelta + WaveSize - 1) / WaveSize;
			if ( axisDelta <= 0 )
				continue;

			ref var disp2 = ref dispatches[dispatchCount++];
			disp2 = disp;

			if ( q == 0 )
			{
				disp2.WaveCount2 = Math.Min( disp.WaveCount2, axisDelta );
				disp.WaveCount2 -= disp2.WaveCount2;
				disp2.WaveOffsetY = disp.WaveOffsetY + disp.WaveCount2;
				disp2.WaveOffsetX--;
				disp2.WaveCount1++;
			}
			else if ( q == 1 )
			{
				disp2.WaveCount1 = Math.Min( disp.WaveCount1, axisDelta );
				disp.WaveCount1 -= disp2.WaveCount1;
				disp2.WaveOffsetX = disp.WaveOffsetX + disp.WaveCount1;
				disp2.WaveCount2++;
			}
			else if ( q == 2 )
			{
				disp2.WaveCount1 = Math.Min( disp.WaveCount1, axisDelta );
				disp.WaveCount1 -= disp2.WaveCount1;
				disp.WaveOffsetX += disp2.WaveCount1;
				disp2.WaveCount2++;
				disp2.WaveOffsetY--;
			}
			else // q == 3
			{
				disp2.WaveCount2 = Math.Min( disp.WaveCount2, axisDelta );
				disp.WaveCount2 -= disp2.WaveCount2;
				disp.WaveOffsetY += disp2.WaveCount2;
				disp2.WaveCount1++;
			}

			if ( disp2.WaveCount1 <= 0 || disp2.WaveCount2 <= 0 )
				disp2 = dispatches[--dispatchCount];
			if ( disp.WaveCount1 <= 0 || disp.WaveCount2 <= 0 )
				disp = dispatches[--dispatchCount];
		}

		for ( int i = 0; i < dispatchCount; i++ )
		{
			dispatches[i].WaveOffsetX *= WaveSize;
			dispatches[i].WaveOffsetY *= WaveSize;
		}

		return dispatchCount;
	}
}
