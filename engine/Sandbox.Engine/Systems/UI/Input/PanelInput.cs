using NativeEngine;
using Sandbox.Engine;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

internal class PanelInput
{
	/// <summary>
	/// Panel we're currently hovered over
	/// </summary>
	public Panel Hovered { get; private set; }

	/// <summary>
	/// Panel we're currently pressing down
	/// </summary>
	public Panel Active { get; private set; }

	/// <summary>
	/// During a drag, the panel currently under the cursor (potential drop target)
	/// </summary>
	internal Panel DropTarget { get; private set; }

	//public string LastCursor;

	public Selection Selection = new Selection();

	public PanelInput()
	{
		MouseStates = new MouseButtonState[5];

		for ( int i = 0; i < 5; i++ )
		{
			MouseStates[i] = new MouseButtonState( this, ButtonCode.MouseLeft + i );
		}
	}

	internal virtual void Tick( IEnumerable<RootPanel> panels, bool mouseIsActive )
	{
		bool hoveredAny = false;

		// When we're ticking inputs, let's emulate the mouse if we're using a gamepad
		if ( Input.EnableVirtualCursor && Input.CurrentController is { } controller )
		{
			var moveX = controller.GetAxis( NativeEngine.GameControllerAxis.LeftX );
			var moveY = controller.GetAxis( NativeEngine.GameControllerAxis.LeftY );

			if ( MathF.Abs( moveX ) > 0 || MathF.Abs( moveY ) > 0 )
			{
				var screen = Screen.Size;
				var min = MathF.Min( screen.x, screen.y );
				Mouse.Position += new Vector2( moveX * min, moveY * min ) * Preferences.ControllerAnalogSpeed * RealTime.Delta;
			}
		}

		var inputData = GetInputData();

		if ( mouseIsActive )
		{
			foreach ( var panel in panels )
			{
				if ( UpdateMouse( panel, inputData ) )
				{
					hoveredAny = true;
					break;
				}
			}
		}

		if ( !hoveredAny )
		{
			SetHovered( null );
			ClearDropTarget();
		}
	}

	HashSet<ButtonCode> mousebuttons = new HashSet<ButtonCode>();
	Vector2 mouseWheelValue { get; set; }

	/// <summary>
	/// Called from input when mouse wheel changes
	/// </summary>
	public void AddMouseWheel( Vector2 value, KeyboardModifiers modifiers )
	{
		//
		// Windows apps will typically translate vertical mouse wheel movement into
		// horizontal mouse wheel movement if the shift key is held down during a mouse
		// wheel event
		// This is also inverted, i.e. scrolling down will scroll to the right
		//
		if ( modifiers.Contains( KeyboardModifiers.Shift ) )
			value = value.WithX( -value.y ).WithY( 0 );

		mouseWheelValue -= value;
	}

	/// <summary>
	/// Called from input when mouse wheel changes
	/// </summary>
	internal void AddMouseButton( ButtonCode code, bool down, KeyboardModifiers modifiers )
	{
		if ( down ) mousebuttons.Add( code );
		else mousebuttons.Remove( code );
	}

	internal virtual InputData GetInputData()
	{
		var mouseWheel = mouseWheelValue;
		var leftMouseDown = mousebuttons.Contains( ButtonCode.MouseLeft );

		// When using a controller, simulate left mouse click, and analog scroll wheel
		if ( Input.EnableVirtualCursor && Input.CurrentController is { } controller )
		{
			leftMouseDown |= InputRouter.IsButtonDown( GamepadCode.A );

			const float scrollScale = 0.5f;

			var mouseWheelY = controller.GetAxis( GameControllerAxis.RightY, 0 ) * scrollScale;
			var mouseWheelX = controller.GetAxis( GameControllerAxis.RightX, 0 ) * scrollScale;

			if ( MathF.Abs( mouseWheelX ) > 0f ) mouseWheel.x = mouseWheelX;
			if ( MathF.Abs( mouseWheelY ) > 0f ) mouseWheel.y = mouseWheelY;
		}

		var d = new InputData();
		d.MousePos = Mouse.Position;
		d.Mouse0 = leftMouseDown;
		d.Mouse1 = mousebuttons.Contains( ButtonCode.MouseMiddle );
		d.Mouse2 = mousebuttons.Contains( ButtonCode.MouseRight );
		d.Mouse3 = mousebuttons.Contains( ButtonCode.MouseBack );
		d.Mouse4 = mousebuttons.Contains( ButtonCode.MouseForward );
		d.MouseWheel = mouseWheel;

		mouseWheelValue = 0;

		return d;
	}

	/// <summary>
	/// The cursor should change. Name could be null, meaning default.
	/// </summary>
	public virtual void SetCursor( string name ) => Mouse.CursorType = name;

	internal virtual bool UpdateMouse( RootPanel root, InputData data )
	{
		root.MousePos = data.MousePos;

		if ( !UpdateHovered( root, data.MousePos ) )
			return false;

		var leftMousePressed = !MouseStates[0].Pressed && data.Mouse0;
		var leftMouseReleased = MouseStates[0].Pressed && !data.Mouse0;

		MouseStates[0].Update( data.Mouse0, Hovered );
		MouseStates[1].Update( data.Mouse2, Hovered );
		MouseStates[2].Update( data.Mouse1, Hovered );
		MouseStates[3].Update( data.Mouse3, Hovered );
		MouseStates[4].Update( data.Mouse4, Hovered );

		Active = null;
		if ( MouseStates[2].Active != null ) Active = MouseStates[2].Active;
		if ( MouseStates[1].Active != null ) Active = MouseStates[1].Active;
		if ( MouseStates[0].Active != null ) Active = MouseStates[0].Active;

		if ( Hovered != null )
		{
			if ( data.MouseWheel != Vector2.Zero )
			{
				Hovered.OnMouseWheel( data.MouseWheel );
			}
		}

		Selection.UpdateSelection( root, Hovered, data.Mouse0, leftMousePressed, leftMouseReleased, data.MousePos );

		return true;
	}

	bool UpdateHovered( Panel panel, Vector2 pos )
	{
		Panel current = null;

		if ( !CheckHover( panel, pos, ref current ) )
		{
			return false;
		}

		if ( MouseStates[0].Dragged )
		{
			UpdateDropTarget( current );
			return true;
		}

		SetHovered( current );

		return true;
	}

	internal void SetHovered( Panel current )
	{
		if ( current != Hovered )
		{
			if ( Hovered != null )
			{
				Panel.Switch( PseudoClass.Hover, false, Hovered, current );
				Hovered.CreateEvent( new MousePanelEvent( "onmouseout", Hovered, "none" ) );
			}

			Hovered = current;

			if ( Hovered != null )
			{
				if ( Active == null || Active == Hovered )
					Panel.Switch( PseudoClass.Hover, true, Hovered );

				Hovered.CreateEvent( new MousePanelEvent( "onmouseover", Hovered, "none" ) );
			}
		}

		if ( Hovered != null )
		{
			var cursor = Hovered.ComputedStyle?.Cursor;
			SetCursor( cursor );
		}
	}

	void UpdateDropTarget( Panel current )
	{
		if ( current == DropTarget )
			return;

		var dragSource = MouseStates[0].DragTarget;

		DropTarget?.CreateEvent( new PanelEvent( "ondragleave", dragSource ) );
		DropTarget = current;
		DropTarget?.CreateEvent( new PanelEvent( "ondragenter", dragSource ) );
	}

	void ClearDropTarget()
	{
		if ( DropTarget is null )
			return;

		DropTarget.CreateEvent( new PanelEvent( "ondragleave", MouseStates[0].DragTarget ) );
		DropTarget = null;
	}

	bool CheckHover( Panel panel, Vector2 pos, ref Panel current )
	{
		bool found = false;

		if ( !panel.IsVisible )
			return false;

		if ( panel.ComputedStyle == null )
			return false;

		//
		// Transform using this panel's local matrix
		//
		pos = panel.GetTransformPosition( pos );

		var inside = panel.IsInside( pos );

		if ( inside && panel.ComputedStyle.PointerEvents != PointerEvents.None )
		{
			current = panel;
			found = true;
		}

		//
		// If we're outside and this panel has overflow hidden we can avoid testing against the children
		//
		if ( !inside && (panel.ComputedStyle?.Overflow ?? OverflowMode.Visible) != OverflowMode.Visible )
		{
			return found;
		}

		//
		// No children
		//
		if ( panel._renderChildren is null || panel._renderChildren.Count == 0 )
		{
			return found;
		}

		int topIndex = -10000;

		foreach ( var child in CollectionsMarshal.AsSpan( panel._renderChildren ) )
		{
			var index = child.GetRenderOrderIndex();
			if ( index < topIndex ) continue;

			if ( CheckHover( child, pos, ref current ) )
			{
				topIndex = index;
				found = true;
			}
		}

		return found;
	}

	internal class MouseButtonState
	{
		public PanelInput Input { get; init; }
		public ButtonCode MouseButton { get; init; }

		public bool Pressed;
		public Panel Active;
		public bool Dragged;

		MousePanelEvent MouseDownEvent;

		/// <summary>
		/// Then panel that is potentially being dragged
		/// </summary>
		public Panel DragTarget;

		/// <summary>
		/// The point where we first pressed on the Active element
		/// </summary>
		public Vector2 StartHoldOffsetLocal;
		public Vector2 StartHoldOffsetScreen;

		public MouseButtonState( PanelInput input, ButtonCode i )
		{
			Input = input;
			MouseButton = i;
		}

		public void Update( bool down, Panel hovered )
		{
			var mouseMoved = !Mouse.Delta.IsNearZeroLength;

			//
			// Watch drag - we might have started dragging
			//
			if ( Pressed && down && DragTarget != null && mouseMoved && MouseDownEvent.Propagate )
			{
				var delta = StartHoldOffsetLocal - (DragTarget.MousePosition + DragTarget.ScrollOffset);

				if ( delta.Length > 5.0f && !Dragged )
				{
					Dragged = true;
					DragTarget?.CreateEvent( new DragEvent( "ondragstart", DragTarget, StartHoldOffsetLocal, StartHoldOffsetScreen ) );

					// We started dragging - stop active panel being active, no click events
					{
						Panel.Switch( PseudoClass.Active, false, Active );
						Panel.Switch( PseudoClass.Hover, false, Active );
						Active.CreateEvent( new MousePanelEvent( "onmouseup", Active, GetMouseButtonName( MouseButton ) ) );
						Active.OnButtonEvent( new ButtonEvent( MouseButton, false ) );
						Active = null;
					}
				}

				if ( Dragged )
				{
					DragTarget?.CreateEvent( new DragEvent( "ondrag", DragTarget, StartHoldOffsetLocal, StartHoldOffsetScreen ) { MouseDelta = Mouse.Delta } );
				}
			}

			if ( Pressed == down ) return;
			Pressed = down;

			if ( down ) OnPressed( hovered );
			else OnReleased( hovered );
		}

		string GetMouseButtonName( ButtonCode bc )
		{
			if ( bc == ButtonCode.MouseLeft ) return "mouseleft";
			if ( bc == ButtonCode.MouseRight ) return "mouseright";
			if ( bc == ButtonCode.MouseMiddle ) return "mousemiddle";
			if ( bc == ButtonCode.MouseBack ) return "mouseback";
			if ( bc == ButtonCode.MouseForward ) return "mouseforward";

			return bc.ToString().ToLower();
		}

		void OnPressed( Panel hovered )
		{
			if ( MouseButton == ButtonCode.MouseBack )
			{
				hovered?.CreateEvent( new PanelEvent( "onback", hovered ) );
				hovered?.OnButtonEvent( new ButtonEvent( MouseButton, true ) );
				return;
			}

			if ( MouseButton == ButtonCode.MouseForward )
			{
				hovered?.CreateEvent( new PanelEvent( "onforward", hovered ) );
				hovered?.OnButtonEvent( new ButtonEvent( MouseButton, true ) );
				return;
			}

			Active = hovered;

			IMenuDll.Current?.ClosePopups( hovered );
			IGameInstanceDll.Current?.ClosePopups( hovered );

			if ( Active == null )
				return;

			Panel.Switch( PseudoClass.Active, true, Active );

			if ( MouseButton == ButtonCode.MouseLeft || MouseButton == ButtonCode.MouseRight )
			{
				Dragged = false;
				DragTarget = Active.FindDragTarget();

				if ( DragTarget != null )
				{
					StartHoldOffsetLocal = DragTarget.MousePosition + DragTarget.ScrollOffset;
					StartHoldOffsetScreen = Mouse.Position;
				}
			}

			Active.Focus();

			MouseDownEvent = new MousePanelEvent( "onmousedown", Active, GetMouseButtonName( MouseButton ) );
			Active.CreateEvent( MouseDownEvent );

			Active.OnButtonEvent( new ButtonEvent( MouseButton, true ) );
		}

		void OnReleased( Panel hovered )
		{
			if ( MouseButton == ButtonCode.MouseBack || MouseButton == ButtonCode.MouseForward )
			{
				hovered?.OnButtonEvent( new ButtonEvent( MouseButton, false ) );
				return;
			}

			bool canClick = hovered == Active && !Dragged;

			if ( Dragged && DragTarget != null )
			{
				DragTarget.CreateEvent( new DragEvent( "ondragend", DragTarget, StartHoldOffsetLocal, StartHoldOffsetScreen ) );

				if ( Input.DropTarget != null )
				{
					Input.DropTarget.CreateEvent( new PanelEvent( "ondrop", DragTarget ) );
				}

				Input.ClearDropTarget();

				Dragged = default;
				DragTarget = default;
				StartHoldOffsetLocal = default;
				StartHoldOffsetScreen = default;
			}

			if ( Active == null )
				return;

			if ( canClick )
			{
				Active.CreateEvent( new MousePanelEvent( "onmouseup", Active, GetMouseButtonName( MouseButton ) ) );

				if ( MouseButton == ButtonCode.MouseLeft )
				{
					Active.CreateEvent( new MousePanelEvent( "onclick", Active, GetMouseButtonName( MouseButton ) ) );
				}
				else if ( MouseButton == ButtonCode.MouseMiddle )
				{
					Active.CreateEvent( new MousePanelEvent( "onmiddleclick", Active, GetMouseButtonName( MouseButton ) ) );
				}
				else if ( MouseButton == ButtonCode.MouseRight )
				{
					Active.CreateEvent( new MousePanelEvent( "onrightclick", Active, GetMouseButtonName( MouseButton ) ) );
				}
			}
			else
			{
				Active.CreateEvent( new MousePanelEvent( "onmouseup", Active, GetMouseButtonName( MouseButton ) ) );
				Panel.Switch( PseudoClass.Hover, false, Active, hovered );
			}

			Panel.Switch( PseudoClass.Active, false, Active );

			Active.OnButtonEvent( new ButtonEvent( MouseButton, false ) );
			Active = null;
		}
	}

	internal MouseButtonState[] MouseStates;
}
