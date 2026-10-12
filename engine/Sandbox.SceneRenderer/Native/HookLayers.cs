using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The camera's managed render stages as native layers: each calls <see cref="Graphics.OnLayer"/> for its stage, which runs
/// the camera's command lists, post processing and UI. The screen-space UI stages blend in gamma space (<c>ui_gamma_blend</c>).
/// </summary>
internal static class HookLayers
{
	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	delegate void OnRenderCallback( ManagedRenderSetup_t setup );

	static readonly StringToken ForwardMode = "Forward";
	static readonly StringToken UIGammaOutput = "UIGammaOutput";
	static readonly StringToken UIFrameGrabEncoded = "UIFrameGrabEncoded";
	static readonly StringToken SrgbTransfer = "D_SRGB_TRANSFER";

	static readonly Stage GameUI = (Stage)(-1);

	/// <summary>
	/// One callback per stage, made once: the layers hold nothing else.
	/// </summary>
	static readonly (Stage Stage, DelegateFunctionPointer Callback)[] Callbacks = MakeCallbacks();

	static (Stage, DelegateFunctionPointer)[] MakeCallbacks()
	{
		var stages = Enum.GetValues<Stage>();
		var callbacks = new (Stage, DelegateFunctionPointer)[stages.Length + 1];
		callbacks[0] = (GameUI, DelegateFunctionPointer.Get<OnRenderCallback>( setup => Graphics.OnLayer( (int)GameUI, setup ) ));

		for ( int i = 0; i < stages.Length; i++ )
		{
			var stage = stages[i];
			callbacks[i + 1] = (stage, DelegateFunctionPointer.Get<OnRenderCallback>( setup => Graphics.OnLayer( (int)stage, setup ) ));
		}

		return callbacks;
	}

	static DelegateFunctionPointer CallbackFor( Stage stage )
	{
		foreach ( var (s, callback) in Callbacks )
		{
			if ( s == stage ) return callback;
		}

		throw new ArgumentException( $"No render stage {stage}" );
	}

	/// <summary>
	/// A layer that runs the camera's managed rendering for <paramref name="stage"/> into <paramref name="color"/> and
	/// <paramref name="depth"/>, bound through <paramref name="colorSpace"/>.
	/// </summary>
	public static ISceneLayer Add( ISceneView view, string name, Stage stage, RenderViewport viewport,
		SceneViewRenderTargetHandle color, SceneViewRenderTargetHandle depth, RenderColorSpace colorSpace = RenderColorSpace.RENDER_SRGB )
	{
		var layer = view.AddManagedProceduralLayer( name, viewport, CallbackFor( stage ), default, true );
		layer.SetShaderMode( ForwardMode );
		layer.SetOutput( color, depth, colorSpace );
		layer.m_nLayerFlags |= LayerFlags.NeverRemove | LayerFlags.NeedsPerViewLightingConstants;
		return layer;
	}

	/// <summary>
	/// The screen-space UI layers for a UI stage (EarlyUI, UI, GameUI) over <paramref name="color"/>. Returns the layer that
	/// writes it.
	/// <para>
	/// With <c>ui_gamma_blend</c> the UI shaders write sRGB-encoded output (<c>UIGammaOutput</c>) and the blend unit works on
	/// those bytes. An 8-bit sRGB target already holds them, so the layer is just bound through a linear view. A float target has
	/// no sRGB view and holds linear HDR, so the UI draws into a temporary float target seeded with the encoded scene, then gets
	/// decoded back wherever it drew. The encode has no clamp, so HDR colours survive that.
	/// </para>
	/// </summary>
	public static ISceneLayer AddUI( ISceneView view, string name, Stage stage, RenderViewport viewport, SceneViewRenderTargetHandle color )
	{
		var gamma = RenderPipeline.UIGammaBlend;
		var srgbTarget = RenderPipelineTools.TargetSupportsSrgbWrite( view, color );

		// The swap chain can't be sampled unless it's sRGB, so the overlay never takes the float path
		if ( !gamma || srgbTarget || stage == GameUI )
		{
			gamma = gamma && srgbTarget;
			var layer = Add( view, name, stage, viewport, color, NativeRenderPipeline.NoTarget, gamma ? RenderColorSpace.RENDER_LINEAR : RenderColorSpace.RENDER_SRGB );
			layer.GetRenderAttributesPtr().SetIntValue( UIGammaOutput, gamma ? 1 : 0 );
			return layer;
		}

		var width = RenderPipelineTools.GetTargetWidth( view, color );
		var height = RenderPipelineTools.GetTargetHeight( view, color );
		var uiColor = RenderPipelineTools.FindOrCreateScratchTarget( view, stage == Stage.EarlyUI ? "ui_gamma_early_color" : "ui_gamma_color",
			width, height, ImageFormat.RGBA16161616F, RenderMultisampleType.RENDER_MULTISAMPLE_NONE, false );

		var seed = view.AddBlitTextureToTargetLayer( "UI Gamma Seed", color, viewport, viewport, uiColor, RenderColorSpace.RENDER_SRGB );
		seed.GetRenderAttributesPtr().SetComboValue( SrgbTransfer, 1 );
		seed.m_nLayerFlags |= LayerFlags.DiscardColorBuffers; // first write to the scratch

		// Linear so render targets the UI binds meanwhile (panel layers) store its encoded output as-is too
		var ui = Add( view, name, stage, viewport, uiColor, NativeRenderPipeline.NoTarget, RenderColorSpace.RENDER_LINEAR );
		var attributes = ui.GetRenderAttributesPtr();
		attributes.SetIntValue( UIGammaOutput, 1 );
		attributes.SetIntValue( UIFrameGrabEncoded, 1 ); // grabs of the float target are already gamma-space

		var composite = view.AddBlitTextureToTargetLayer( "UI Gamma Composite", uiColor, viewport, viewport, color, RenderColorSpace.RENDER_SRGB );
		composite.GetRenderAttributesPtr().SetComboValue( SrgbTransfer, 2 );
		composite.m_nLayerFlags &= ~LayerFlags.DiscardColorBuffers; // blends over the scene
		return composite;
	}
}
