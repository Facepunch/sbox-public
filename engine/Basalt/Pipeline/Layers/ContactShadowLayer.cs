namespace Basalt;

/// <summary>
/// Screen-space sun shadows using <c>ContactShadows.Render</c>, before <c>AfterDepthPrepass</c>.
/// Supplements cascades, including static casters excluded by baked suns.
/// </summary>
internal sealed class ContactShadowLayer : RenderLayer
{
	public ContactShadowLayer() : base( "Contact Shadows" )
	{
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.ContactShadowMask is not null;

	// screen_space_shadows_cs marches the depth chain
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;
	public override FrameResources Writes => FrameResources.ContactShadows;
	public override bool SupportsAsyncCompute => true;

	public override void HandOff( RenderFrame frame, GpuContext rc ) => Render( frame, rc, ContactShadows.Steps.Prepare );

	public override void TakeBack( RenderFrame frame, GpuContext rc ) => rc.BarrierComputeToPixelShaderRead( frame.ContactShadowMask );

	/// <summary>
	/// Acquire a viewport-sized mask when contact shadows are enabled for a perspective game camera with a prepass.
	/// Called before setup writes its bindless index into sun constants (<c>SceneLight.GetShadowMask</c>).
	/// </summary>
	internal GpuTexture MaskFor( RenderFrame frame )
	{
		var lighting = frame.World.Lighting;
		var view = frame.View;
		if ( frame.Stages is null || !frame.System.DepthPrepass || !frame.System.Shadows || view.Orthographic ) return null;
		if ( !RenderHost.Current.ContactShadows || !lighting.SunEnabled || !lighting.SunShadows || !lighting.SunContactShadows ) return null;

		var width = (int)view.Viewport.Width;
		var height = (int)view.Viewport.Height;
		if ( width < 1 || height < 1 ) return null;

		ContactShadows.WarmUp();
		return frame.System.Transients.Get( TextureDesc.Target( width, height, TextureFormat.A8 ) with { Storage = true }, "Contact Shadow Mask" );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		Render( frame, rc, rc.AsyncCompute ? ContactShadows.Steps.Dispatch : ContactShadows.Steps.All );
	}

	void Render( RenderFrame frame, GpuContext rc, ContactShadows.Steps steps )
	{
		// Native WorldDirection points toward the light.
		var lighting = frame.World.Lighting;
		ContactShadows.Render( rc, frame.ContactShadowMask, frame.View.WorldToProjection, -lighting.SunDirection, lighting.SunShadowHardness, steps );
	}
}
