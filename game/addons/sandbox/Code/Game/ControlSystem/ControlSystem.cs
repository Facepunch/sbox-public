/// <summary>
/// Lets players drive contraptions from a seat. Every tick, this finds who's sitting in each
/// chair and calls <see cref="IPlayerControllable.OnControl"/> on the attached contraption.
/// </summary>
public sealed partial class ControlSystem : GameObjectSystem<ControlSystem>
{
	private readonly Dictionary<BaseChair, RealTimeSince> _occupiedSince = new();
	private readonly List<BaseChair> _chairs = new();
	private readonly List<BaseChair> _seats = new();
	private readonly HashSet<GameObject> _driven = new();
	private readonly LinkedGameObjectBuilder _linked = new();
	private readonly List<Component> _components = new();

	public ControlSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 10, OnTick, "ControlSystem" );
		Listen( Stage.StartUpdate, 10, ReportWatchedKeys, "ControlSystem.Keys" );
	}

	private void OnTick()
	{
		if ( Scene != Game.ActiveScene ) return;

		if ( !Networking.IsHost ) return;

		// Whoever sat down first claims the contraption — a second seat on the same vehicle does nothing.
		var driven = _driven;
		driven.Clear();

		foreach ( var chair in GetSortedSeats() )
		{
			if ( driven.Contains( chair.GameObject.Root ) ) continue;

			_linked.Clear();
			_linked.AddConnected( chair.GameObject );

			if ( AnyDriven( _linked.Objects ) ) continue;

			foreach ( var gameObject in _linked.Objects )
				driven.Add( gameObject );

			RunControl( chair, _linked );
		}

		_linked.Clear();

		StopUnwatchedKeys();
	}

	private bool AnyDriven( List<GameObject> objects )
	{
		foreach ( var gameObject in objects )
		{
			if ( _driven.Contains( gameObject ) ) return true;
		}

		return false;
	}

	private List<BaseChair> GetSortedSeats()
	{
		_chairs.Clear();
		_seats.Clear();

		Scene.GetAll( _chairs );

		foreach ( var chair in _chairs )
		{
			if ( !chair.IsValid() || !chair.IsOccupied )
			{
				_occupiedSince.Remove( chair );
				continue;
			}

			_occupiedSince.TryAdd( chair, 0 );

			// Insertion keeps it stable, like the OrderBy it replaced
			var index = _seats.Count;
			while ( index > 0 && _occupiedSince[_seats[index - 1]] > _occupiedSince[chair] ) index--;
			_seats.Insert( index, chair );
		}

		return _seats;
	}

	private void RunControl( BaseChair chair, LinkedGameObjectBuilder linked )
	{
		var player = chair.GetOccupant()?.GetComponent<Player>();
		if ( !player.IsValid() ) return;

		var connection = player.Network?.Owner;
		if ( connection is null ) return;

		_controllables.Clear();
		_components.Clear();

		foreach ( var gameObject in linked.Objects )
			LinkedGameObjectBuilder.CollectComponents( gameObject, _components );

		foreach ( var component in _components )
		{
			if ( !component.IsValid() ) continue;
			if ( component is not IPlayerControllable controllable ) continue;
			if ( !controllable.CanControl( player ) ) continue;

			_controllables.Add( controllable );
		}

		_components.Clear();

		WatchKeys( connection, _controllables );

		using var scope = ControlContext.Push( player, connection );

		foreach ( var controllable in _controllables )
		{
			// An earlier one might have destroyed it - dynamite going off
			if ( controllable is Component component && !component.IsValid() ) continue;
			controllable.OnControl();
		}
	}
}
