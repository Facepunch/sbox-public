using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Called by RootPanel and parent panels to record this panel and its children into the frame.
	/// Enters the panel destination, applies effects and optional layer rendering, then draws its background,
	/// content, inset shadows and outline before traversing children.
	/// </summary>
	internal void Render( Painter painter, ref RootPanel.FrameStats stats )
	{
		if ( ComputedStyle is null || !IsVisible )
		{
			stats.PanelsCulled++;
			return;
		}

		stats.Panels++;
		using var scope = EnterDestination( painter );
		painter = scope.Painter;

		var layered = HasPanelLayer;
		var layer = default( Painter.TargetScope );
		if ( layered )
		{
			stats.LayerPanels++;
			layer = painter.Target( PanelLayerRTName, Box.RectOuter );
		}

		try
		{
			if ( !LayoutTree.IsInlineParticipant )
			{
				if ( HasBackdropFilter ) painter.FilterBackdrop( Box.Rect, _paintCache.Backdrop, _paintCache.OuterRadii );
				if ( layered || this is RootPanel ) DrawShadows( inset: false, painter );
				DrawBackground( painter );
				DrawContent( painter );
				DrawShadows( inset: true, painter );
				DrawOutline( painter );
			}

			RenderChildren( painter, ref stats );
		}
		finally
		{
			if ( layered )
			{
				layer.Dispose();
				DrawLayer( painter );
			}
		}
	}

	/// <summary>
	/// Called by Render after the CSS background to invoke OnDraw and emit inline content in panel-local coordinates.
	/// Also dispatches IPanelDraw native commands and restores destination attributes afterward.
	/// </summary>
	void DrawContent( Painter painter )
	{
		if ( _hasDrawCallback || LayoutTree.HasInlineContent )
		{
			using ( var content = painter.WithContentOrigin( Box.Rect.Position ) )
			using ( var legacy = content.Painter.BindLegacy() )
			{
				try
				{
					if ( _hasDrawCallback ) OnDraw( content.Painter );
					LayoutTree?.DrawInlineContent( content.Painter );
				}
				catch ( Exception e )
				{
					Log.Error( e );
				}
			}
		}

		if ( this is IPanelDraw custom )
		{
			try
			{
				custom.Draw( painter.NativeCommands( objectSpace: true ) );
			}
			finally
			{
				painter.ApplyDestinationAttributes();
			}
		}
	}

	/// <summary>
	/// Called by Render to draw child shadows and non-fixed children in sorted order inside the child clip.
	/// Draws scrollbars afterward; RootPanel handles fixed-position panels separately.
	/// </summary>
	void RenderChildren( Painter painter, ref RootPanel.FrameStats stats )
	{
		if ( !HasChildren ) return;
		SortRenderChildren();

		using ( ClipChildren( painter, _paintCache.ContentClipRect ) )
		{
			RenderChildShadows( painter );
			foreach ( var child in _renderChildren )
			{
				if ( !child.IsFixed )
					child.Render( painter, ref stats );
			}
		}

		RenderScrollbars( painter, ref stats );
	}

	/// <summary>
	/// Called by RenderChildren before child bodies so non-fixed sibling outset shadows are painted underneath them.
	/// </summary>
	void RenderChildShadows( Painter painter )
	{
		if ( _shadowChildren is null ) return;
		foreach ( var child in _shadowChildren )
		{
			if ( child.IsFixed ) continue;
			child.RenderShadow( painter );
		}
	}

	/// <summary>
	/// Called by parent shadow passes, RootPanel fixed overlays and scrollbar rendering to draw this panel's outset shadows in its destination.
	/// Skips inline and layered panels, whose shadows are handled by their own rendering paths.
	/// </summary>
	internal void RenderShadow( Painter painter )
	{
		if ( ComputedStyle is null || !IsVisible || LayoutTree.IsInlineParticipant || ComputedStyle.BoxShadow.Count == 0 ) return;
		if ( HasPanelLayer ) return;
		using var scope = EnterDestination( painter );
		DrawShadows( inset: false, scope.Painter );
	}

	/// <summary>
	/// Called by Render and RenderShadow to enter this panel's drawing destination.
	/// Combines inherited opacity and blend mode with the cached panel settings and applies its transform.
	/// </summary>
	Painter.DestinationScope EnterDestination( Painter painter )
	{
		CachedRenderOpacity = painter.InheritedOpacity * _paintCache.Opacity;
		CachedOverrideBlendMode = _paintCache.Blend ?? painter.InheritedBlendMode;
		return painter.WithDestination( _paintCache.Bounds, ScaleToScreen,
			CachedRenderOpacity, CachedOverrideBlendMode, RenderTransform, ComputedStyle.BackgroundPlaybackPaused );
	}

	/// <summary>
	/// Called by RenderChildren and scrollbar rendering to apply the panel's overflow clip in destination coordinates.
	/// Uses the cached inner corner radii and clip transform; returns an empty scope when overflow does not clip.
	/// </summary>
	internal Painter.DestinationClipScope ClipChildren( Painter painter, Rect clipRect )
	{
		if ( !_paintCache.ClipsChildren ) return default;

		return painter.ClipDestination( clipRect, _paintCache.InnerRadii, _paintCache.ClipTransform );
	}

	/// <summary>
	/// Called by Render after content and inset shadows to draw the CSS outline around the border box.
	/// Uses the layout-resolved width, color, offset and corner radii; skips transparent or zero-width outlines.
	/// </summary>
	internal void DrawOutline( Painter painter )
	{
		if ( _paintCache.OutlineColor.a <= 0 || _paintCache.OutlineWidth <= 0 ) return;
		painter.Outline( Box.Rect, _paintCache.OutlineColor, _paintCache.OutlineWidth, _paintCache.OuterRadii, _paintCache.OutlineOffset );
	}

	/// <summary>
	/// Called by Render before content to draw the cached CSS background and border when the panel has either.
	/// </summary>
	void DrawBackground( Painter painter )
	{
		if ( HasBackground ) DrawBackgroundBox( painter, in _paintCache.Background );
	}

	/// <summary>
	/// Called by Image, SvgPanel and ScenePanel from OnDraw(Painter) to draw their texture without a content cache.
	/// Resolves sizing, position, tint, filtering and rounded clipping from the current style, draws in local coordinates,
	/// and restores the caller's Painter state. CSS borders remain in the separate background pass.
	/// </summary>
	public void DrawTexture( Painter painter, Texture texture, Length defaultSize )
	{
		if ( !texture.IsValid() || ComputedStyle is not { } style ) return;

		var rect = new Rect( Vector2.Zero, Box.Rect.Size );
		if ( rect.Width <= 0 || rect.Height <= 0 ) return;

		var tile = ImageRect.Calculate( new ImageRect.Input
		{
			Image = texture,
			PanelRect = rect,
			ScaleToScreen = ScaleToScreen,
			DefaultSize = defaultSize,
			ImageSizeX = style.BackgroundSizeX is { } sizeX && sizeX.Unit != LengthUnit.Undefined ? sizeX : defaultSize,
			ImageSizeY = style.BackgroundSizeY is { } sizeY && sizeY.Unit != LengthUnit.Undefined ? sizeY : defaultSize,
			ImagePositionX = style.BackgroundPositionX,
			ImagePositionY = style.BackgroundPositionY
		} ).Rect;

		if ( tile.z <= 0 || tile.w <= 0 ) return;

		var filter = style.ImageRendering switch
		{
			ImageRendering.Point => FilterMode.Point,
			ImageRendering.Bilinear => FilterMode.Bilinear,
			ImageRendering.Trilinear => FilterMode.Trilinear,
			_ => FilterMode.Anisotropic
		};

		using var scope = painter.Scope();
		painter.Stroke = Stroke.None;
		var border = Box.Border;
		var inset = GetBackgroundClipInset( style.BackgroundClip ?? BackgroundClip.BorderBox );

		painter.ClipFill( new Vector4( MathF.Max( border.Left, inset.x ), MathF.Max( border.Top, inset.y ),
			MathF.Max( border.Right, inset.z ), MathF.Max( border.Bottom, inset.w ) ) );

		painter.Fill = Fill.Image( texture, style.BackgroundTint.Value, tile.z, tile.w, tile.x, tile.y,
			style.BackgroundRepeat ?? BackgroundRepeat.Repeat, filter )
			.WithRotation( style.BackgroundAngle.Value.GetPixels( 1 ) * (180 / MathF.PI) );

		var radii = style.GetBorderRadii( rect );
		painter.DrawRect( rect, radii, style.BorderShape );
	}

	/// <summary>
	/// Called by DrawBackground to submit the cached CSS box without inheriting custom callback drawing state.
	/// For background-clip: text, draws the border once and masks separate background fills to visible labels.
	/// </summary>
	void DrawBackgroundBox( Painter painter, in PaintCache.ImagePlacement image )
	{
		if ( !image.HasFill && !_paintCache.HasBorder ) return;

		ref readonly var descriptor = ref image.Descriptor;
		if ( !_paintCache.ClipsBackgroundToText )
		{
			painter.Rect( in descriptor );
			return;
		}

		if ( _paintCache.HasBorder )
		{
			var border = descriptor with
			{
				Color = Color.Transparent,
				BackgroundImage = null,
				BackgroundGradient = default,
				BackgroundTint = Color.Transparent,
				BackgroundBlendMode = BlendMode.Normal,
				BackgroundClip = BackgroundClip.BorderBox,
				BackgroundClipInset = default
			};
			painter.Rect( in border );
		}

		if ( !image.HasFill ) return;

		foreach ( var label in Descendants.Prepend( this ).OfType<Label>() )
		{
			if ( !label.IsVisible || label.VisualRoot != VisualRoot ) continue;
			if ( !label.GetTextMask( out var texture, out var rect ) ) continue;
			var fill = descriptor with
			{
				BackgroundClip = BackgroundClip.Text,
				BackgroundClipInset = default,
				TextMask = texture,
				TextMaskRect = new Vector4( rect.Left - descriptor.Rect.Left, rect.Top - descriptor.Rect.Top, rect.Width, rect.Height ),
				Stroke = default,
				BorderImage = default
			};
			painter.Rect( in fill );
		}
	}

	/// <summary>
	/// Used by paint-cache layout and DrawTexture to resolve CSS background clipping.
	/// Returns border-box insets ordered left, top, right, bottom, adding padding for content-box clipping.
	/// </summary>
	Vector4 GetBackgroundClipInset( BackgroundClip clip )
	{
		if ( clip == BackgroundClip.BorderBox || clip == BackgroundClip.Text )
			return Vector4.Zero;

		var inset = Box.Border;
		if ( clip == BackgroundClip.ContentBox ) inset += Box.Padding;

		return new Vector4( inset.Left, inset.Top, inset.Right, inset.Bottom );
	}

	/// <summary>
	/// Called by Render for inset shadows and root/layer outset shadows, and by RenderShadow for ordinary outset shadows.
	/// Draws matching CSS box-shadow entries around the border box or inside the padding box using layout-resolved radii.
	/// </summary>
	internal void DrawShadows( bool inset, Painter painter )
	{
		var shadows = ComputedStyle.BoxShadow;
		var c = shadows.Count;

		if ( c == 0 )
			return;

		var rect = Box.Rect;
		var radii = _paintCache.OuterRadii;

		// Outset shadows live outside the border box, inset ones inside the padding box
		if ( inset )
		{
			radii = _paintCache.InnerRadii;
			rect = Box.ClipRect;
		}

		for ( int i = 0; i < c; i++ )
		{
			var shadow = shadows[i];
			if ( shadow.Inset != inset ) continue;
			if ( shadow.Color.a <= 0 ) continue;

			painter.RectShadow( rect, radii, color: shadow.Color, blur: shadow.Blur, spread: shadow.Spread, offset: new Vector2( shadow.OffsetX, shadow.OffsetY ), inset: inset );
		}
	}
}
