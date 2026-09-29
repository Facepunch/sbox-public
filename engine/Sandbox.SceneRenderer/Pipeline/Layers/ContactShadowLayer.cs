using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Screen-space sun shadows using <c>ContactShadows.Render</c>, before <c>AfterDepthPrepass</c>.
/// Supplements cascades, including static casters excluded by baked suns.
/// </summary>
internal sealed class ContactShadowLayer : RenderLayer
{
	// Per-system pool key; native uses one per light and camera.
	readonly string maskName;
	Graphics.ManagedView graphicsView;

	public ContactShadowLayer( int system ) : base( "Contact Shadows" )
	{
		maskName = $"ShadowMask_Managed_{system}";
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.ContactShadowMask is not null;

	// screen_space_shadows_cs marches the depth chain
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;

	// Its dispatches, anyway: the mask's clear and barriers are graphics, in HandOff and TakeBack
	public override bool AsyncCompute => true;

	public override void HandOff( RenderFrame frame, RenderContext rc ) => Render( frame, rc, ContactShadows.Steps.Prepare );

	public override void TakeBack( RenderFrame frame, RenderContext rc ) => rc.BarrierComputeToPixelShaderRead( frame.ContactShadowMask );

	/// <summary>
	/// Acquire a viewport-sized mask when contact shadows are enabled for a perspective game camera with a prepass.
	/// Called before setup writes its bindless index into sun constants (<c>SceneLight.GetShadowMask</c>).
	/// </summary>
	internal Texture MaskFor( RenderFrame frame )
	{
		var lighting = frame.World.Lighting;
		var view = frame.View;
		if ( frame.Stages is null || !frame.System.DepthPrepass || !frame.System.Shadows || view.Orthographic ) return null;
		if ( !ContactShadows.Enabled || !lighting.SunEnabled || !lighting.SunShadows || !lighting.SunContactShadows ) return null;

		var width = (int)view.Viewport.Width;
		var height = (int)view.Viewport.Height;
		if ( width < 1 || height < 1 ) return null;

		using var rt = RenderTarget.GetTemporary( width, height, ImageFormat.A8, ImageFormat.None, MultisampleAmount.MultisampleNone, 1, maskName );
		return rt.ColorTarget;
	}

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		Render( frame, rc, rc.AsyncCompute ? ContactShadows.Steps.Dispatch : ContactShadows.Steps.All );
	}

	void Render( RenderFrame frame, RenderContext rc, ContactShadows.Steps steps )
	{
		var target = frame.Output.Target;
		graphicsView ??= new Graphics.ManagedView();

		// The compute queue binds no render targets: the dispatches write the mask and read the depth chain
		graphicsView.Color = rc.AsyncCompute ? default : target.Color.native;
		graphicsView.Depth = rc.AsyncCompute ? default : target.Depth.native;
		graphicsView.SrgbWrite = false;
		graphicsView.Viewport = frame.View.Viewport;
		graphicsView.MinZ = frame.DepthMin;
		graphicsView.MaxZ = frame.DepthMax;
		graphicsView.ColorFormat = target.Color.ImageFormat;
		graphicsView.Msaa = target.Samples switch
		{
			2 => MultisampleAmount.Multisample2x,
			4 => MultisampleAmount.Multisample4x,
			8 => MultisampleAmount.Multisample8x,
			16 => MultisampleAmount.Multisample16x,
			_ => MultisampleAmount.MultisampleNone,
		};

		// Native WorldDirection points toward the light.
		var lighting = frame.World.Lighting;
		rc.RenderContactShadows( graphicsView, frame.ContactShadowMask, frame.View.WorldToProjection, -lighting.SunDirection, lighting.SunShadowHardness, steps );
	}

	public override void Dispose()
	{
		graphicsView?.Dispose();
		graphicsView = null;
	}
}
