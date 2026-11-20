public abstract class BaseToggle : Component
{
	public delegate Task StateChangedDelegate( bool state );

	/// <summary>
	/// The toggle state has changed
	/// </summary>
	[Property] public StateChangedDelegate OnStateChanged { get; set; }

	bool _state;

	[Property, Sync]
	public bool State
	{
		get => _state;
		set
		{
			if ( _state == value ) return;

			_state = value;
			StateHasChanged( _state );

		}
	}

	/// <summary>
	/// The toggle state has changed
	/// </summary>
	protected virtual void StateHasChanged( bool newState )
	{
		OnStateChanged?.Invoke( _state );
	}
}
