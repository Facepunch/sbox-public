using Microsoft.AspNetCore.Components;

namespace Sandbox.UI;

/// <summary>
/// A window within the UI, with a draggable titlebar and optional minimize and close buttons.
/// Place windows under the same parent to share their stacking order.
/// </summary>
[Library( "FloatingWindow" )]
[StyleSheet.Inline( "floatingwindow", Styles )]
public partial class FloatingWindow : Panel
{
	const float MinimizedSize = 64;
	const float MinimizedSpacing = 8;
	const float MinimizedOverhang = 12;
	static readonly List<FloatingWindow> minimizedWindows = new();

	Length? restoreWidth;
	Length? restoreHeight;
	int minimizedIndex;
	int minimizedCount;
	Vector2? position;
	Vector2 dragStart;
	bool wasDragged;
	bool wasActive;
	Rect viewport;
	float layoutScale;
	bool constrained;

	/// <summary>
	/// The text displayed in the default titlebar.
	/// </summary>
	[Parameter] public string Title { get; set; }

	/// <summary>
	/// The emoji displayed in the titlebar and when this window is minimized.
	/// </summary>
	[Parameter] public string Icon { get; set; } = "🪟";

	/// <summary>
	/// Whether the titlebar includes a minimize button.
	/// </summary>
	[Parameter] public bool ShowMinimizeButton { get; set; }

	/// <summary>
	/// Whether this window is showing as an icon at the bottom of the screen.
	/// </summary>
	public bool IsMinimized { get; private set; }

	/// <summary>
	/// Whether the default titlebar includes a close button.
	/// </summary>
	[Parameter] public bool ShowCloseButton { get; set; }

	/// <summary>
	/// Keep the expanded window within its root panel, including after the viewport or contents resize.
	/// </summary>
	[Parameter] public bool KeepOnScreen { get; set; }

	/// <summary>
	/// Handles a close request. When unset, closing deletes the window.
	/// </summary>
	[Parameter] public Action OnCloseRequested { get; set; }

	/// <summary>
	/// Whether the user has moved this window from its initial placement.
	/// </summary>
	public bool HasBeenDragged { get; private set; }

	/// <summary>
	/// Whether a titlebar drag is in progress.
	/// </summary>
	public bool IsDragging { get; private set; }

	/// <summary>
	/// Create a window without taking keyboard focus away from its contents.
	/// </summary>
	public FloatingWindow()
	{
		AddClass( "floating-window" );
		CanDragScroll = false;
		FocusOnClick = false;
	}

	/// <summary>
	/// Move the window to a position in screen pixels, preserving it when its parent changes.
	/// </summary>
	public void MoveTo( Vector2 screenPosition )
	{
		if ( IsMinimized || IsAnimating ) return;
		StopThrow();
		if ( position == screenPosition ) return;
		position = screenPosition;
		SetNeedsFinalLayout();
	}

	/// <summary>
	/// Raise this window among its siblings without changing keyboard focus or its UI layer.
	/// </summary>
	public void BringToFront()
	{
		if ( Parent is not { } parent ) return;
		parent.SetChildIndex( this, parent.ChildrenCount - 1 );
	}

	/// <summary>
	/// Ask the owner to close the window, or delete it when no handler is supplied.
	/// </summary>
	public void RequestClose()
	{
		StopThrow();
		if ( OnCloseRequested is { } close )
		{
			close();
		}
		else
		{
			Delete();
		}
	}

	/// <summary>
	/// Animate the window into a restore button at the screen edge, keeping its contents alive.
	/// </summary>
	public void Minimize()
	{
		if ( IsMinimized || IsAnimating ) return;
		EndMove( false );
		StopThrow();
		position = Box.Rect.Position;
		restoreWidth = Style.Width;
		restoreHeight = Style.Height;
		IsMinimized = true;
		minimizedWindows.Add( this );
		UpdateMinimizedPlacement();
		BeginWindowAnimation( Box.Rect );
	}

	/// <summary>
	/// Animate back to the window's size and position and bring it to the front.
	/// </summary>
	public void Restore()
	{
		if ( !IsMinimized || IsAnimating ) return;
		animationDockRect = Box.Rect;
		IsMinimized = false;
		minimizedWindows.Remove( this );
		Style.Width = restoreWidth;
		Style.Height = restoreHeight;
		SetClass( "minimized", false );
		BeginWindowAnimation( expandedRect );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		StopThrow();
		BringToFront();
	}

	internal void BeginMove( Vector2 delta )
	{
		if ( IsAnimating || IsMinimized ) return;
		BeginDragMotion( delta );
		dragStart = Box.Rect.Position;
		wasDragged = HasBeenDragged;
		HasBeenDragged = true;
		IsDragging = true;
		BringToFront();
		SetClass( "dragging", true );
	}

	internal void Move( Vector2 delta )
	{
		if ( !IsDragging ) return;
		SampleDragMotion( delta );
		MoveTo( dragStart + delta );
	}

	internal void EndMove( bool cancelled )
	{
		if ( !IsDragging ) return;

		if ( cancelled )
		{
			MoveTo( dragStart );
			HasBeenDragged = wasDragged;
		}
		else
		{
			StartThrow();
		}

		IsDragging = false;
		SetClass( "dragging", false );
	}

	/// <summary>
	/// Update viewport limits and raise the window when a child consumes a mouse press.
	/// </summary>
	public override void Tick()
	{
		base.Tick();

		var active = PseudoClass.HasFlag( PseudoClass.Active );
		if ( active && !wasActive )
		{
			StopThrow();
			BringToFront();
		}
		wasActive = active;

		if ( !IsVisible ) EndMove( true );

		if ( IsMinimized ) UpdateMinimizedPlacement();
		TickWindowAnimation();
		TickThrow();

		var bounds = FindRootPanel().Box.Rect;
		if ( viewport == bounds && layoutScale == ScaleFromScreen && constrained == KeepOnScreen ) return;

		viewport = bounds;
		layoutScale = ScaleFromScreen;
		constrained = KeepOnScreen;
		Style.MaxWidth = KeepOnScreen ? bounds.Width * ScaleFromScreen : null;
		Style.MaxHeight = KeepOnScreen ? bounds.Height * ScaleFromScreen : null;
		SetNeedsFinalLayout();
	}

	void UpdateMinimizedPlacement()
	{
		var root = FindRootPanel();
		var count = 0;
		var index = 0;

		// Share the bottom edge in minimize order, even when windows have different parents.
		foreach ( var window in minimizedWindows )
		{
			if ( !window.IsValid() || !window.IsVisible || window.FindRootPanel() != root ) continue;
			if ( window == this ) index = count;
			count++;
		}

		if ( minimizedIndex == index && minimizedCount == count ) return;
		minimizedIndex = index;
		minimizedCount = count;
		SetNeedsFinalLayout();
	}

	/// <summary>
	/// Apply the saved position and keep the window inside the viewport using its measured size.
	/// </summary>
	public override void OnLayout( ref Rect layoutRect )
	{
		base.OnLayout( ref layoutRect );

		if ( IsMinimized && !IsAnimating )
		{
			layoutRect.Position = GetMinimizedRect().Position;
			return;
		}

		if ( position is { } location ) layoutRect.Position = location;

		if ( KeepOnScreen )
		{
			var bounds = FindRootPanel().Box.Rect;
			layoutRect.Position = new Vector2(
				Math.Clamp( layoutRect.Left, bounds.Left, Math.Max( bounds.Left, bounds.Right - layoutRect.Width ) ),
				Math.Clamp( layoutRect.Top, bounds.Top, Math.Max( bounds.Top, bounds.Bottom - layoutRect.Height ) ) );
		}

		if ( position.HasValue ) position = layoutRect.Position;

		if ( IsAnimating )
		{
			expandedRect = layoutRect;
			ApplyWindowAnimation();
		}
	}

	Rect GetMinimizedRect()
	{
		var bounds = FindRootPanel().Box.Rect;
		var size = MinimizedSize * ScaleToScreen;
		var spacing = (MinimizedSize + MinimizedSpacing) * ScaleToScreen;
		var rowWidth = Math.Max( 0, minimizedCount - 1 ) * spacing + size;
		return new Rect(
			bounds.Left + (bounds.Width - rowWidth) * 0.5f + minimizedIndex * spacing,
			bounds.Bottom - size + MinimizedOverhang * ScaleToScreen,
			size, size );
	}

	/// <summary>
	/// Remove this window from the minimized row when it is deleted.
	/// </summary>
	public override void OnDeleted()
	{
		minimizedWindows.Remove( this );
		base.OnDeleted();
	}

	protected override int BuildHash() => HashCode.Combine( Title, Icon, ShowCloseButton, ShowMinimizeButton, IsMinimized );
}
