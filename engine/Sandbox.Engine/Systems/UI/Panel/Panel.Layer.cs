using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

public partial class Panel
{
	string PanelLayerRTName => field ??= $"PanelLayer.{GetHashCode()}";

	internal bool HasPanelLayer => _paintCache.Layer is not null;
	internal Rect PanelLayerBounds => _paintCache.Layer.Bounds;

	/// <summary>
	/// Called by Render after closing a panel's offscreen target to composite it into the parent destination.
	/// Applies the cached CSS filter, mask, drop shadows and layer border to the completed subtree.
	/// </summary>
	void DrawLayer( Painter painter )
	{
		var layer = _paintCache.Layer;
		painter.Composite( new RenderTargetHandle { Name = PanelLayerRTName }, PanelLayerBounds, layer.Filter, layer.Mask,
			layer.MaskScope, CollectionsMarshal.AsSpan( layer.DropShadows ), layer.BorderWidth, layer.BorderColor );
	}

	sealed class LayerPaint
	{
		internal Rect Bounds; // The panel box padded by the filters' reach
		internal Painter.Filter Filter;
		internal Painter.Mask? Mask;
		internal MaskScope MaskScope;
		internal ShadowList DropShadows;
		internal float BorderWidth;
		internal Color BorderColor;
		internal int Mips = 1;
		internal (int Hash, ulong Frame) Cache;
		float _padding;
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
			var padding = MathF.Max( Filter.Blur * 3, BorderWidth );
			var sigma = Filter.Blur;
			foreach ( var shadow in DropShadows )
			{
				padding = MathF.Max( padding, MathF.Max( MathF.Abs( shadow.OffsetX ), MathF.Abs( shadow.OffsetY ) ) + shadow.Blur * 3 + 1 );
				sigma = MathF.Max( sigma, shadow.Blur );
			}
			// Pad in power of two steps so a changing blur only reallocates when it crosses one, and the padding follows it back down.
			// Back to no blur and no padding, drop both
			_padding = padding > 0 ? MathF.Min( 1024, MathF.Max( 64, MathF.Pow( 2, MathF.Ceiling( MathF.Log2( padding ) ) ) ) ) : 0;
			Bounds = CalculateBounds( panel ).Grow( _padding );
			Mips = sigma > 0 ? 16 : 1;
			_maskImage = style.MaskImage;
			_maskSize = _maskImage?.Size ?? default;
			_maskVersion = _maskImage?.DirtyVersion ?? 0;
		}

		/// <summary>
		/// Fits the margin box and outset shadows inside an integer-sized offscreen target.
		/// </summary>
		static Rect CalculateBounds( Panel panel )
		{
			var bounds = panel.Box.RectOuter;
			foreach ( var shadow in panel.ComputedStyle.BoxShadow )
			{
				if ( shadow.Inset || shadow.Color.a <= 0 ) continue;

				// Match the renderer's shadow quad: border box + spread + three-sigma blur.
				var shape = (panel.Box.Rect + new Vector2( shadow.OffsetX, shadow.OffsetY )).Grow( shadow.Spread );
				bounds.Add( shape.Grow( MathF.Ceiling( shadow.Blur * 1.5f ) ) );
			}

			// Round outward to integer target pixels without clipping fractional shadows.
			bounds.Left = MathF.Floor( bounds.Left );
			bounds.Top = MathF.Floor( bounds.Top );
			bounds.Right = MathF.Ceiling( bounds.Right );
			bounds.Bottom = MathF.Ceiling( bounds.Bottom );
			return bounds;
		}
	}
}
