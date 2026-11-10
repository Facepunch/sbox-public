using NativeEngine;

namespace Sandbox.SceneRenderer.Gpu;

/// <summary>
/// Frame target and optional final copy to a swap chain or texture.
/// </summary>
internal readonly struct RenderOutput
{
	/// <summary>
	/// What the view draws into.
	/// </summary>
	public ViewTarget Target { get; init; }

	/// <summary>
	/// Optional swap-chain destination.
	/// </summary>
	public SwapChainHandle_t SwapChain { get; init; }

	/// <summary>
	/// Optional texture destination for the HDR copy.
	/// </summary>
	public Texture Texture { get; init; }

	/// <summary>
	/// MSAA scratch for final stages, preserving compatible depth before resolving into <see cref="Texture"/>
	/// (<c>CCameraRenderer::RenderToLayer</c>).
	/// </summary>
	public Texture Scratch { get; init; }

	/// <summary>
	/// Whether the target is copied into a swap chain or texture at the end.
	/// </summary>
	public bool HasCopy => (IntPtr)SwapChain != IntPtr.Zero || Texture is not null;

	/// <summary>
	/// Full target size in pixels.
	/// </summary>
	public Vector2 Size => Target.Size;

	/// <summary>
	/// Samples per pixel. The shaders read it (<c>g_nMSAASampleCount</c>).
	/// </summary>
	public int Samples => Target.Samples;

	/// <summary>
	/// A swap chain frame: drawn into the render system's HDR target, then copied in.
	/// </summary>
	public static RenderOutput ForSwapChain( ViewTarget hdr, SwapChainHandle_t swapChain ) => new() { Target = hdr, SwapChain = swapChain };

	/// <summary>
	/// A frame into a texture: drawn into an HDR target, then copied in.
	/// </summary>
	public static RenderOutput ForTexture( ViewTarget hdr, Texture texture, Texture scratch = null ) => new() { Target = hdr, Texture = texture, Scratch = scratch };

	public static RenderOutput ForTarget( ViewTarget target ) => new() { Target = target };

	/// <summary>
	/// Native swap-chain multisample type.
	/// </summary>
	public static RenderMultisampleType SwapChainMultisample( SwapChainHandle_t swapChain ) => g_pRenderDevice.GetSwapChainInfo( swapChain ).m_nMultisampleType;
}
