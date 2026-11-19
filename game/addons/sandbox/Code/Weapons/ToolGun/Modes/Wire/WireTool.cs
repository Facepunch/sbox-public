[Icon( "🔗" )]
[Title( "#tool.name.wire" )]
[ClassName( "wire" )]
[Group( "#tool.group.tools" )]
public sealed class WireTool : BaseConstraintToolMode
{
	private GameObject _hoveredRoot;
	private SignalPortDescription[] _hoveredPorts = [];
	private SignalPortDescription _sourcePort;
	private int _selectedPortIndex;
	private (GameObject Root, int Stage, SignalPortDescription Source) _hoveredPortsKey;
	private RealTimeSince _sinceHoveredPortsRefresh;

	private SignalsOverlay.Entry _hoverEntry;
	private SignalPortDescription[] _hoverEntryPorts;
	private int _hoverEntryIndex = -1;

	private SignalsOverlay _overlay;

	public override string Description => Stage == 1 ? "#tool.hint.wire.stage1" : "#tool.hint.wire.stage0";
	public override string PrimaryAction => Stage == 1 ? "#tool.hint.wire.finish" : "#tool.hint.wire.source";
	public override string ReloadAction => "#tool.hint.wire.remove";
	public override bool UseSnapGrid => false;

	public override void OnControl()
	{
		if ( Input.Pressed( "attack2" ) )
			_sourcePort = null;

		var select = TraceSelect();
		UpdateHoveredSelection( select );
		SelectPortWithMouseWheel();

		var pressedPrimary = Input.Pressed( "attack1" );
		if ( pressedPrimary && SelectedPort is null )
		{
			Input.Clear( "attack1" );
		}
		else if ( pressedPrimary && Stage == 0 )
		{
			_sourcePort = SelectedPort;
		}
		else if ( pressedPrimary && !SignalSystem.AreCompatible( _sourcePort, SelectedPort ) )
		{
			Input.Clear( "attack1" );
		}

		base.OnControl();

		IsValidState = SelectedPort is not null && (Stage == 0 || _sourcePort is not null);

		UpdateOverlay();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		// The overlay hides itself once we stop calling Show.
		_sourcePort = null;
		_selectedPortIndex = 0;
		_hoverEntry = null;
		_hoverEntryPorts = null;
		_overlay = null;
	}

	private SignalPortDescription SelectedPort
	{
		get
		{
			if ( _hoveredPorts.Length == 0 ) return null;
			var port = _hoveredPorts[_selectedPortIndex.Clamp( 0, _hoveredPorts.Length - 1 )];
			return IsPortValid( port ) ? port : null;
		}
	}

	private static bool IsPortValid( SignalPortDescription port ) =>
		port?.Component.IsValid() == true && port.Component.GameObject.IsValid();

	private void UpdateHoveredSelection( SelectionPoint select )
	{
		// Port descriptions can outlive their component. Cancel the pending wire
		// before accessing the source or filtering compatible targets.
		if ( Stage != 0 && (!IsPortValid( _sourcePort ) || !Point1.IsValid()) )
		{
			Stage = 0;
			Point1 = default;
			Point2 = default;
			_sourcePort = null;
			_hoveredPortsKey = default;
			_hoveredPorts = [];
			_selectedPortIndex = 0;
		}

		var root = select.IsValid() ? select.GameObject.Root : null;
		var targetChanged = root != _hoveredRoot;
		_hoveredRoot = root;

		if ( Stage == 0 )
			_sourcePort = null;

		// Enumerating ports reflects over the whole contraption — only redo it when the
		// selection changed, plus a slow tick to catch components toggling on or off.
		var key = (root, Stage, _sourcePort);
		if ( key != _hoveredPortsKey || _sinceHoveredPortsRefresh > 0.2f )
		{
			_hoveredPortsKey = key;
			_sinceHoveredPortsRefresh = 0;

			var canTarget = root.IsValid() && (Stage == 0 || (_sourcePort is not null && root != _sourcePort.Component.GameObject.Root));
			_hoveredPorts = !canTarget ? [] : Stage == 0
				? SignalSystem.GetPorts( root ).ToArray()
				: SignalSystem.GetCompatiblePorts( root, _sourcePort ).ToArray();
		}

		if ( targetChanged || _selectedPortIndex >= _hoveredPorts.Length )
			_selectedPortIndex = 0;
	}

	private void SelectPortWithMouseWheel()
	{
		var wheel = Input.MouseWheel.y;
		if ( wheel.AlmostEqual( 0f ) ) return;

		if ( _hoveredPorts.Length <= 1 ) return;

		var direction = wheel > 0f ? -1 : 1;
		_selectedPortIndex = (_selectedPortIndex + direction + _hoveredPorts.Length) % _hoveredPorts.Length;

		// Inventory reads this later in the frame. Wire tool owns the wheel while choosing a port.
		Sandbox.Inventory.ConsumeMouseWheel();
	}

	private void UpdateOverlay()
	{
		if ( !_overlay.IsValid() )
			_overlay = Scene.Get<SignalsOverlay>();
		if ( !_overlay.IsValid() ) return;
		if ( Player?.WantsHideHud == true ) return;

		// The overlay collects and draws the wires itself; we just keep it up and
		// hand it the hover card for the port being picked.
		if ( !_hoveredRoot.IsValid() || SelectedPort is null )
		{
			_hoverEntry = null;
			_hoverEntryPorts = null;
		}
		else if ( _hoveredPorts != _hoverEntryPorts || _selectedPortIndex != _hoverEntryIndex )
		{
			// This runs every frame — only rebuild the card when the selection changed.
			_hoverEntryPorts = _hoveredPorts;
			_hoverEntryIndex = _selectedPortIndex;

			var options = _hoveredPorts.Select( port => new SignalsOverlay.Option( port.Title )
			{
				Icon = port.Icon,
				// which object it's on - a contraption can have several of the same thing, all with the same port names
				Subtitle = port.ComponentTitle,
				Description = string.IsNullOrWhiteSpace( port.Description )
					? Game.Language.GetPhrase( port is SignalOutputDescription ? "tool.wire.signal_output" : "tool.wire.signal_input", new() { { "component", port.ComponentTitle } } )
					: port.Description
			} ).ToArray();

			_hoverEntry = new SignalsOverlay.Entry( _hoveredRoot, SelectedPort.Icon )
			{
				Card = new SignalsOverlay.Card( SelectedPort.ComponentTitle )
				{
					Options = options,
					SelectedOption = _selectedPortIndex
				}
			};
		}

		_overlay.Show( _hoverEntry );
	}

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var link in linked.GetComponentsInChildren<ManualLink>( true ) )
			if ( linked == target || link.Body?.Root == target )
				yield return link.GameObject;
	}

	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		var links = ManualLink.CreatePair( point1.GameObject, point2.GameObject );
		Track( links );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.undo.link";
		undo.Add( links[0] );
	}

	protected override void OnConstraintSubmitted( SelectionPoint point1, SelectionPoint point2 )
	{
		SignalSystem.SetConnected( _sourcePort, SelectedPort, true );

		_sourcePort = null;
	}
}
