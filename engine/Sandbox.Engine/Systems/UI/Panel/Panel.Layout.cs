using Sandbox.Audio;

namespace Sandbox.UI;

public partial class Panel
{
	internal YogaWrapper YogaNode;

	/// <summary>
	/// Access to various bounding boxes of this panel.
	/// </summary>
	[Hide]
	public Box Box { get; init; } = new Box();

	/// <summary>
	/// If true, calls <see cref="DrawContent( ref RenderState )"/>.
	/// </summary>
	[Hide]
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
	public Matrix? GlobalMatrix { get; internal set; }

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

	/// <summary>
	/// The computed style has a non-default filter property
	/// </summary>
	[Hide]
	internal bool HasFilter { get; private set; }

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

	internal virtual void PreLayout( LayoutCascade cascade )
	{
		if ( YogaNode == null )
			return;

		if ( !needsPreLayout && !cascade.SelectorChanged && !cascade.ParentChanged )
			return;

		needsPreLayout = false;

		if ( IndexesDirty )
		{
			UpdateChildrenIndexes();
		}


		ComputedStyle = Style.BuildFinal( ref cascade, out bool changed );
		cascade.ParentStyles = ComputedStyle;

		PushLengthValues();

		ScaleToScreen = cascade.Scale;
		Opacity = ComputedStyle.Opacity.Value * (Parent?.Opacity ?? 1.0f);
		UpdateVisibility();

		if ( changed || !YogaNode.Initialized )
		{
			UpdateYoga();
		}

		if ( changed )
		{
			backgroundRenderDirty = true;

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
			return;
		}

		if ( _children == null || _children.Count == 0 )
			return;

		// We need to tell the children to force an update if any of the parent's
		// cascading styles have changed.
		cascade.ParentChanged = cascade.ParentChanged || changed;

		for ( int i = 0; i < _children.Count; i++ )
		{
			_children[i].PreLayout( cascade );
		}

		//
		// Our children's 'order' properties might have changed
		// if so, tell yoga about the new order
		//
		SortChildrenOrder();
	}

	internal void UpdateYoga()
	{
		if ( ComputedStyle == null )
			return;

		YogaNode.Width = ComputedStyle.Width;
		YogaNode.Height = ComputedStyle.Height;
		YogaNode.MaxWidth = ComputedStyle.MaxWidth;
		YogaNode.MaxHeight = ComputedStyle.MaxHeight;
		YogaNode.MinWidth = ComputedStyle.MinWidth;
		YogaNode.MinHeight = ComputedStyle.MinHeight;
		YogaNode.Display = ComputedStyle.Display;

		YogaNode.Left = ComputedStyle.Left;
		YogaNode.Right = ComputedStyle.Right;
		YogaNode.Top = ComputedStyle.Top;
		YogaNode.Bottom = ComputedStyle.Bottom;

		YogaNode.MarginLeft = ComputedStyle.MarginLeft;
		YogaNode.MarginRight = ComputedStyle.MarginRight;
		YogaNode.MarginTop = ComputedStyle.MarginTop;
		YogaNode.MarginBottom = ComputedStyle.MarginBottom;

		YogaNode.PaddingLeft = ComputedStyle.PaddingLeft;
		YogaNode.PaddingRight = ComputedStyle.PaddingRight;
		YogaNode.PaddingTop = ComputedStyle.PaddingTop;
		YogaNode.PaddingBottom = ComputedStyle.PaddingBottom;

		YogaNode.BorderLeftWidth = ComputedStyle.BorderLeftWidth;
		YogaNode.BorderTopWidth = ComputedStyle.BorderTopWidth;
		YogaNode.BorderRightWidth = ComputedStyle.BorderRightWidth;
		YogaNode.BorderBottomWidth = ComputedStyle.BorderBottomWidth;

		YogaNode.PositionType = ComputedStyle.Position;
		YogaNode.AspectRatio = ComputedStyle.AspectRatio;
		YogaNode.FlexGrow = ComputedStyle.FlexGrow;
		YogaNode.FlexShrink = ComputedStyle.FlexShrink;
		YogaNode.FlexBasis = ComputedStyle.FlexBasis;
		YogaNode.Wrap = ComputedStyle.FlexWrap;

		YogaNode.AlignContent = ComputedStyle.AlignContent;
		YogaNode.AlignItems = ComputedStyle.AlignItems;
		YogaNode.AlignSelf = ComputedStyle.AlignSelf;
		YogaNode.FlexDirection = ComputedStyle.FlexDirection;
		YogaNode.JustifyContent = ComputedStyle.JustifyContent;
		YogaNode.Overflow = ComputedStyle.Overflow;

		YogaNode.RowGap = ComputedStyle.RowGap;
		YogaNode.ColumnGap = ComputedStyle.ColumnGap;

		YogaNode.Initialized = true;
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

		if ( YogaNode is null )
			return;

		PushLengthValues();

		var hash = HashCode.Combine( offset, ScrollOffset, ScrollVelocity, ComputedStyle?.Transform, Opacity, ComputedStyle.Display );
		if ( layoutHash == hash && !YogaNode.HasNewLayout && !needsFinalLayout ) return;

		needsFinalLayout = false;
		layoutHash = hash;

		//if ( YogaNode.HasNewLayout || parentPos != offset )
		{
			Box.Rect = YogaNode.YogaRect;

			Box.Rect.Position += offset;

			OnLayout( ref Box.Rect );

			Box.Padding = YogaNode.Padding;
			Box.Margin = YogaNode.Margin;
			Box.Border = YogaNode.Border;

			Box.RectOuter = Box.Rect.Grow( YogaNode.Margin.Left, YogaNode.Margin.Top, YogaNode.Margin.Right, YogaNode.Margin.Bottom );
			Box.RectInner = Box.Rect.Shrink( YogaNode.Padding.Left, YogaNode.Padding.Top, YogaNode.Padding.Right, YogaNode.Padding.Bottom );
			Box.ClipRect = Box.Rect.Shrink( YogaNode.Border.Left, YogaNode.Border.Top, YogaNode.Border.Right, YogaNode.Border.Bottom );

			UpdateLayer( ComputedStyle );

			Box.Rect = Box.Rect.Floor();
			Box.RectOuter = Box.RectOuter.Floor();
			Box.RectInner = Box.RectInner.Floor();
			Box.ClipRect = Box.ClipRect.Floor();

			// Build the matrix that is generated from "transform" etc. We do this here after we have the size of the
			// panel - which should be super duper fine.
			TransformMatrix = ComputedStyle.BuildTransformMatrix( Box.Rect.Size );

			backgroundRenderDirty = true;
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

		offset = Box.Rect.Position - ScrollOffset.SnapToGrid( 1.0f );
		FinalLayoutChildren( offset );

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

	bool IsDragScrolling;

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

			for ( int i = 0; i < _children.Count; i++ )
			{
				var child = _children[i];

				if ( child.IsVisible )
				{
					rect.Add( child.GetLayoutRect() );
				}
			}

			rect.Height += Box.Padding.Bottom;
			rect.Right += Box.Padding.Right;

			ConstrainScrolling( rect.Size );
		}
		else
		{
			ScrollOffset = 0;
		}

	}

	Rect GetLayoutRect()
	{
		if ( HasChildren && ComputedStyle.Display == DisplayMode.Contents )
		{
			Rect rect = default;
			for ( int i = 0; i < _children.Count; i++ )
			{
				var child = _children[i];

				if ( child.IsVisible )
				{
					if ( i == 0 ) rect = child.GetLayoutRect();
					else rect.Add( child.GetLayoutRect() );
				}
			}

			return rect;
		}

		return Box.RectOuter;
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

		// Reverse the axis if flex-direction: *-reverse or justify-content: flex-end;
		var axisReversed = ComputedStyle.JustifyContent == Justify.FlexEnd || ComputedStyle.FlexDirection == FlexDirection.RowReverse || ComputedStyle.FlexDirection == FlexDirection.ColumnReverse;

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

internal static class YogaEx
{
	public static void SetYoga( this ref Length? self, YGNodeRef _native, Func<float> dimension, Action<YGNodeRef> setAuto, Action<YGNodeRef, float> setUnit, Action<YGNodeRef, float> setPercent )
	{
		if ( !self.HasValue || self.Value.Unit == LengthUnit.Undefined )
		{
			setUnit( _native, float.NaN );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Expression )
		{
			setUnit( _native, self.Value.GetPixels( dimension() ) );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Auto )
		{
			setAuto?.Invoke( _native );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Pixels )
		{
			setUnit( _native, self.Value.Value );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Percentage )
		{
			setPercent( _native, self.Value.Value );
			return;
		}

		if ( self.Value.Unit == LengthUnit.ViewHeight || self.Value.Unit == LengthUnit.ViewWidth || self.Value.Unit == LengthUnit.ViewMin || self.Value.Unit == LengthUnit.ViewMax )
		{
			setUnit( _native, self.Value.GetPixels( 0.0f ) );
			return;
		}

		if ( self.Value.Unit == LengthUnit.RootEm || self.Value.Unit == LengthUnit.Em )
		{
			setUnit( _native, self.Value.GetPixels( dimension() ) );
			return;
		}
	}

	public static void SetYoga( this ref Length? self, YGNodeRef _native, Func<float> dimension, Action<YGNodeRef, YGEdge> setAuto, Action<YGNodeRef, YGEdge, float> setUnit, Action<YGNodeRef, YGEdge, float> setPercent, YGEdge edge )
	{
		if ( !self.HasValue || self.Value.Unit == LengthUnit.Undefined )
		{
			setUnit( _native, edge, float.NaN );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Expression )
		{
			setUnit( _native, edge, self.Value.GetPixels( dimension() ) );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Auto )
		{
			setAuto?.Invoke( _native, edge );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Pixels )
		{
			setUnit( _native, edge, self.Value.Value );
			return;
		}

		if ( self.Value.Unit == LengthUnit.Percentage )
		{
			if ( setPercent is not null )
			{
				setPercent( _native, edge, self.Value.Value );
			}
			else
			{
				setUnit( _native, edge, self.Value.GetPixels( dimension() ) );
			}

			return;
		}

		if ( self.Value.Unit == LengthUnit.ViewHeight || self.Value.Unit == LengthUnit.ViewWidth || self.Value.Unit == LengthUnit.ViewMin || self.Value.Unit == LengthUnit.ViewMax )
		{
			setUnit( _native, edge, self.Value.GetPixels( 0.0f ) );
			return;
		}

		if ( self.Value.Unit == LengthUnit.RootEm || self.Value.Unit == LengthUnit.Em )
		{
			setUnit( _native, edge, self.Value.GetPixels( dimension() ) );
			return;
		}
	}

	public static float ToFloat( this Length? self, Length? dimension )
	{
		if ( self == null ) return 0;

		if ( self.Value.Unit == LengthUnit.Expression )
			return self.Value.GetPixels( dimension?.Value ?? 0f );

		if ( self.Value.Unit == LengthUnit.Pixels )
			return self.Value.Value;

		if ( self.Value.Unit == LengthUnit.RootEm || self.Value.Unit == LengthUnit.Em )
			return self.Value.GetPixels( dimension?.Value ?? 0f );

		// TODO
		return self.Value.Value;
	}
}
