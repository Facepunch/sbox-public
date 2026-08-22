namespace Sandbox.UI;


public partial class Panel
{
	/// <summary>
	/// Current mouse position local to this panels top left corner.
	/// </summary>
	[Hide]
	public Vector2 MousePosition
	{
		get
		{
			if ( FindRootPanel() is not RootPanel root )
				return default;

			var mp = root.MousePos;

			if ( GlobalMatrix.HasValue )
			{
				mp = GlobalMatrix.Value.Transform( mp );
			}

			return mp - Box.Rect.Position;
		}

	}

	/// <summary>
	/// Called by <see cref="PanelInput.CheckHover(Panel, Vector2, ref Panel)" /> to transform
	/// the current mouse position using the panel's LocalMatrix (by default). This can be overriden for special cases.
	/// </summary>
	/// <param name="pos"></param>
	/// <returns></returns>
	public virtual Vector2 GetTransformPosition( Vector2 pos )
	{
		return LocalMatrix?.Transform( pos ) ?? pos;
	}

	/// <summary>
	/// Whether given screen position is within this panel. This will accurately handle border radius as well.
	/// </summary>
	/// <param name="pos">The position to test, in screen coordinates.</param>
	public bool IsInside( Vector2 pos )
	{
		var rect = Box.Rect;

		if ( pos.x < rect.Left || pos.x > rect.Right ) return false;
		if ( pos.y < rect.Top || pos.y > rect.Bottom ) return false;

		var s = ComputedStyle;
		if ( s == null ) return false;

		if ( !s.HasBorderRadius ) return true;

		var radii = BorderRadii.FromStyle( s, rect );

		pos.x -= rect.Left;
		pos.y -= rect.Top;

		var right = rect.Width - pos.x;
		var bottom = rect.Height - pos.y;

		if ( OutsideCorner( radii.TopLeft, pos.x, pos.y ) ) return false;
		if ( OutsideCorner( radii.TopRight, right, pos.y ) ) return false;
		if ( OutsideCorner( radii.BottomRight, right, bottom ) ) return false;
		if ( OutsideCorner( radii.BottomLeft, pos.x, bottom ) ) return false;

		return true;
	}

	/// <summary>
	/// Whether a point falls outside a corner's quarter ellipse. Distances are from the panel's
	/// two edges at that corner, so both are below the radius only within the corner itself.
	/// </summary>
	static bool OutsideCorner( Vector2 radius, float fromSide, float fromTopOrBottom )
	{
		if ( fromSide >= radius.x || fromTopOrBottom >= radius.y ) return false;

		var x = (radius.x - fromSide) / radius.x;
		var y = (radius.y - fromTopOrBottom) / radius.y;

		return x * x + y * y > 1.0f;
	}

	/// <summary>
	/// Whether the given rect is inside this panels bounds. (<see cref="Box.Rect"/>)
	/// </summary>
	/// <param name="rect">The rect to test, which should have screen-space coordinates.</param>
	/// <param name="fullyInside"><see langword="true"/> to test if the given rect is completely inside the panel. <see langword="false"/> to test for an intersection.</param>
	public bool IsInside( Rect rect, bool fullyInside )
	{
		return rect.IsInside( Box.Rect, fullyInside );
	}

	/// <summary>
	/// False by default, can this element accept keyboard focus. If an element accepts
	/// focus it'll be able to receive keyboard input.
	/// </summary>
	[Property]
	public bool AcceptsFocus { get; set; }

	/// <summary>
	/// Describe what to do with keyboard input. The default is InputMode.UI which means that when
	/// focused, this panel will receive Keys Typed and Button Events.
	/// If you set this to InputMode.Game, this panel will redirect its inputs to the game, which means
	/// for example that if you're focused on this panel and press space, it'll send the jump button to the game.
	/// </summary>
	[Property]
	public PanelInputType ButtonInput { get; set; }

	/// <summary>
	/// False by default. Anything that is capable of accepting IME input should return true. Which is probably just a TextEntry.
	/// </summary>
	[Hide]
	public virtual bool AcceptsImeInput => false;

	/// <summary>
	/// Give input focus to this panel.
	/// </summary>
	public bool Focus()
	{
		return InputFocus.Set( this );
	}

	/// <summary>
	/// Remove input focus from this panel.
	/// </summary>
	public bool Blur()
	{
		return InputFocus.Clear( this );
	}

	/// <summary>
	/// Called when any button, mouse (except for mouse4/5) and keyboard, are pressed or depressed while hovering this panel.
	/// </summary>
	public virtual void OnButtonEvent( ButtonEvent e )
	{
		Parent?.OnButtonEvent( e );
	}

	/// <summary>
	/// Called when a printable character has been typed (pressed) while this panel has input focus. (<see cref="Focus"/>)
	/// </summary>
	public virtual void OnKeyTyped( char k )
	{
		Parent?.OnKeyTyped( k );
	}

	/// <summary>
	/// Called when any keyboard button has been typed (pressed) while this panel has input focus. (<see cref="Focus"/>)
	/// </summary>
	public virtual void OnButtonTyped( ButtonEvent e )
	{
		Parent?.OnButtonTyped( e );
	}

	/// <summary>
	/// Called when the user presses CTRL+V while this panel has input focus.
	/// </summary>
	/// <param name="text"></param>
	public virtual void OnPaste( string text )
	{
		Parent?.OnPaste( text );
	}

	/// <summary>
	/// If we have a value that can be copied to the clipboard, return it here.
	/// </summary>
	public virtual string GetClipboardValue( bool cut )
	{
		if ( AllowChildSelection )
			return CollectSelectedChildrenText( this );

		if ( Parent != null )
			return Parent.GetClipboardValue( cut );

		return null;
	}

	/// <summary>
	/// Called when the player scrolls their mouse wheel while hovering this panel.
	/// </summary>
	/// <param name="value">The scroll wheel delta. Positive values are scrolling down, negative - up.</param>
	public virtual void OnMouseWheel( Vector2 value )
	{
		if ( TryScroll( value ) )
			return;

		Parent?.OnMouseWheel( value );
	}

	/// <summary>
	/// Called from <see cref="OnMouseWheel"/> to try to scroll.
	/// </summary>
	/// <param name="value">The scroll wheel delta. Positive values are scrolling down, negative - up.</param>
	/// <returns>Return true to NOT propagate the event to the <see cref="Parent"/>.</returns>
	public bool TryScroll( Vector2 value )
	{
		if ( ComputedStyle == null ) return false;
		if ( !HasScrollY && !HasScrollX ) return false;

		// If we're not scrolling in the same direction that this panel overflows in, ignore
		if ( ComputedStyle.OverflowX != OverflowMode.Scroll && value.x != 0 ) return false;
		if ( ComputedStyle.OverflowY != OverflowMode.Scroll && value.y != 0 ) return false;

		var velocityAdd = Vector2.Zero;

		if ( ComputedStyle.OverflowX == OverflowMode.Scroll && HasScrollX ) velocityAdd += new Vector2( value.x * -20, 0 );
		if ( ComputedStyle.OverflowY == OverflowMode.Scroll && HasScrollY ) velocityAdd += new Vector2( 0, value.y * 20 );

		velocityAdd *= (1 + ScrollVelocity.Length / 100.0f);
		ScrollVelocity += velocityAdd;

		if ( velocityAdd.Length.AlmostEqual( 0 ) )
			return false;

		return true;
	}

	/// <summary>
	/// Scroll to the bottom, if the panel has scrolling enabled.
	/// </summary>
	/// <returns>Whether we scrolled to the bottom or not.</returns>
	public bool TryScrollToBottom()
	{
		if ( ComputedStyle == null ) return false;
		if ( !HasScrollY ) return false;

		ScrollOffset = new Vector2( ScrollOffset.x, ScrollSize.y );
		IsScrollAtBottom = true;
		ScrollVelocity = new Vector2( 0, 0 );
		return true;
	}

	internal static Panel MouseCapture { get; private set; }

	/// <summary>
	/// Captures the mouse cursor while active. The cursor will be hidden and will be stuck in place.
	/// <para>You will want to use <see cref="Mouse.Delta"/> in
	/// <see cref="Panel.Tick"/> while <see cref="HasMouseCapture"/> to read mouse movements.</para>
	/// <para>You can call this from <see cref="OnButtonEvent"/> for mouse clicks.</para>
	/// </summary>
	/// <param name="b">Whether to enable or disable the capture.</param>
	public void SetMouseCapture( bool b )
	{
		if ( b )
		{
			MouseCapture = this;
			return;
		}

		if ( MouseCapture == this )
		{
			MouseCapture = null;
			return;
		}
	}

	/// <summary>
	/// Whether this panel is capturing the mouse cursor. See <see cref="SetMouseCapture"/>.
	/// </summary>
	[Hide]
	public bool HasMouseCapture => MouseCapture == this;

	//
	// These are used by the input system as an optimization
	//
	internal Vector2 WorldCursor;
	internal float WorldDistance = float.MaxValue;

	/// <summary>
	/// Transform a ray in 3D space to a position on the panel. This is used for world panel input.
	/// </summary>
	/// <param name="ray">The ray in 3D world space to test against this panel.</param>
	/// <param name="position">Position on the panel where the intersection happened, local to the panel's top left corner.</param>
	/// <param name="distance">Distance from the ray's origin to the intersection in 3D space.</param>
	/// <returns>Return true if a hit/intersection was detected.</returns>
	public virtual bool RayToLocalPosition( Ray ray, out Vector2 position, out float distance )
	{
		position = default;
		distance = default;

		return false;
	}
}
