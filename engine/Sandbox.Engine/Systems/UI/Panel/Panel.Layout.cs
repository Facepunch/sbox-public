using Sandbox.Audio;

namespace Sandbox.UI;

public partial class Panel
{
	internal PanelLayout LayoutTree;
	internal InlineParagraph InlineParagraph;
	internal InlineParagraph InlineOwner;

	private void UpdateInlineParagraph()
	{
		if ( Sandbox.UI.InlineParagraph.CanFormat( this ) )
		{
			InlineParagraph ??= new InlineParagraph( this );
			InlineParagraph.Update();
			LayoutTree.Node.InlineContent = InlineParagraph;
		}
		else if ( InlineParagraph is not null )
		{
			LayoutTree.Node.InlineContent = null;
			InlineParagraph.Dispose();
			InlineParagraph = null;
			MarkRenderDirty();
		}
	}

	/// <summary>
	/// Access to various bounding boxes of this panel.
	/// </summary>
	[Hide]
	public Box Box { get; init; } = new Box();

	/// <summary>
	/// If true, calls <see cref="DrawContent(PanelRenderer, ref RenderState)"/>.
	/// </summary>
	[Hide, Obsolete( "Use Draw" )]
	public virtual bool HasContent => false;

	/// <summary>
	/// The velocity of the current scroll
	/// </summary>
	[Hide]
	public Vector2 ScrollVelocity;

	/// <summary>
	/// Offset of the panel's children position for scrolling purposes.
	/// </summary>
	[Hide]
	public Vector2 ScrollOffset { get; set; }

	/// <summary>
	/// Scale of the panel on the screen.
	/// </summary>
	[Hide]
	public float ScaleToScreen { get; internal set; } = 1.0f;

	/// <summary>
	/// Inverse scale of <see cref="ScaleToScreen"/>.
	/// </summary>
	[Hide]
	public float ScaleFromScreen => 1.0f / ScaleToScreen;

	int LayoutCount = 0;


	/// <summary>
	/// If this panel has transforms, they'll be reflected here
	/// </summary>
	[Hide]
	public Matrix? LocalMatrix { get; internal set; }

	/// <summary>
	/// If this panel or its parents have transforms, they'll be compounded here.
	/// </summary>
	[Hide]
	public Matrix? GlobalMatrix
	{
		get;
		internal set
		{
			field = value;
			_globalMatrixInverted = null;
		}
	}

	Matrix? _globalMatrixInverted;

	/// <summary>
	/// Cached inverse of <see cref="GlobalMatrix"/>. Null when GlobalMatrix is null.
	/// </summary>
	internal Matrix? GlobalMatrixInverted
	{
		get
		{
			if ( GlobalMatrix is not { } m )
				return null;

			_globalMatrixInverted ??= m.Inverted;
			return _globalMatrixInverted;
		}
	}

	/// <summary>
	/// Set <see cref="GlobalMatrix"/> along with an already known inverse, so it doesn't need computing again.
	/// </summary>
	internal void SetGlobalMatrix( Matrix? matrix, Matrix? inverted )
	{
		GlobalMatrix = matrix;
		_globalMatrixInverted = inverted;
	}

	/// <summary>
	/// The matrix that is applied as a result of transform: styles
	/// </summary>
	[Hide]
	internal Matrix TransformMatrix { get; set; }

	/// <summary>
	/// The computed style has a non-default backdrop filter property
	/// </summary>
	[Hide]
	internal bool HasBackdropFilter { get; private set; }

	[Hide]
	internal bool HasFilter { get; private set; }

	[Hide]
	internal bool HasCustomDraw => CachedDescriptors?.CustomEntries.Count > 0;

	/// <summary>
	/// The computed style has a renderable background
	/// </summary>
	[Hide]
	internal bool HasBackground { get; private set; }

	internal void UpdateVisibility()
	{
		bool old = IsVisible;

		IsVisibleSelf = ComputedStyle?.CalcVisible() ?? false;
		IsVisibleSelf = IsVisibleSelf || HasActiveTransitions;
		IsVisible = IsVisibleSelf && (Parent?.IsVisible ?? true);

		if ( old == IsVisible )
			return;

		if ( Parent != null )
		{
			Parent.IndexesDirty = true;
		}

		var c = _children?.Count ?? 0;

		for ( int i = 0; i < c; i++ )
		{
			_children[i].UpdateVisibility();
		}

		try
		{
			OnVisibilityChanged();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e );
		}
	}

	/// <summary>
	/// Called when the visibility of the current panel changes. This could be because our own style changed, or a parent style.
	/// You can check visibility using <see cref="IsVisible"/> and <see cref="IsVisibleSelf"/>.
	/// </summary>
	protected virtual void OnVisibilityChanged()
	{

	}

	bool needsPreLayout = true;
	bool needsFinalLayout = true;

	internal void SetNeedsPreLayout()
	{
		if ( needsPreLayout ) return;

		needsPreLayout = true;
		needsFinalLayout = true;

		Parent?.SetNeedsPreLayout();
	}

	/// <summary>
	/// Request the final layout pass without a style rebuild. Enough for anything that
	/// only moves content - like scrolling - where styles and layout are unaffected.
	/// </summary>
	internal void SetNeedsFinalLayout()
	{
		if ( needsFinalLayout ) return;

		needsFinalLayout = true;

		Parent?.SetNeedsFinalLayout();
	}

	internal virtual void PreLayout( LayoutCascade cascade )
	{
		if ( LayoutTree == null )
			return;

		if ( !needsPreLayout && !cascade.SelectorChanged && !cascade.ParentChanged && !LayoutTree.ReferenceSizeChanged )
			return;

		needsPreLayout = false;
		if ( cascade.Root is { } overlayRoot ) overlayRoot.FixedOverlaysDirty = true;

		if ( IndexesDirty )
		{
			UpdateChildrenIndexes();
		}


		// Inherit from what we're styled under, if that's not where we're laid out
		if ( StyleParent is { } styleParent && styleParent != Parent )
		{
			cascade.ParentStyles = styleParent.ComputedStyle;
		}

		var ownStyleChanged = Style.IsDirty;
		var hadTransitions = Transitions.HasAny;
		ComputedStyle = Style.BuildFinal( ref cascade, out bool changed );
		if ( changed )
		{
			// ResolveCssWide retains the sparse keyword map, including expanded shorthands.
			// Refresh even if a selector changes to inherit the same value we already had.
			inheritsLayoutStyle = ComputedStyle.CssWide?.ContainsValue( CssWideKeyword.Inherit ) == true;
		}
		if ( IsFixed ) cascade.ClipBackgroundToText = false;
		cascade.ParentStyles = ComputedStyle;

		PushLengthValues();
		ScaleToScreen = cascade.Scale;
		if ( this is RootPanel root ) root.PushRootValues();

		var previousOpacity = Opacity;
		Opacity = ComputedStyle.Opacity.Value * (Parent?.Opacity ?? 1.0f);
		UpdateVisibility();
		// Scrollbar width inherits by default and can change the gutter without changing our rules.
		LayoutTree.Gutter = ScrollbarGutter;

		// SelectorChanged forces BuildCached even when this panel's rules did not change (e.g. resize).
		// Only that path needs a layout-input comparison; ordinary inheritance visits must stay cheap.
		if ( !LayoutTree.Initialized || ownStyleChanged || LayoutTree.ReferenceSizeChanged
			|| (cascade.ParentChanged && inheritsLayoutStyle)
			|| (changed && (!cascade.SelectorChanged || hadTransitions || ComputedStyle.IsAnimationActive || GetLayoutStyleHash() != layoutStyleHash)) )
		{
			UpdateLayoutStyle();
		}

		if ( Opacity != previousOpacity )
		{
			IsRenderDirty = true;
		}

		if ( changed )
		{
			IsRenderDirty = true;

			if ( Parent is not null )
			{
				Parent._renderChildrenDirty = true;
			}

			HasBackdropFilter = !ComputedStyle.IsDefault( "backdrop-filter-blur" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-contrast" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-saturate" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-sepia" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-invert" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-hue-rotate" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-brightness" );

			HasFilter = !ComputedStyle.IsDefault( "filter-saturate" )
				|| !ComputedStyle.IsDefault( "filter-brightness" )
				|| !ComputedStyle.IsDefault( "filter-contrast" )
				|| !ComputedStyle.IsDefault( "filter-blur" )
				|| !ComputedStyle.IsDefault( "filter-sepia" )
				|| !ComputedStyle.IsDefault( "filter-hue-rotate" )
				|| !ComputedStyle.IsDefault( "filter-invert" )
				|| !ComputedStyle.IsDefault( "filter-tint" )
				|| !ComputedStyle.IsDefault( "filter-border-width" );

			HasBackground = ComputedStyle.BackgroundColor.Value.a > 0f
				|| ComputedStyle.BorderImageSource is not null
				|| !ComputedStyle.BackgroundGradient.ColorOffsets.IsDefaultOrEmpty
				|| (ComputedStyle.BackgroundImage is not null && ComputedStyle.BackgroundImage != Texture.Invalid)
				|| (ComputedStyle.BorderLeftColor.Value.a > 0f && ComputedStyle.BorderLeftWidth.Value.GetPixels( 1.0f ) > 0f)
				|| (ComputedStyle.BorderTopColor.Value.a > 0f && ComputedStyle.BorderTopWidth.Value.GetPixels( 1.0f ) > 0f)
				|| (ComputedStyle.BorderRightColor.Value.a > 0f && ComputedStyle.BorderRightWidth.Value.GetPixels( 1.0f ) > 0f)
				|| (ComputedStyle.BorderBottomColor.Value.a > 0f && ComputedStyle.BorderBottomWidth.Value.GetPixels( 1.0f ) > 0f);

			UpdateLayer( ComputedStyle );
		}

		UpdateOrder();

		if ( LayoutCount > 0 && !IsVisibleSelf )
		{
			// display:none must release ownership even though child style traversal is skipped.
			if ( ComputedStyle.Display == DisplayMode.None ) UpdateInlineParagraph();
			return;
		}

		if ( _children == null || _children.Count == 0 )
		{
			UpdateInlineParagraph();
			return;
		}

		// We need to tell the children to force an update if any of the parent's
		// cascading styles have changed.
		cascade.ParentChanged = cascade.ParentChanged || changed;

		// background-clip: text clips to the text of the whole subtree, so every label under it lends its own
		cascade.ClipBackgroundToText = cascade.ClipBackgroundToText || ComputedStyle.BackgroundClip == BackgroundClip.Text;

		for ( int i = 0; i < _children.Count; i++ )
		{
			_children[i].PreLayout( cascade );
		}

		//
		// Our children's 'order' properties might have changed
		// if so, tell the layout tree about the new order
		//
		SortChildrenOrder();
		UpdateInlineParagraph();
	}

	private int layoutStyleHash;
	private bool inheritsLayoutStyle;

	private int GetLayoutStyleHash()
	{
		var style = ComputedStyle;
		var hash = new HashCode();
		AddLength( style.Width );
		AddLength( style.Height );
		AddLength( style.MaxWidth );
		AddLength( style.MaxHeight );
		AddLength( style.MinWidth );
		AddLength( style.MinHeight );
		AddLength( style.Left );
		AddLength( style.Right );
		AddLength( style.Top );
		AddLength( style.Bottom );
		AddLength( style.MarginLeft );
		AddLength( style.MarginRight );
		AddLength( style.MarginTop );
		AddLength( style.MarginBottom );
		AddLength( style.PaddingLeft );
		AddLength( style.PaddingRight );
		AddLength( style.PaddingTop );
		AddLength( style.PaddingBottom );
		AddLength( style.BorderLeftWidth );
		AddLength( style.BorderRightWidth );
		AddLength( style.BorderTopWidth );
		AddLength( style.BorderBottomWidth );
		AddLength( style.FlexBasis );
		AddLength( style.RowGap );
		AddLength( style.ColumnGap );
		hash.Add( ScrollbarGutter );
		hash.Add( style.Display );
		hash.Add( style.Position );
		hash.Add( style.AspectRatio );
		hash.Add( style.FlexGrow );
		hash.Add( style.FlexShrink );
		hash.Add( style.FlexDirection );
		hash.Add( style.FlexWrap );
		hash.Add( style.AlignContent );
		hash.Add( style.AlignItems );
		hash.Add( style.AlignSelf );
		hash.Add( style.JustifyContent );
		hash.Add( style.Overflow );
		hash.Add( style.JustifyItems );
		hash.Add( style.JustifySelf );
		hash.Add( style.GridTemplateColumns );
		hash.Add( style.GridTemplateRows );
		hash.Add( style.GridAutoColumns );
		hash.Add( style.GridAutoRows );
		hash.Add( style.GridAutoFlow );
		hash.Add( style.GridColumnStart );
		hash.Add( style.GridColumnEnd );
		hash.Add( style.GridRowStart );
		hash.Add( style.GridRowEnd );
		return hash.ToHashCode();

		void AddLength( Length? length )
		{
			hash.Add( length );
			// Length's hash only includes the numeric value and unit, not the expression text.
			if ( length?.Unit == LengthUnit.Expression ) hash.Add( length.Value.ToString() );
		}
	}

	internal void UpdateLayoutStyle()
	{
		if ( ComputedStyle == null )
			return;

		layoutStyleHash = GetLayoutStyleHash();
		LayoutTree.BeginStyleUpdate();

		LayoutTree.Width = ComputedStyle.Width;
		LayoutTree.Height = ComputedStyle.Height;
		LayoutTree.MaxWidth = ComputedStyle.MaxWidth;
		LayoutTree.MaxHeight = ComputedStyle.MaxHeight;
		LayoutTree.MinWidth = ComputedStyle.MinWidth;
		LayoutTree.MinHeight = ComputedStyle.MinHeight;
		LayoutTree.Display = ComputedStyle.Display;

		LayoutTree.Left = ComputedStyle.Left;
		LayoutTree.Right = ComputedStyle.Right;
		LayoutTree.Top = ComputedStyle.Top;
		LayoutTree.Bottom = ComputedStyle.Bottom;

		LayoutTree.MarginLeft = ComputedStyle.MarginLeft;
		LayoutTree.MarginRight = ComputedStyle.MarginRight;
		LayoutTree.MarginTop = ComputedStyle.MarginTop;
		LayoutTree.MarginBottom = ComputedStyle.MarginBottom;

		LayoutTree.Gutter = ScrollbarGutter;

		LayoutTree.PaddingLeft = ComputedStyle.PaddingLeft;
		LayoutTree.PaddingRight = ComputedStyle.PaddingRight;
		LayoutTree.PaddingTop = ComputedStyle.PaddingTop;
		LayoutTree.PaddingBottom = ComputedStyle.PaddingBottom;

		LayoutTree.BorderLeftWidth = ComputedStyle.BorderLeftWidth;
		LayoutTree.BorderTopWidth = ComputedStyle.BorderTopWidth;
		LayoutTree.BorderRightWidth = ComputedStyle.BorderRightWidth;
		LayoutTree.BorderBottomWidth = ComputedStyle.BorderBottomWidth;

		LayoutTree.PositionType = ComputedStyle.Position;
		LayoutTree.AspectRatio = ComputedStyle.AspectRatio;
		LayoutTree.FlexGrow = ComputedStyle.FlexGrow;
		LayoutTree.FlexShrink = ComputedStyle.FlexShrink;
		LayoutTree.FlexDirection = ComputedStyle.FlexDirection;
		LayoutTree.FlexBasis = ComputedStyle.FlexBasis;
		LayoutTree.Wrap = ComputedStyle.FlexWrap;

		LayoutTree.AlignContent = ComputedStyle.AlignContent;
		LayoutTree.AlignItems = ComputedStyle.AlignItems;
		LayoutTree.AlignSelf = ComputedStyle.AlignSelf;
		LayoutTree.JustifyContent = ComputedStyle.JustifyContent;
		LayoutTree.Overflow = ComputedStyle.Overflow;

		LayoutTree.RowGap = ComputedStyle.RowGap;
		LayoutTree.ColumnGap = ComputedStyle.ColumnGap;

		LayoutTree.JustifyItems = ComputedStyle.JustifyItems;
		LayoutTree.JustifySelf = ComputedStyle.JustifySelf;

		// Grid (parsed by the layout engine; cheap when unchanged)
		LayoutTree.GridTemplateColumns = ComputedStyle.GridTemplateColumns;
		LayoutTree.GridTemplateRows = ComputedStyle.GridTemplateRows;
		LayoutTree.GridAutoColumns = ComputedStyle.GridAutoColumns;
		LayoutTree.GridAutoRows = ComputedStyle.GridAutoRows;
		LayoutTree.GridAutoFlow = ComputedStyle.GridAutoFlow;
		LayoutTree.GridColumnStart = ComputedStyle.GridColumnStart;
		LayoutTree.GridColumnEnd = ComputedStyle.GridColumnEnd;
		LayoutTree.GridRowStart = ComputedStyle.GridRowStart;
		LayoutTree.GridRowEnd = ComputedStyle.GridRowEnd;

		LayoutTree.CaptureReferenceSize();
		LayoutTree.Initialized = true;
	}

	/// <summary>
	/// The currently calculated opacity.
	/// This is set by multiplying our current style opacity with our parent's opacity.
	/// </summary>
	[Hide]
	public float Opacity { get; private set; } = 1.0f;

	/// <summary>
	/// This panel has just been laid out. You can modify its position now and it will affect its children.
	/// This is a useful place to restrict shit to the screen etc.
	/// </summary>
	public virtual void OnLayout( ref Rect layoutRect )
	{

	}

	int layoutHash;

	/// <summary>
	/// Takes a <see cref="LayoutCascade"/> and returns an outer rect
	/// </summary>
	public virtual void FinalLayout( Vector2 offset )
	{
		if ( ComputedStyle is null )
			return;

		if ( LayoutTree is null )
			return;

		if ( IsFixed && FindRootPanel() is { } root ) offset = root.PanelBounds.Position;

		var hash = HashCode.Combine( offset, ScrollOffset, ScrollVelocity, ComputedStyle?.Transform, Opacity, ComputedStyle.Display );
		if ( layoutHash == hash && !needsFinalLayout && !LayoutTree.HasNewLayout ) return;

		needsFinalLayout = false;
		layoutHash = hash;

		PushLengthValues();

		//if ( LayoutTree.HasNewLayout || parentPos != offset )
		{
			var previousRect = Box.Rect;

			Box.Rect = LayoutTree.LayoutRect;

			Box.Rect.Position += offset;

			OnLayout( ref Box.Rect );

			Box.Padding = LayoutTree.Padding;
			Box.Margin = LayoutTree.Margin;

			// The scrollbar gutter rides on the layout border for layout, but it's inside the clip and doesn't draw
			var border = LayoutTree.Border;
			var gutter = LayoutTree.Gutter;
			Box.Border = new Margin( border.Left - gutter.Left, border.Top, border.Right - gutter.Right, border.Bottom );

			Box.RectOuter = Box.Rect.Grow( LayoutTree.Margin.Left, LayoutTree.Margin.Top, LayoutTree.Margin.Right, LayoutTree.Margin.Bottom );
			Box.RectInner = Box.Rect.Shrink( LayoutTree.Padding.Left, LayoutTree.Padding.Top, LayoutTree.Padding.Right, LayoutTree.Padding.Bottom );
			Box.ClipRect = Box.Rect.Shrink( Box.Border.Left, Box.Border.Top, Box.Border.Right, Box.Border.Bottom );

			UpdateLayer( ComputedStyle );

			Box.Rect = Box.Rect.Floor();
			Box.RectOuter = Box.RectOuter.Floor();
			Box.RectInner = Box.RectInner.Floor();
			Box.ClipRect = Box.ClipRect.Floor();

			// Build the matrix that is generated from "transform" etc. We do this here after we have the size of the
			// panel - which should be super duper fine.
			TransformMatrix = ComputedStyle.BuildTransformMatrix( Box.Rect.Size );

			if ( previousRect != Box.Rect )
			{
				IsRenderDirty = true;
			}
		}

		//
		// If we have an intro flag, we need to turn it off
		// because by now it's been on for one frame
		//
		if ( HasIntro )
		{
			// A nice optimization here would be to not dirty the
			// style selector if none of our styles have a :intro flag
			Switch( PseudoClass.Intro, false );
		}

		if ( ComputedStyle.Display == DisplayMode.None ) return;
		if ( LayoutCount > 0 && Opacity <= 0.0f ) return;

		// The initial state should be true for these panels
		// So there is no need to manually scroll to the bottom for scroll to be pinned there by default
		if ( LayoutCount == 0 && PreferScrollToBottom )
		{
			IsScrollAtBottom = true;
		}

		bool wasScrollatBottom = IsScrollAtBottom;

		_laidOutScrollOffset = ScrollOffset.SnapToGrid( 1.0f );
		offset = Box.Rect.Position - _laidOutScrollOffset;
		FinalLayoutChildren( offset );
		InlineParagraph?.FinalizeLayout();

		if ( wasScrollatBottom )
		{
			UpdateScrollPin();
		}

		LayoutCount++;
	}

	private void PushLengthValues()
	{
		Length.CurrentFontSize = ComputedStyle.FontSize ?? Length.Pixels( 13 ).Value;
	}

	/// <summary>
	/// The scroll offset the children were last laid out against
	/// </summary>
	Vector2 _laidOutScrollOffset;

	/// <summary>
	/// If true, we'll try to stay scrolled to the bottom when the panel changes size
	/// </summary>
	[Hide]
	public bool PreferScrollToBottom { get; set; }

	/// <summary>
	/// Whether the scrolling is currently pinned to the bottom of the panel as dictated by <see cref="PreferScrollToBottom"/>.
	/// </summary>
	[Hide]
	public bool IsScrollAtBottom { get; private set; }

	/// <summary>
	/// The size of the scrollable area within this panel.
	/// </summary>
	[Hide]
	public Vector2 ScrollSize { get; private set; }

	/// <summary>
	/// Is this panel currently being scrolled by dragging?
	/// </summary>
	[Hide]
	public bool IsDragScrolling { get; private set; }

	/// <summary>
	/// Layout the children of this panel.
	/// </summary>
	/// <param name="offset">The parent's position.</param>
	protected virtual void FinalLayoutChildren( Vector2 offset )
	{
		if ( !HasChildren )
			return;

		for ( int i = 0; i < _children.Count; i++ )
		{
			try
			{
				if ( _children[i].IsFixed ) continue;
				_children[i].FinalLayout( offset );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}

		if ( ComputedStyle.Overflow.Value == OverflowMode.Scroll )
		{
			var rect = Box.Rect;
			rect.Position -= ScrollOffset;

			// The scrollable area is our box grown to fit the children. The padding after the
			// last child scrolls with the content, so it's added to the children's extent - not
			// to the box, which already includes it. Adding it to the box made every padded scroll
			// panel scrollable by its padding even when nothing overflowed.
			Rect content = default;
			bool hasContent = false;

			for ( int i = 0; i < _children.Count; i++ )
			{
				var child = _children[i];

				if ( !child.IsVisible || child.IsFixed )
					continue;

				if ( child is ScrollBar )
					continue;

				if ( !child.TryGetLayoutRect( out var childRect ) ) continue;

				if ( hasContent ) content.Add( childRect );
				else content = childRect;

				hasContent = true;
			}

			if ( hasContent )
			{
				// Content scrolls up to the gutter, not under it
				content.Right += Box.Padding.Right + LayoutTree.Gutter.Right;
				content.Bottom += Box.Padding.Bottom;
				rect.Add( content );
			}

			ConstrainScrolling( rect.Size );
		}
		else
		{
			ScrollOffset = 0;
		}

	}

	bool TryGetLayoutRect( out Rect rect )
	{
		rect = default;
		if ( !IsVisible || IsFixed ) return false;
		if ( ComputedStyle.Display == DisplayMode.Contents )
		{
			bool hasRect = false;
			for ( int i = 0; i < (_children?.Count ?? 0); i++ )
			{
				var child = _children[i];

				if ( child.TryGetLayoutRect( out var childRect ) )
				{
					if ( !hasRect ) rect = childRect;
					else rect.Add( childRect );
					hasRect = true;
				}
			}

			return hasRect;
		}

		rect = Box.RectOuter;
		return true;
	}

	private void UpdateScrollPin()
	{
		if ( !PreferScrollToBottom )
			return;

		if ( IsScrollAtBottom )
			return;

		if ( !ScrollVelocity.y.AlmostEqual( 0, 0.1f ) )
			return;

		ScrollOffset = new Vector2( ScrollOffset.x, ScrollSize.y );
		IsScrollAtBottom = true;
		ScrollVelocity.y = 0;

	}

	bool isScrolling;
	Vector2 scrollVelocityVelocity;

	protected virtual void AddScrollVelocity()
	{
		if ( ScrollVelocity.IsNearZeroLength )
		{
			ScrollVelocity = 0;
			return;
		}

		ScrollVelocity = Vector2.SmoothDamp( ScrollVelocity, 0, ref scrollVelocityVelocity, 0.5f, RealTime.SmoothDelta );

		// Bring it to a stop
		if ( ScrollVelocity.y.AlmostEqual( 0, 0.01f ) ) ScrollVelocity.y = 0;
		if ( ScrollVelocity.x.AlmostEqual( 0, 0.01f ) ) ScrollVelocity.x = 0;
	}

	/// <summary>
	/// Reversed by flex-direction: *-reverse or justify-content: flex-end, when the scroll offset runs from -<see cref="ScrollSize"/> to zero
	/// </summary>
	internal bool IsScrollAxisReversed => ComputedStyle.JustifyContent == Justify.FlexEnd || ComputedStyle.FlexDirection == FlexDirection.RowReverse || ComputedStyle.FlexDirection == FlexDirection.ColumnReverse;

	/// <summary>
	/// Constrain <see cref="ScrollOffset">scrolling</see> to the given size.
	/// </summary>
	protected virtual void ConstrainScrolling( Vector2 size )
	{
		if ( IsDragScrolling )
			return;

		isScrolling = false;

		size -= Box.Rect.Size;

		var heightChange = size.y - ScrollSize.y;

		ScrollSize = size;
		ScrollSize = ScrollSize.SnapToGrid( 1.0f );

		var overflow = ComputedStyle.Overflow;

		if ( overflow == OverflowMode.Visible || overflow == OverflowMode.Hidden )
		{
			ScrollOffset = 0;
			return;
		}

		var so = ScrollOffset;

		// add velocity
		so += ScrollVelocity * RealTime.SmoothDelta * 60.0f;

		var axisReversed = IsScrollAxisReversed;

		IsScrollAtBottom = so.y + ScrollVelocity.y >= size.y;
		if ( ScrollVelocity.y > 0 && IsScrollAtBottom ) so.y += heightChange;

		//
		// TODO - a style to let them turn springy mode off ?
		//

		var constrainSpeed = RealTime.SmoothDelta * 100.0f;

		if ( axisReversed )
		{
			if ( so.y > 0 ) so.y = so.y.LerpTo( 0, constrainSpeed );
			if ( so.x > 0 ) so.x = so.x.LerpTo( 0, constrainSpeed );
			if ( so.y < -ScrollSize.y ) so.y = so.y.LerpTo( -ScrollSize.y, constrainSpeed );
			if ( so.x < -ScrollSize.x ) so.x = so.x.LerpTo( -ScrollSize.x, constrainSpeed );
		}
		else
		{
			if ( so.y < 0 ) so.y = so.y.LerpTo( 0, constrainSpeed );
			if ( so.x < 0 ) so.x = so.x.LerpTo( 0, constrainSpeed );
			if ( so.y > ScrollSize.y ) so.y = so.y.LerpTo( ScrollSize.y, constrainSpeed );
			if ( so.x > ScrollSize.x ) so.x = so.x.LerpTo( ScrollSize.x, constrainSpeed );
		}

		if ( ScrollOffset == so )
			return;

		ScrollOffset = so;
		isScrolling = true;
	}

	/// <summary>
	/// Play a sound from this panel.
	/// </summary>
	public void PlaySound( string sound )
	{
		if ( string.IsNullOrEmpty( sound ) )
			return;

		var h = Sound.Play( sound );
		if ( !h.IsValid() )
			return;

		if ( FindRootPanel() is WorldPanel worldPanel )
		{
			// Calculate world position of the element, not just the root WorldPanel
			var worldPosition = worldPanel.Position;
			var panelPosition = Box.Rect.Position;
			var worldRotation = worldPanel.Rotation * new Angles( 0, 90, 0 );
			var worldOffset = new Vector3( panelPosition.x, panelPosition.y, 0 );
			worldOffset = worldRotation * (worldOffset * ScenePanelObject.ScreenToWorldScale);
			h.TargetMixer = Mixer.FindMixerByName( "Game" );
			h.Position = worldPosition + worldOffset;
		}
		else
		{
			var normalizedScreenPosition = Box.Rect.Center / Screen.Size;
			normalizedScreenPosition -= 0.5f;
			h.TargetMixer = Mixer.FindMixerByName( "UI" );
			h.Position = new Vector3( 64.0f, normalizedScreenPosition.x.Clamp( -1, 1 ) * -256.0f, -normalizedScreenPosition.y.Clamp( -1, 1 ) * 64.0f );
			h.ListenLocal = true;
		}
	}

}

/// <summary>
/// Represents position and size of a <see cref="Panel"/> on the screen.
/// </summary>
[SkipHotload]
public class Box
{
	/// <summary>
	/// Position and size of the element on the screen, <b>including both - its padding AND margin</b>.
	/// </summary>
	public Rect RectOuter;

	/// <summary>
	/// Position and size of only the element's inner content on the screen, <i>without padding OR margin</i>.
	/// </summary>
	public Rect RectInner;

	/// <summary>
	/// The size of padding.
	/// </summary>
	public Margin Padding;

	/// <summary>
	/// The size of border.
	/// </summary>
	public Margin Border;

	/// <summary>
	/// The size of border.
	/// </summary>
	public Margin Margin;

	/// <summary>
	/// Position and size of the element on the screen, <b>including its padding</b>, <i>but not margin</i>.
	/// </summary>
	public Rect Rect;

	/// <summary>
	/// <see cref="Rect"/> minus the border sizes.
	/// Used internally to "clip" (hide) everything outside of these bounds, if the panels <see cref="OverflowMode"/> is not set to <see cref="OverflowMode.Visible"/>.
	/// </summary>
	public Rect ClipRect;

	/// <summary>
	/// Position of the left edge in screen coordinates.
	/// </summary>
	public float Left => Rect.Left;

	/// <summary>
	/// Position of the right edge in screen coordinates.
	/// </summary>
	public float Right => Rect.Right;

	/// <summary>
	/// Position of the top edge in screen coordinates.
	/// </summary>
	public float Top => Rect.Top;

	/// <summary>
	/// Position of the bottom edge in screen coordinates.
	/// </summary>
	public float Bottom => Rect.Bottom;
}
