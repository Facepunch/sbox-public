using Sandbox.Utility;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Return true if this panel wants to be dragged
	/// </summary>
	public virtual bool WantsDrag => !ScrollSize.IsNearZeroLength && WantsDragScrolling;

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
		if ( ScrollSize.IsNearZeroLength ) return;
		if ( !WantsDragScrolling ) return;

		ScrollVelocity = 0;
		e.StopPropagation();

		IsDragScrolling = true;
	}

	protected virtual void OnDragEnd( DragEvent e )
	{
		IsDragScrolling = false;

		if ( e.Target != this ) return;
		if ( ScrollSize.IsNearZeroLength ) return;
		if ( !WantsDragScrolling ) return;

		var delta = Mouse.Velocity * -6.0f;

		if ( !HasScrollX ) delta.x = 0.0f;
		if ( !HasScrollY ) delta.y = 0.0f;

		ScrollVelocity += delta;
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

		if ( ScrollSize.IsNearZeroLength ) return;
		if ( !WantsDragScrolling ) return;

		e.StopPropagation();

		var delta = e.LocalGrabPosition - e.LocalPosition;

		// don't drag in directions we don't overflow in
		if ( !HasScrollX ) delta.x = 0.0f;
		if ( !HasScrollY ) delta.y = 0.0f;

		ScrollOffset += delta;

		//
		// If we overshot, let us drag out of bounds a little bit, but make it feel
		// tough and resistant to being pulled any more than that.
		//
		{
			Vector2 overShoot = 0;

			if ( ScrollOffset.y < 0 ) overShoot.y = ScrollOffset.y;
			if ( ScrollOffset.x < 0 ) overShoot.x = ScrollOffset.x;
			if ( ScrollOffset.y > ScrollSize.y ) overShoot.y = ScrollOffset.y - ScrollSize.y;
			if ( ScrollOffset.x > ScrollSize.x ) overShoot.x = ScrollOffset.x - ScrollSize.x;

			if ( !overShoot.IsNearZeroLength )
			{
				float overDrag = 16.0f;
				float overSize = overShoot.Length / (overDrag * 12.0f);
				overSize = Easing.EaseOut( overSize.Clamp( 0.0f, 1.0f ) );

				ScrollOffset -= overShoot;
				ScrollOffset += overShoot.Normal * overSize * overDrag;
			}
		}
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
	/// Called when a dragged panel is released over this panel.
	/// </summary>
	protected virtual void OnDrop( PanelEvent e ) { }
}
