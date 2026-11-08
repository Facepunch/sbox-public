using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// Bend Studio contact shadows, for native's views (<see cref="NativeShadowMapper.RenderScreenSpaceShadows"/>).
/// Basalt draws its own (<c>Basalt.Gpu.ContactShadows</c>); this shares its dispatch list and constants.
/// </summary>
internal static class ContactShadows
{
	[ConVar( "r.shadows.contact.enabled", Help = "Enable screen-space (contact) shadows for directional lights." )]
	public static bool Enabled { get; set; } = true;

	static readonly ComputeShader Compute = new( "screen_space_shadows_cs" );

	/// <summary>
	/// Draw a directional light's contact shadows into <paramref name="mask"/>, from the depth chain (<c>DepthChainDownsample</c>) of a
	/// view whose world to projection matrix is <paramref name="viewProjection"/> (reverse-Z, row-vector), for a light pointing
	/// <paramref name="lightDirection"/> (towards the light) with <paramref name="shadowHardness"/>, into the current
	/// <see cref="Graphics"/> context. The body of <see cref="NativeShadowMapper.RenderScreenSpaceShadows"/>.
	/// </summary>
	internal static void Render( Texture mask, Matrix viewProjection, Vector3 lightDirection, float shadowHardness )
	{
		Span<Basalt.Gpu.ContactShadows.DispatchData> dispatches = stackalloc Basalt.Gpu.ContactShadows.DispatchData[Basalt.Gpu.ContactShadows.MaxDispatches];
		int dispatchCount = Basalt.Gpu.ContactShadows.Plan( mask.Width, mask.Height, viewProjection, lightDirection, shadowHardness, dispatches, out var constants );
		if ( dispatchCount <= 0 )
			return;

		// Sparse wavefront writes - unwritten pixels stay lit.
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
			Compute.DispatchWithAttributes( attrs, d.WaveCount0 * Basalt.Gpu.ContactShadows.WaveSize, d.WaveCount1, d.WaveCount2 );
		}

		Graphics.ResourceBarrierTransition( mask, ResourceState.PixelShaderResource );
	}
}
