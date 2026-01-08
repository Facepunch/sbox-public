namespace Sandbox.UI;

/// <summary>
/// Simulates PanelInput for world space panels using a ray and inputs.
/// </summary>
internal class WorldPanelInput : PanelInput
{
	internal Ray Ray { get; set; }
	internal bool MouseLeftPressed;
	internal bool MouseRightPressed;
	internal Vector2 MouseWheel;
	internal bool UseMouseInput;

	internal override void Tick( IEnumerable<RootPanel> panels, bool mouseIsActive )
	{
		bool hoveredAny = false;
		var inputData = GetInputData();

		List<RootPanel> worldPanels = new();
		foreach ( var panel in panels.Where( p => p.ChildrenWantMouseInput ) )
		{
			if ( panel.RayToLocalPosition( Ray, out panel.WorldCursor, out panel.WorldDistance ) )
				worldPanels.Add( panel );
		}

		// In order of distance, update our mouse on them
		foreach ( var panel in worldPanels.OrderBy( x => x.WorldDistance ) )
		{
			inputData.MousePos = panel.WorldCursor;
			if ( UpdateMouse( panel, inputData ) )
			{
				hoveredAny = true;
				break;
			}
		}

		if ( !hoveredAny )
		{
			SetHovered( null );
		}

		SimulateEvents();
	}

	internal override InputData GetInputData()
	{
		if ( UseMouseInput )
		{
			return base.GetInputData();
		}

		return new InputData
		{
			Mouse0 = MouseLeftPressed,
			Mouse2 = MouseRightPressed,
			MouseWheel = MouseWheel,
		};
	}

	//
	// Simulate some events, these are a bit different from how InputRouter
	// handles it, so it's a bit shit that this is repeated but required for now.
	//

	//
	// We only want to count as a double click if it was clicked within
	// a small amount of pixels & on the same root panel.
	// It's not a double click if you click, move the mouse, and then click again.
	//
	internal Panel LastClickRoot;
	internal Vector2 LastClickPos;
	internal RealTimeSince LastClickTimeSince;

	// Bit more forgiving then normal panels ( shaky VR hands )
	internal const float MaxAltClickDelta = 50.0f;

	internal Queue<string> DoubleClicks = new();
	internal Vector2 MouseMovement;
	internal Vector2 LastMousePosition;

	internal override bool UpdateMouse( RootPanel root, InputData data )
	{
		MouseMovement += LastMousePosition - data.MousePos;
		LastMousePosition = data.MousePos;

		var leftMousePressed = !MouseStates[0].Pressed && data.Mouse0;
		if ( leftMousePressed )
		{
			// Are we a double clicker ( 250ms matches engine )
			if ( LastClickTimeSince < 0.25f && LastClickRoot == root )
			{
				// let's be a lot more forgiving with the delta then on normal panels
				// people can have shaky vr hands
				var AltClickDelta = LastClickPos - data.MousePos;
				if ( AltClickDelta.Length < MaxAltClickDelta / root.Scale )
				{
					DoubleClicks.Enqueue( "mouseleft" );
				}
			}

			LastClickRoot = root;
			LastClickPos = data.MousePos;
			LastClickTimeSince = 0;
		}

		return base.UpdateMouse( root, data );
	}

	internal void SimulateEvents()
	{
		var listSize = DoubleClicks.Count;
		for ( int i = 0; i < listSize; i++ )
			if ( DoubleClicks.TryDequeue( out var e ) )
			{
				Hovered?.CreateEvent( new MousePanelEvent( "ondoubleclick", Hovered, e ) );
			}

		if ( MouseMovement != 0 )
		{
			// If we're pressing down on a panel we send all the mouse move events to that 
			var moveRecv = Hovered;
			if ( Active != null ) moveRecv = Active;

			moveRecv?.CreateEvent( new MousePanelEvent( "onmousemove", moveRecv, "none" ) );
			MouseMovement = 0;
		}
	}
}
