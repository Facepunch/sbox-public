using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	PaintCache _paintCache;

	/// <summary>
	/// Style and geometry resolved during layout for the next paint.
	/// </summary>
	struct PaintCache
	{
		bool _dirty;
		Length? _fontSize;
		Length _rootFontSize;
		Color? _fontColor;
		string _mixBlendMode;
		ImageRendering? _imageRendering;
		Rect _rect;
		Rect _outerRect;
		Matrix? _parentMatrix;
		internal ImagePlacement Background;
		FilterMode _sampling;

		internal BorderRadii OuterRadii;
		internal BorderRadii InnerRadii;
		internal Vector4 FillInsets;
		internal float OutlineWidth;
		internal float OutlineOffset;
		internal Color OutlineColor;
		internal Matrix ClipTransform;
		internal Rect Bounds;
		internal Rect ContentClipRect;
		internal float Opacity;
		internal BlendMode? Blend;
		internal bool ClipsChildren;
		internal bool HasBackground;
		internal bool HasBackdrop;
		internal bool HasFilter;
		internal bool HasBorder;
		internal bool ClipsBackgroundToText;
		internal Painter.Filter Backdrop;
		internal LayerPaint Layer;

		/// <summary>
		/// Called during style cascading to detect inherited values that require the cached paint settings to be rebuilt.
		/// </summary>
		internal readonly bool InheritedStylesChanged( Panel panel )
		{
			var style = panel.ComputedStyle;
			return _fontSize != style.FontSize || _rootFontSize != Length.RootFontSize
				|| _mixBlendMode != style.MixBlendMode || _imageRendering != style.ImageRendering
				|| (style.HasCurrentColor && _fontColor != style.FontColor)
				|| style.CssWide?.ContainsValue( CssWideKeyword.Inherit ) == true;
		}

		/// <summary>
		/// Called by layout when paint-affecting styles change. Marks the cache dirty and refreshes visibility flags
		/// needed before final geometry, including whether backgrounds, borders and filters have anything to draw.
		/// </summary>
		internal void Invalidate( Panel panel )
		{
			_dirty = true;
			var style = panel.ComputedStyle;
			// Inline layout needs these flags before the box geometry is finalized.
			HasBackground = style.BackgroundColor.Value.a > 0 || style.BorderImageSource is not null
				|| !style.BackgroundGradient.ColorOffsets.IsDefaultOrEmpty
				|| (style.BackgroundImage is not null && style.BackgroundImage != Texture.Invalid)
				|| (style.BorderLeftColor.Value.a > 0 && style.UsedBorderLeftWidth.Value.GetPixels( 1 ) > 0)
				|| (style.BorderTopColor.Value.a > 0 && style.UsedBorderTopWidth.Value.GetPixels( 1 ) > 0)
				|| (style.BorderRightColor.Value.a > 0 && style.UsedBorderRightWidth.Value.GetPixels( 1 ) > 0)
				|| (style.BorderBottomColor.Value.a > 0 && style.UsedBorderBottomWidth.Value.GetPixels( 1 ) > 0);
			HasBackdrop = !style.IsDefault( "backdrop-filter-blur" ) || !style.IsDefault( "backdrop-filter-contrast" )
				|| !style.IsDefault( "backdrop-filter-saturate" ) || !style.IsDefault( "backdrop-filter-sepia" )
				|| !style.IsDefault( "backdrop-filter-invert" ) || !style.IsDefault( "backdrop-filter-hue-rotate" )
				|| !style.IsDefault( "backdrop-filter-brightness" );
			HasFilter = !style.IsDefault( "filter-saturate" ) || !style.IsDefault( "filter-brightness" )
				|| !style.IsDefault( "filter-contrast" ) || !style.IsDefault( "filter-blur" ) || !style.IsDefault( "filter-sepia" )
				|| !style.IsDefault( "filter-hue-rotate" ) || !style.IsDefault( "filter-invert" )
				|| !style.IsDefault( "filter-tint" ) || !style.IsDefault( "filter-border-width" );
		}

		/// <summary>
		/// Used by layout to check whether style invalidation or a changed parent transform requires a cache update.
		/// </summary>
		internal readonly bool NeedsUpdate( Panel panel )
		{
			return _dirty || _parentMatrix != panel.VisualParent?.GlobalMatrix;
		}

		/// <summary>
		/// Called by Panel.TickInternal to detect resized background or mask textures and request final layout to resolve them again.
		/// </summary>
		internal void CheckTextures( Panel panel )
		{
			if ( Background.SizeChanged() | (Layer?.MaskSizeChanged() ?? false) )
			{
				_dirty = true;
				panel.SetNeedsFinalLayout();
			}
		}

		/// <summary>
		/// Called by final layout to resolve CSS background placement, borders, effects, clipping and transforms.
		/// Reuses unchanged settings while updating geometry-dependent values for the subsequent paint pass.
		/// </summary>
		internal void Update( Panel panel )
		{
			var style = panel.ComputedStyle;
			var rect = panel.Box.Rect;
			var insets = panel.GetBackgroundClipInset( style.BackgroundClip ?? BackgroundClip.BorderBox );
			var geometryChanged = _dirty || _rect.Size != rect.Size || FillInsets != insets;
			panel.PushLengthValues();

			if ( geometryChanged )
			{
				var size = (rect.Width + rect.Height) * 0.5f;
				var widths = style.GetBorderWidths( size );
				OuterRadii = style.GetBorderRadii( rect );
				InnerRadii = OuterRadii.Inner( widths );
				FillInsets = insets;
				OutlineWidth = style.OutlineWidth.Value.GetPixels( size );
				OutlineOffset = style.OutlineOffset.Value.GetPixels( size );
				OutlineColor = style.OutlineColor.Value;
				Bounds = new Rect( Vector2.Zero, rect.Size );
				Opacity = style.Opacity ?? 1;
				Blend = style.MixBlendMode is { } blend ? ParseBlendMode( blend ) : null;
				ClipsChildren = style.Overflow is not (OverflowMode.Visible or OverflowMode.ClipWhole);
				ClipsBackgroundToText = style.BackgroundClip == BackgroundClip.Text;
				_sampling = (style.ImageRendering ?? ImageRendering.Anisotropic) switch
				{
					ImageRendering.Point => FilterMode.Point,
					ImageRendering.Bilinear => FilterMode.Bilinear,
					ImageRendering.Trilinear => FilterMode.Trilinear,
					_ => FilterMode.Anisotropic
				};
				var backgroundAngle = style.BackgroundAngle.Value.GetPixels( 1 );
				var backgroundBlend = ParseBlendMode( style.BackgroundBlendMode );
				var border = Painter.ResolveBoxStroke( widths, style.BorderLeftColor.Value, style.BorderTopColor.Value,
					style.BorderRightColor.Value, style.BorderBottomColor.Value, style.BorderStyle ?? BorderStyle.Solid );
				NineSliceImage borderImage = default;
				if ( style.BorderImageSource is { } image )
				{
					var slices = new Vector4( style.BorderImageWidthLeft.Value.GetPixels( size ), style.BorderImageWidthTop.Value.GetPixels( size ),
						style.BorderImageWidthRight.Value.GetPixels( size ), style.BorderImageWidthBottom.Value.GetPixels( size ) );
					borderImage = new NineSliceImage( image, slices, style.BorderImageRepeat ?? BorderImageRepeat.Stretch,
						style.BorderImageFill ?? BorderImageFill.Unfilled, style.BorderImageTint.Value );
				}

				Backdrop = HasBackdrop ? new Painter.Filter
				{
					Brightness = style.BackdropFilterBrightness.Value.GetPixels( 1 ),
					Contrast = style.BackdropFilterContrast.Value.GetPixels( 1 ),
					Saturation = style.BackdropFilterSaturate.Value.GetPixels( 1 ),
					Sepia = style.BackdropFilterSepia.Value.GetPixels( 1 ),
					Invert = style.BackdropFilterInvert.Value.GetPixels( 1 ),
					HueRotation = style.BackdropFilterHueRotate.Value.GetPixels( 1 ),
					Blur = style.BackdropFilterBlur.Value.GetPixels( 1 )
				} : default;

				Background.Update( panel, style.BackgroundImage, style.BackgroundGradient, _sampling, backgroundAngle, backgroundBlend, FillInsets );
				Background.Descriptor.Radii = OuterRadii;
				Background.Descriptor.Stroke = border;
				Background.Descriptor.BorderImage = borderImage;
				Painter.SetBorderShape( ref Background.Descriptor, style.BorderShape );
				HasBorder = border.HasInk || Background.Descriptor.HasBorderImage;
				panel.TransformMatrix = style.BuildTransformMatrix( rect.Size );
			}

			Background.Descriptor.Rect = rect;

			var outer = panel.Box.RectOuter;
			if ( geometryChanged || _outerRect != outer )
			{
				if ( outer.Width > 1 && outer.Height > 1 && (HasFilter || style.FilterDropShadow.Count > 0 || style.MaskImage is not null || style.Isolation == UI.Isolation.Isolate) )
				{
					Layer ??= new LayerPaint();
					Layer.Update( panel, _sampling );
				}
				else
				{
					Layer = null;
				}
			}

			if ( geometryChanged || _rect != rect || _parentMatrix != panel.VisualParent?.GlobalMatrix )
				UpdateTransform( panel );

			ClipTransform = panel.GlobalMatrix ?? Matrix.Identity;
			ContentClipRect = panel.ContentClipRect;
			_fontSize = style.FontSize;
			_rootFontSize = Length.RootFontSize;
			_fontColor = style.FontColor;
			_mixBlendMode = style.MixBlendMode;
			_imageRendering = style.ImageRendering;
			_rect = rect;
			_outerRect = outer;
			_dirty = false;
		}

		/// <summary>
		/// Called by Update when panel geometry or its visual parent changes to rebuild local and global CSS transforms.
		/// The chain runs through paint layers; the painter cancels a layer's transform while drawing into it.
		/// </summary>
		void UpdateTransform( Panel panel )
		{
			var parent = panel.VisualParent;
			var global = parent?.GlobalMatrix;
			_parentMatrix = global;
			var inverse = parent?.GlobalMatrixInverted;
			Matrix? local = null;
			var transform = inverse ?? Matrix.Identity;
			var style = panel.ComputedStyle;
			if ( !style.Transform.Value.IsEmpty() && panel.TransformMatrix != Matrix.Identity )
			{
				var rect = panel.Box.Rect;
				Vector3 origin = rect.Position;
				origin.x += style.TransformOriginX.Value.GetPixels( rect.Width, 0 );
				origin.y += style.TransformOriginY.Value.GetPixels( rect.Height, 0 );
				origin = inverse?.Transform( origin ) ?? origin;
				transform *= Matrix.CreateTranslation( -origin );
				transform *= panel.TransformMatrix;
				transform *= Matrix.CreateTranslation( origin );
				var matrix = transform.Inverted;
				local = inverse.HasValue ? inverse.Value * matrix : matrix;
				global = matrix;
				inverse = transform;
			}

			if ( panel.GlobalMatrix != global ) panel.SetGlobalMatrix( global, inverse );
			if ( panel.LocalMatrix != local ) panel.LocalMatrix = local;
		}

		/// <summary>
		/// Used by Update to map supported CSS blend names to renderer modes, falling back to normal blending.
		/// </summary>
		static BlendMode ParseBlendMode( string mode )
		{
			return mode switch
			{
				"lighten" => BlendMode.Lighten,
				"multiply" => BlendMode.Multiply,
				_ => BlendMode.Normal
			};
		}

		internal struct ImagePlacement
		{
			internal bool HasFill;
			internal Texture Texture;
			internal Painter.BoxDescriptor Descriptor;
			Vector2 _size;
			int _version;

			/// <summary>
			/// Called by CheckTextures to detect intrinsic background size changes since the descriptor was built.
			/// </summary>
			internal bool SizeChanged()
			{
				if ( Texture is null || Texture.DirtyVersion == _version ) return false;
				_version = Texture.DirtyVersion;
				return _size != Texture.Size;
			}

			/// <summary>
			/// Called by the paint-cache Update method to resolve the CSS background image or gradient into a box descriptor.
			/// Stores the texture size and version for subsequent layout invalidation checks.
			/// </summary>
			internal void Update( Panel panel, Texture texture, in GradientInfo gradient, FilterMode sampling, float angle, BlendMode blend, in Vector4 fillInsets )
			{
				if ( texture is not null && texture == Texture.Invalid ) texture = null;
				panel.FindRootPanel()?.PushRootValues();
				panel.PushLengthValues();
				var style = panel.ComputedStyle;
				var tile = texture is not null || !gradient.ColorOffsets.IsDefaultOrEmpty ? ImageRect.Calculate( new ImageRect.Input
				{
					ScaleToScreen = panel.ScaleToScreen,
					Image = texture,
					PanelRect = panel.Box.Rect,
					DefaultSize = Length.Auto,
					ImagePositionX = style.BackgroundPositionX,
					ImagePositionY = style.BackgroundPositionY,
					ImageSizeX = style.BackgroundSizeX,
					ImageSizeY = style.BackgroundSizeY
				} ).Rect : default;
				var fill = new Fill( style.BackgroundColor.Value, texture, gradient, tile, style.BackgroundTint.Value,
					style.BackgroundRepeat ?? BackgroundRepeat.Repeat, sampling, angle, blend );
				Descriptor = fill.CreateDescriptor( panel.Box.Rect, 1, BlendMode.Normal, fillInsets );
				HasFill = !fill.IsTransparent;
				Texture = texture;
				_size = texture?.Size ?? default;
				_version = texture?.DirtyVersion ?? 0;
			}
		}
	}
}
