/// <summary>
/// Runs <see cref="IPlayerControllable.OnControl"/> on each seated driver's linked contraption.
/// </summary>
public sealed class ControlSystem : GameObjectSystem<ControlSystem>
{
	private readonly List<(Player Player, BaseChair Seat)> _drivers = new();
	private readonly HashSet<GameObject> _driven = new();
	private readonly LinkedGameObjectBuilder _linked = new() { ComponentSnapshot = new() };

	/// <summary>
	/// Runs seat controls before the contraption's fixed update.
	/// </summary>
	public ControlSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 10, OnTick, "ControlSystem" );
	}

	/// <summary>
	/// Keeps occupied seats in entry order. The first driver on a linked contraption gets control.
	/// </summary>
	internal void SetSeat( Player player, BaseChair seat )
	{
		for ( var i = 0; i < _drivers.Count; i++ )
		{
			if ( _drivers[i].Player != player ) continue;
			if ( _drivers[i].Seat == seat ) return;

			// A callback can eject a later driver. Clear their slot without moving anyone else's turn.
			_drivers[i] = default;
			break;
		}

		if ( seat is not null )
			_drivers.Add( (player, seat) );
	}

	private void OnTick()
	{
		_drivers.RemoveAll( static driver => driver.Player is null );
		if ( _drivers.Count == 0 || !Networking.IsHost || Scene != Game.ActiveScene ) return;

		try
		{
			// Drivers who enter during a callback get their first turn next tick.
			var count = _drivers.Count;
			for ( var i = 0; i < count; i++ )
			{
				var (player, seat) = _drivers[i];
				RunControl( player, seat );
			}
		}
		finally
		{
			_driven.Clear();
			_linked.Clear();
		}
	}

	private void RunControl( Player player, BaseChair seat )
	{
		if ( !seat.IsValid() || !seat.Active || seat.GameObject.IsDestroyed ) return;
		if ( !player.IsValid() || player.GameObject.IsDestroyed || player.Controller is not { Active: true } ) return;
		if ( _driven.Contains( seat.GameObject.Root ) ) return;

		var connection = player.Network?.Owner;
		if ( connection is null ) return;

		_linked.Clear();
		_linked.AddConnected( seat.GameObject );

		foreach ( var obj in _linked.Objects )
		{
			if ( _driven.Contains( obj ) ) return;
		}

		foreach ( var obj in _linked.Objects )
			_driven.Add( obj );

		using var scope = ControlContext.Push( player, connection );

		foreach ( var component in _linked.ComponentSnapshot )
		{
			if ( !component.IsValid() || component.GameObject.IsDestroyed ) continue;
			if ( component is not IPlayerControllable controllable ) continue;
			if ( !controllable.CanControl( player ) ) continue;

			controllable.OnControl();
		}
	}
}
