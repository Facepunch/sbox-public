using System;

namespace Sandbox.UI;

/// <summary>
/// A scrollbar for one axis of a scrolling panel. Created by the panel while that axis overflows and its
/// <c>scrollbar-width</c> is non-zero. Configured with <c>scrollbar-width</c>, <c>scrollbar-color</c> and
/// <c>scrollbar-gutter</c> on the scrolling panel, like the web.
/// </summary>
[StyleSheet.Inline( "scrollbar", Styles )]
public sealed class ScrollBar : Panel
{
	/// <summary>
	/// Thickness of <c>scrollbar-width: auto</c>, in style pixels.
	/// </summary>
	public const float AutoThickness = 12;

	/// <summary>
	/// Thickness of <c>scrollbar-width: thin</c>, in style pixels.
	/// </summary>
	public const float ThinThickness = 8;

	const float MinThumbLength = 20;

	const string Styles = """
		scrollbar
		{
			position: absolute;
			pointer-events: all;
			z-index: 10000;
			opacity: 0.5;
			transition: opacity 0.15s ease-out;
			cursor: default;
		}

		scrollbar.visible, scrollbar:hover, scrollbar.dragging { opacity: 1; }

		scrollbar > .thumb
		{
			position: absolute;
			background-color: #ffffff80;
			border-radius: 100px;
			opacity: 0.7;
			transition: opacity 0.15s ease-out;
		}

		scrollbar > .thumb:hover, scrollbar.dragging > .thumb { opacity: 1; }
		""";

	const float ThumbInset = 0.15f;

	readonly bool _vertical;
	readonly Panel _thumb;

	Panel Owner => Parent;

	public bool IsVertical => _vertical;

	bool? _shown;
	float _thickness;
	float _cornerInset;
	float _thumbPosition;
	float _thumbLength;
	Color? _thumbColor;
	Color? _trackColor;
	Rect _laidOutClip;

	bool _pressedThumb;
	bool _dragging;
	float _dragStartOffset;

	internal ScrollBar( bool vertical )
	{
		_vertical = vertical;

		ElementName = "scrollbar";
		AddClass( vertical ? "vertical" : "horizontal" );

		_thumb = new Panel();
		_thumb.AddClass( "thumb" );
		AddChild( _thumb );
	}

	/// <summary>
	/// Is this panel a scrollbar or its thumb?
	/// </summary>
	internal static bool Owns( Panel panel ) => panel is ScrollBar || panel?.Parent is ScrollBar;

	/// <summary>
	/// Bar thickness for a <c>scrollbar-width</c>, in whole screen pixels. Zero means no bar.
	/// </summary>
	internal static float Thickness( Length? width, float scale )
	{
		if ( width is not { } w ) return 0;

		var pixels = w.Unit == LengthUnit.Auto ? AutoThickness * scale : w.GetPixels( 0 );

		return MathF.Max( 0, MathF.Round( pixels ) );
	}

	float ShownThickness => _shown == true ? _thickness : 0;

	float Axis( Vector2 v ) => _vertical ? v.y : v.x;

	public override void Tick()
	{
		base.Tick();

		var owner = Owner;
		if ( owner?.ComputedStyle is null ) return;

		var thickness = Thickness( owner.ComputedStyle.ScrollbarWidth, ScaleToScreen );
		var scrollable = _vertical ? owner.HasScrollY : owner.HasScrollX;
		var shown = scrollable && thickness > 0;

		// Tick runs after this frame's dirty check, so style changes here have to request their own layout

		if ( _shown != shown )
		{
			_shown = shown;
			Style.Display = shown ? DisplayMode.Flex : DisplayMode.None;
			SetNeedsPreLayout();
		}

		if ( !shown ) return;

		if ( _thickness != thickness )
		{
			_thickness = thickness;

			var thumbInset = MathF.Round( thickness * ThumbInset ) / ScaleToScreen;

			if ( _vertical )
			{
				Style.Width = thickness / ScaleToScreen;
				_thumb.Style.Left = thumbInset;
				_thumb.Style.Right = thumbInset;
			}
			else
			{
				Style.Height = thickness / ScaleToScreen;
				_thumb.Style.Top = thumbInset;
				_thumb.Style.Bottom = thumbInset;
			}

			SetNeedsPreLayout();
			_thumb.SetNeedsPreLayout();
		}

		var thumbColor = owner.ComputedStyle.ScrollbarThumbColor;
		if ( _thumbColor != thumbColor )
		{
			_thumbColor = thumbColor;
			_thumb.Style.BackgroundColor = thumbColor;
			_thumb.SetNeedsPreLayout();
		}

		var trackColor = owner.ComputedStyle.ScrollbarTrackColor;
		if ( _trackColor != trackColor )
		{
			_trackColor = trackColor;
			Style.BackgroundColor = trackColor;
			SetNeedsPreLayout();
		}

		// Leave the corner free when the other axis has a bar too
		var other = _vertical ? owner.ScrollbarX : owner.ScrollbarY;
		var inset = other?.ShownThickness ?? 0;

		if ( _cornerInset != inset || _laidOutClip != owner.Box.ClipRect )
		{
			_cornerInset = inset;
			SetNeedsFinalLayout();
		}

		UpdateThumb( owner );

		SetClass( "visible", _dragging || owner.IsDragScrolling || owner.ScrollVelocity != 0 );
		SetClass( "dragging", _dragging );
	}

	void UpdateThumb( Panel owner )
	{
		var track = Axis( Box.Rect.Size );
		var viewport = Axis( owner.Box.Rect.Size );
		var range = Axis( owner.ScrollSize );

		if ( track <= 0 || viewport <= 0 || range <= 0 ) return;

		var length = MathF.Min( track, MathF.Max( MinThumbLength * ScaleToScreen, track * viewport / (viewport + range) ) );
		var position = (track - length) * Progress( owner );

		if ( _thumbLength == length && _thumbPosition == position ) return;

		_thumbLength = length;
		_thumbPosition = position;

		var scale = ScaleToScreen;

		if ( _vertical )
		{
			_thumb.Style.Top = position / scale;
			_thumb.Style.Height = length / scale;
		}
		else
		{
			_thumb.Style.Left = position / scale;
			_thumb.Style.Width = length / scale;
		}

		// The thumb's own dirty check has already run this tick
		_thumb.SetNeedsPreLayout();
	}

	float Progress( Panel owner )
	{
		var range = Axis( owner.ScrollSize );
		var offset = Axis( owner.ScrollOffset );

		if ( owner.IsScrollAxisReversed ) offset += range;

		return Math.Clamp( offset / range, 0, 1 );
	}

	// Placed by hand: absolute insets resolve against the nearest positioned ancestor, and the owner
	// offsets its children by its scroll, which the bar must not follow
	public override void OnLayout( ref Rect layoutRect )
	{
		if ( Owner is not { } owner ) return;

		var clip = owner.Box.ClipRect;
		_laidOutClip = clip;

		layoutRect = _vertical
			? new Rect( clip.Right - _thickness, clip.Top, _thickness, clip.Height - _cornerInset )
			: new Rect( clip.Left, clip.Bottom - _thickness, clip.Width - _cornerInset, _thickness );
	}

	public override bool WantsDrag => true;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		// A drag only starts if the press keeps propagating (PanelInput.MouseButtonState.Update)
		_pressedThumb = e.Target == _thumb;
		if ( _pressedThumb ) return;

		if ( Owner is not { } owner ) return;

		var page = Axis( owner.Box.Rect.Size ) * 0.9f;
		ScrollBy( owner, Axis( MousePosition ) > _thumbPosition ? page : -page );

		e.StopPropagation();
	}

	protected override void OnDragStart( DragEvent e )
	{
		if ( !_pressedThumb ) return;
		if ( Owner is not { } owner ) return;

		_dragging = true;
		_dragStartOffset = Axis( owner.ScrollOffset );

		owner.ScrollTo( owner.ScrollOffset );

		e.StopPropagation();
	}

	protected override void OnDrag( DragEvent e )
	{
		if ( !_dragging ) return;
		if ( Owner is not { } owner ) return;

		var free = Axis( Box.Rect.Size ) - _thumbLength;
		if ( free <= 0 ) return;

		var travel = Axis( MousePosition - e.LocalGrabPosition );
		var target = _dragStartOffset + travel * Axis( owner.ScrollSize ) / free;

		var offset = owner.ScrollOffset;
		if ( _vertical ) offset.y = target;
		else offset.x = target;

		owner.ScrollTo( offset );

		e.StopPropagation();
	}

	protected override void OnDragEnd( DragEvent e )
	{
		_dragging = false;
		_pressedThumb = false;
	}

	void ScrollBy( Panel owner, float delta )
	{
		var offset = owner.ScrollOffset;
		if ( _vertical ) offset.y += delta;
		else offset.x += delta;

		owner.ScrollTo( offset );
	}
}
