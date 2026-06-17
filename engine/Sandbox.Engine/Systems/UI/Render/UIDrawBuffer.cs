using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// Thread-local buffer for collecting UI draw descriptors during panel.OnDraw().
/// Descriptors are routed directly to the active RenderLayer.
/// </summary>
internal class UIDrawBuffer
{
	[ThreadStatic] static UIDrawBuffer _current;

	internal static UIDrawBuffer Current => _current ??= new();

	/// <summary>
	/// The target layer for draw calls. Set by the renderer before OnDraw().
	/// </summary>
	public RenderLayer ActiveLayer;

	/// <summary>
	/// The scale factor from logical to physical pixels for the current panel. Set before OnDraw().
	/// </summary>
	public float ScaleToScreen = 1f;

	/// <summary>
	/// Accumulated CSS opacity for the current panel. Set before OnDraw().
	/// </summary>
	public float Opacity = 1f;

	/// <summary>
	/// Active blend mode override for the current panel. Set before OnDraw().
	/// </summary>
	public BlendMode OverrideBlendMode = BlendMode.Normal;

	public void AddBox( in BoxDrawDescriptor desc )
	{
		ActiveLayer.AddBox( desc );
	}

	public void AddShadow( in ShadowDrawDescriptor desc )
	{
		ActiveLayer.AddShadow( desc );
	}

	public void AddOutline( in OutlineDrawDescriptor desc )
	{
		ActiveLayer.AddOutline( desc );
	}

	public void AddBackdrop( in BackdropDrawDescriptor desc )
	{
		ActiveLayer.Backdrops.Add( desc );
	}
}
