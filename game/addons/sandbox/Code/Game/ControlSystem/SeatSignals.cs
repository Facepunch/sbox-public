/// <summary>
/// Wire outputs for a seat: a pulse when someone sits down, a pulse when they get up, and an on/off "occupied" state.
/// Wire them to anything - a speaker, a door, a light. Added to every <see cref="BaseChair"/> automatically by
/// <see cref="SeatSignalsSystem"/>, so seats from the spawn menu, the workshop, dupes and maps all have them.
/// </summary>
[Title( "#entity.name.seat" ), Icon( "event_seat" )]
public sealed class SeatSignals : Component, Global.IControlEvents
{
	/// <summary>
	/// Pulses once when someone sits in this seat.
	/// </summary>
	[Property, Sync, Hide, SignalOutput( Default = true ), Title( "#signal.seat.entered" )]
	public SignalOutput Entered { get; set; } = new();

	/// <summary>
	/// Pulses once when they get out.
	/// </summary>
	[Property, Sync, Hide, SignalOutput, Title( "#signal.seat.exited" )]
	public SignalOutput Exited { get; set; } = new();

	/// <summary>
	/// On for as long as someone is sitting here.
	/// </summary>
	[Property, Sync, Hide, SignalOutput, Title( "#signal.seat.occupied" )]
	public SignalOutput Occupied { get; set; } = new();

	private BaseChair _chair;
	private BaseChair Chair => _chair.IsValid() ? _chair : _chair = GetComponent<BaseChair>();

	void Global.IControlEvents.OnControlStarted( ControlEvent e )
	{
		if ( !Networking.IsHost || e.Target != GameObject ) return;

		Entered.Emit( this, e.Player );
	}

	void Global.IControlEvents.OnControlStopped( ControlEvent e )
	{
		if ( !Networking.IsHost || e.Target != GameObject ) return;

		Exited.Emit( this, e.Player );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		var chair = Chair;
		var occupied = chair.IsValid() && chair.IsOccupied;
		var player = occupied ? chair.GetOccupant()?.GetComponent<Player>() : null;

		// state outputs emit every tick while on, and once when they go off
		Occupied.Emit( this, occupied, player );
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost )
			Occupied.Emit( this, false );
	}
}

/// <summary>
/// Gives every seat in the scene a <see cref="SeatSignals"/>, so their enter/exit can be wired up.
/// Seats are an engine component spawned from all sorts of prefabs, so it's simpler to add it here than to every one.
/// </summary>
internal sealed class SeatSignalsSystem : GameObjectSystem<SeatSignalsSystem>
{
	private RealTimeSince _sinceScan;

	public SeatSignalsSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 20, Scan, "SeatSignals" );
	}

	private void Scan()
	{
		if ( Scene != Game.ActiveScene ) return;
		if ( _sinceScan < 0.25f ) return;
		_sinceScan = 0;

		// copied, since adding components while walking the scene's component list isn't safe
		foreach ( var chair in Scene.GetAll<BaseChair>().ToArray() )
		{
			if ( !chair.IsValid() ) continue;
			if ( chair.GetComponent<SeatSignals>( true ).IsValid() ) continue;

			var networked = chair.GameObject.Network?.Active == true;

			// networked seats get it from the host, which sends it on to everyone;
			// local-only ones (some map seats) are given it by every machine itself
			if ( networked && (!Networking.IsHost || chair.IsProxy) ) continue;

			chair.AddComponent<SeatSignals>();

			if ( networked )
				chair.GameObject.Network.Refresh();
		}
	}
}
