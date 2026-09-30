using Sandbox.Utility;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Return true if this panel wants to be dragged
	/// </summary>
	public virtual bool WantsDrag => (!ScrollSize.IsNearZeroLength || BlocksScrollChaining) && WantsDragScrolling;

	bool BlocksScrollChaining =>
		(IsScrollContainer( true ) && GetOverscrollBehavior( true ) != OverscrollBehavior.Auto) ||
		(IsScrollContainer( false ) && GetOverscrollBehavior( false ) != OverscrollBehavior.Auto);

	/// <summary>
	/// Set this to false if you want to opt out of drag scrolling
	/// </summary>
	public bool CanDragScroll { get; set; } = true;

	protected virtual bool WantsDragScrolling
	{
		get
		{
			if ( !CanDragScroll )
				return false;

			if ( ComputedStyle.OverflowX == OverflowMode.Scroll )
				return true;

			if ( ComputedStyle.OverflowY == OverflowMode.Scroll )
				return true;

			return false;
		}
	}

	/// <summary>
	/// Find a panel in our heirachy that wants to be dragged
	/// </summary>
	internal Panel FindDragTarget()
	{
		if ( WantsDrag ) return this;
		return Parent?.FindDragTarget();
	}

	/// <summary>
	/// Distribute the drag events to specific virtual functions
	/// </summary>
	void InternalDragEvent( DragEvent e )
	{
		if ( e.Is( "ondragstart" ) ) OnDragStart( e );
		if ( e.Is( "ondragend" ) ) OnDragEnd( e );
		if ( e.Is( "ondrag" ) ) OnDrag( e );
	}

	protected virtual void OnDragStart( DragEvent e )
	{
		if ( e.Target != this ) return;
		if ( ScrollSize.IsNearZeroLength && !BlocksScrollChaining ) return;
		if ( !WantsDragScrolling ) return;

		StopScrollVelocity();
		e.StopPropagation();

		IsDragScrolling = true;
	}

	protected virtual void OnDragEnd( DragEvent e )
	{
		IsDragScrolling = false;

		if ( e.Target != this ) return;
		if ( ScrollSize.IsNearZeroLength ) return;
		if ( !WantsDragScrolling ) return;

		var delta = (UISystem?.Input.CursorVelocity ?? default) * -6.0f;

		if ( !HasScrollX ) delta.x = 0.0f;
		if ( !HasScrollY ) delta.y = 0.0f;

		ScrollVelocity += delta;
		scrollVelocityVelocity = 0;
		e.StopPropagation();
	}

	/// <summary>
	/// Return true if this panel is scrollable on the X axis
	/// </summary>
	public bool HasScrollX => ScrollSize.x > 0 && ComputedStyle.OverflowX == OverflowMode.Scroll;

	/// <summary>
	/// Return true if this panel is scrollable on the Y axis
	/// </summary>
	public bool HasScrollY => ScrollSize.y > 0 && ComputedStyle.OverflowY == OverflowMode.Scroll;


	protected virtual void OnDrag( DragEvent e )
	{
		if ( e.Target != this ) return;

		if ( ScrollSize.IsNearZeroLength && !BlocksScrollChaining ) return;
		if ( !WantsDragScrolling ) return;

		e.StopPropagation();

		var delta = e.LocalGrabPosition - e.LocalPosition;

		// Only scroll-container axes participate in dragging or chaining.
		if ( !IsScrollContainer( true ) ) delta.x = 0.0f;
		if ( !IsScrollContainer( false ) ) delta.y = 0.0f;

		ApplyDragScroll( delta );
	}

	void ApplyDragScroll( Vector2 delta )
	{
		var min = IsScrollAxisReversed ? -ScrollSize : Vector2.Zero;
		var max = IsScrollAxisReversed ? Vector2.Zero : ScrollSize;
		var target = ScrollOffset + delta;
		var overshoot = target - target.Clamp( min, max );

		// Pass only the part of the drag beyond the boundary to the next scroll container.
		if ( overshoot.x != 0 && GetOverscrollBehavior( true ) == OverscrollBehavior.Auto && FindScrollChainTarget( overshoot.x, true ) is { } parentX )
		{
			parentX.StopScrollVelocity();
			parentX.ApplyDragScroll( new Vector2( overshoot.x, 0 ) );
			target.x -= overshoot.x;
		}
		if ( overshoot.y != 0 && GetOverscrollBehavior( false ) == OverscrollBehavior.Auto && FindScrollChainTarget( overshoot.y, false ) is { } parentY )
		{
			parentY.StopScrollVelocity();
			parentY.ApplyDragScroll( new Vector2( 0, overshoot.y ) );
			target.y -= overshoot.y;
		}

		if ( !HasScrollX || GetOverscrollBehavior( true ) == OverscrollBehavior.None ) target.x = target.x.Clamp( min.x, max.x );
		if ( !HasScrollY || GetOverscrollBehavior( false ) == OverscrollBehavior.None ) target.y = target.y.Clamp( min.y, max.y );
		// Original drag resistance, followed by the shared hard outer limit.
		var overShoot = target - target.Clamp( min, max );
		if ( !overShoot.IsNearZeroLength )
		{
			float overDrag = 16.0f;
			float overSize = overShoot.Length / (overDrag * 12.0f);
			overSize = Easing.EaseOut( overSize.Clamp( 0.0f, 1.0f ) );
			target -= overShoot;
			target += overShoot.Normal * overSize * overDrag;
		}
		ScrollOffset = target.Clamp( min - ScrollBounceLimit, max + ScrollBounceLimit );
		SetNeedsFinalLayout();
	}

	/// <summary>
	/// Called when a panel is being dragged over this panel. Fires continuously as the cursor moves.
	/// </summary>
	protected virtual void OnDragEnter( PanelEvent e ) { }

	/// <summary>
	/// Called when a panel being dragged leaves this panel's bounds.
	/// </summary>
	protected virtual void OnDragLeave( PanelEvent e ) { }

	/// <summary>
	/// Called when a dragged panel is released over this panel. Drags from outside the app
	/// come through here too, as a <see cref="DropEvent"/> - repeatedly while one hovers, so
	/// its Action can answer whether it'd be taken, then once more when it lands.
	/// </summary>
	protected virtual void OnDrop( PanelEvent e ) { }
}
