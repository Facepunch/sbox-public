using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// Bend Studio contact shadows, for native's views (<see cref="ShadowMapper.RenderScreenSpaceShadows"/>) and the managed scene renderer's.
/// Kept out of <see cref="ShadowMapper"/> so the mapper's statics don't need the engine.
/// </summary>
internal static class ContactShadows
{
	[ConVar( "r.shadows.contact.enabled", Help = "Enable screen-space (contact) shadows for directional lights." )]
	public static bool Enabled { get; set; } = true;

	// Must match WAVE_SIZE / numthreads in screen_space_shadows_cs.shader.
	const int WaveSize = 64;
	const int MaxDispatches = 8;

	static readonly ComputeShader Compute = new( "screen_space_shadows_cs" );

	/// <summary>
	/// Draw a directional light's contact shadows into <paramref name="mask"/>, from the depth chain (<c>DepthChainDownsample</c>) of a
	/// view whose world to projection matrix is <paramref name="viewProjection"/> (reverse-Z, row-vector), for a light pointing
	/// <paramref name="lightDirection"/> (towards the light) with <paramref name="shadowHardness"/>, into the current
	/// <see cref="Graphics"/> context. The body of <see cref="ShadowMapper.RenderScreenSpaceShadows"/>, which the managed scene renderer runs
	/// too, for its own frames. It can run in <paramref name="steps"/>: the mask's clear and barriers on the graphics queue, and
	/// the dispatches between them on the async compute queue, which neither clears nor names graphics stages.
	/// </summary>
	internal static void Render( Texture mask, Matrix viewProjection, Vector3 lightDirection, float shadowHardness, Steps steps = Steps.All )
	{
		int width = mask.Width;
		int height = mask.Height;

		var lightProjection = viewProjection.Transform( new Vector4( lightDirection, 0.0f ) );

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
			ShadowContrast = 1.0f + shadowHardness * 4.0f, // same as CSM penumbra
			FarDepthValue = 0.0f,  // reverse-Z
			NearDepthValue = 1.0f,
			IgnoreEdgePixels = 0,
			UsePrecisionOffset = 0,
			BilinearSamplingOffsetMode = 0,
			UseEarlyOut = 1,
		};

		// Sparse wavefront writes — unwritten pixels stay lit.
		if ( (steps & Steps.Prepare) != 0 )
		{
			mask.Clear( Color.White );
			Graphics.ResourceBarrierTransition( mask, ResourceState.UnorderedAccess );
		}

		if ( (steps & Steps.Dispatch) == 0 )
		{
			if ( (steps & Steps.Finish) != 0 ) Graphics.ResourceBarrierTransition( mask, ResourceState.PixelShaderResource );
			return;
		}

		var attrs = Graphics.Attributes;
		attrs.Set( "OutputShadow", mask );

		for ( int i = 0; i < dispatchCount; i++ )
		{
			ref readonly var d = ref dispatches[i];
			constants.WaveOffsetX = d.WaveOffsetX;
			constants.WaveOffsetY = d.WaveOffsetY;
			attrs.SetData( "SssConstants", constants );

			// Bend group counts: Dispatch divides by numthreads[WAVE_SIZE,1,1].
			Compute.DispatchWithAttributes( attrs, d.WaveCount0 * WaveSize, d.WaveCount1, d.WaveCount2 );
		}

		if ( (steps & Steps.Finish) != 0 ) Graphics.ResourceBarrierTransition( mask, ResourceState.PixelShaderResource );
	}

	/// <summary>
	/// The parts of <see cref="Render"/>, in order.
	/// </summary>
	[Flags]
	internal enum Steps
	{
		/// <summary>Clear the mask and make it writable: graphics.</summary>
		Prepare = 1,
		/// <summary>March the depth chain into it: compute only.</summary>
		Dispatch = 2,
		/// <summary>Make it readable by pixel shaders: graphics.</summary>
		Finish = 4,
		All = Prepare | Dispatch | Finish,
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
