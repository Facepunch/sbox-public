using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

public partial class Panel
{
	string PanelLayerRTName => field ??= $"PanelLayer.{GetHashCode()}";

	internal bool HasPanelLayer => _paintCache.Layer is not null;

	/// <summary>
	/// Called by Render after closing a panel's offscreen target to composite it into the parent destination.
	/// Applies the cached CSS filter, mask, drop shadows and layer border to the completed subtree.
	/// </summary>
	void DrawLayer( Painter painter )
	{
		var layer = _paintCache.Layer;
		painter.Composite( new RenderTargetHandle { Name = PanelLayerRTName }, Box.RectOuter, layer.Filter, layer.Mask,
			layer.MaskScope, CollectionsMarshal.AsSpan( layer.DropShadows ), layer.BorderWidth, layer.BorderColor );
	}

	sealed class LayerPaint
	{
		internal Painter.Filter Filter;
		internal Painter.Mask? Mask;
		internal MaskScope MaskScope;
		internal ShadowList DropShadows;
		internal float BorderWidth;
		internal Color BorderColor;
		Texture _maskImage;
		Vector2 _maskSize;
		int _maskVersion;

		internal bool MaskSizeChanged()
		{
			if ( _maskImage is null || _maskImage.DirtyVersion == _maskVersion ) return false;
			_maskVersion = _maskImage.DirtyVersion;
			return _maskSize != _maskImage.Size;
		}

		internal void Update( Panel panel, FilterMode sampling )
		{
			var style = panel.ComputedStyle;
			Filter = new Painter.Filter
			{
				Blur = style.FilterBlur.Value.GetPixels( 1 ),
				Saturation = style.FilterSaturate.Value.GetFraction( 1 ),
				Sepia = style.FilterSepia.Value.GetFraction( 1 ),
				Brightness = style.FilterBrightness.Value.GetPixels( 1 ),
				Contrast = style.FilterContrast.Value.GetPixels( 1 ),
				Invert = style.FilterInvert.Value.GetPixels( 1 ),
				HueRotation = style.FilterHueRotate.Value.GetPixels( 1 ),
				Tint = style.FilterTint ?? Vector4.One
			};
			Mask = null;
			if ( style.MaskImage is { } image )
			{
				var tile = ImageRect.Calculate( new ImageRect.Input
				{
					ScaleToScreen = panel.ScaleToScreen,
					Image = image,
					PanelRect = panel.Box.RectOuter,
					DefaultSize = Length.Auto,
					ImagePositionX = style.MaskPositionX,
					ImagePositionY = style.MaskPositionY,
					ImageSizeX = style.MaskSizeX,
					ImageSizeY = style.MaskSizeY
				} ).Rect;
				var rect = new Rect( panel.Box.RectOuter.Left + tile.x, panel.Box.RectOuter.Top + tile.y, tile.z, tile.w );
				Mask = new Painter.Mask( image, rect, style.MaskMode ?? MaskMode.MatchSource, style.MaskRepeat ?? BackgroundRepeat.Repeat,
					(style.MaskAngle?.GetPixels( 1 ) ?? 0) * (180f / MathF.PI), sampling );
			}

			MaskScope = style.MaskScope ?? UI.MaskScope.Default;
			DropShadows = style.FilterDropShadow;
			BorderWidth = style.FilterBorderWidth.Value.GetPixels( 1 ) * panel.ScaleToScreen;
			BorderColor = style.FilterBorderColor.Value;
			_maskImage = style.MaskImage;
			_maskSize = _maskImage?.Size ?? default;
			_maskVersion = _maskImage?.DirtyVersion ?? 0;
		}
	}
}
