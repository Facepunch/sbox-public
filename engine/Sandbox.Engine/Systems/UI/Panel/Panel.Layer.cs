using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

public partial class Panel
{
	string PanelLayerRTName => field ??= $"PanelLayer.{GetHashCode()}";

	internal bool HasPanelLayer => _paintCache.Layer is not null;

	/// <summary>
	/// The offscreen target's extent, re-measured now rather than read from the paint cache: it
	/// depends on the whole subtree, and a descendant that resizes - a text-width badge is the
	/// everyday case - never dirties this panel's own geometry.
	/// </summary>
	internal Rect PanelLayerBounds => _paintCache.Layer.Measure( this );

	/// <summary>
	/// Called by Render after closing a panel's offscreen target to composite it into the parent destination.
	/// Applies the cached CSS filter, mask, drop shadows and layer border to the completed subtree.
	/// </summary>
	void DrawLayer( Painter painter )
	{
		var layer = _paintCache.Layer;
		// Bounds, not PanelLayerBounds: Render measured the subtree when it opened the target,
		// and the composite has to land on exactly the rect that was rendered into.
		painter.Composite( new RenderTargetHandle { Name = PanelLayerRTName }, layer.Bounds, layer.Filter, layer.Mask,
			layer.MaskScope, CollectionsMarshal.AsSpan( layer.DropShadows ), layer.BorderWidth, layer.BorderColor );
	}

	sealed class LayerPaint
	{
		internal Rect Bounds;
		internal Painter.Filter Filter;
		internal Painter.Mask? Mask;
		internal MaskScope MaskScope;
		internal ShadowList DropShadows;
		internal float BorderWidth;
		internal Color BorderColor;
		Texture _maskImage;
		Vector2 _maskSize;
		int _maskVersion;

		/// <summary>
		/// Re-measure the subtree and remember it, for the composite that follows. Called once a
		/// frame, from <see cref="Panel.PanelLayerBounds"/>.
		/// </summary>
		internal Rect Measure( Panel panel ) => Bounds = CalculateBounds( panel );

		internal bool MaskSizeChanged()
		{
			if ( _maskImage is null || _maskImage.DirtyVersion == _maskVersion ) return false;
			_maskVersion = _maskImage.DirtyVersion;
			return _maskSize != _maskImage.Size;
		}

		internal void Update( Panel panel, FilterMode sampling )
		{
			var style = panel.ComputedStyle;
			Bounds = CalculateBounds( panel );
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

		/// <summary>
		/// Fits the margin box, its outset shadows and anything the subtree paints outside that
		/// box inside an integer-sized offscreen target.
		/// </summary>
		static Rect CalculateBounds( Panel panel )
		{
			var bounds = InkBounds( panel );

			// Round outward to integer target pixels without clipping fractional shadows.
			bounds.Left = MathF.Floor( bounds.Left );
			bounds.Top = MathF.Floor( bounds.Top );
			bounds.Right = MathF.Ceiling( bounds.Right );
			bounds.Bottom = MathF.Ceiling( bounds.Bottom );
			return bounds;
		}

		/// <summary>
		/// What this panel and its unclipped descendants actually paint. Children routinely draw
		/// outside their parent - a badge hung off a corner at a negative offset is the everyday
		/// case - and the target has to hold them, or they land outside it and are cut away.
		/// </summary>
		static Rect InkBounds( Panel panel )
		{
			var bounds = panel.Box.RectOuter;

			foreach ( var shadow in panel.ComputedStyle.BoxShadow )
			{
				if ( shadow.Inset || shadow.Color.a <= 0 ) continue;

				// Match the renderer's shadow quad: border box + spread + three-sigma blur.
				var shape = (panel.Box.Rect + new Vector2( shadow.OffsetX, shadow.OffsetY )).Grow( shadow.Spread );
				bounds.Add( shape.Grow( MathF.Ceiling( shadow.Blur * 1.5f ) ) );
			}

			// A panel that clips its own children keeps whatever they overhang to itself.
			if ( panel._paintCache.ClipsChildren || panel._children is null )
				return bounds;

			foreach ( var child in panel._children )
			{
				if ( child is null || !child.IsVisible || child.ComputedStyle is null ) continue;

				bounds.Add( InkBounds( child ) );
			}

			return bounds;
		}
	}
}
