/// <summary>
/// Determines whether the button is active only briefly when pressed, or alternates
/// between on and off for each press.
/// </summary>
public enum ButtonMode
{
	[Title( "#tool.option.hold" )]
	Hold,
	[Title( "#tool.option.toggle" )]
	Toggle
}

[Alias( "button" ), Title( "#tool.name.button" )]
public sealed class ButtonEntity : Component, Component.IPressable
{
	[Property, Sync, Hide, SignalOutput( Default = true ), Title( "#tool.signal.pressed" )]
	public SignalOutput Pressed { get; set; } = new();

	[Property, Sync, ClientEditable, Group( "#tool.setting.group.behavior" ), Title( "#tool.setting.mode" ), Order( 0 )]
	public ButtonMode Mode { get; set; } = ButtonMode.Hold;

	[Property, Group( "Visual" )]
	public GameObject Cap { get; set; }

	[Property, Range( 0, 16 ), Group( "Visual" )]
	public float Travel { get; set; } = 2f;

	[Sync]
	public bool IsPressed { get; private set; }

	private Vector3 _capRestPosition;
	private Player _worldController;

	protected override void OnStart()
	{
		if ( Cap.IsValid() )
			_capRestPosition = Cap.LocalPosition;
	}

	protected override void OnUpdate()
	{
		if ( Cap.IsValid() )
			Cap.LocalPosition = _capRestPosition + Vector3.Down * (IsPressed ? Travel : 0f);
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost )
			Pressed.Emit( this, false, _worldController );

		IsPressed = false;
		_worldController = null;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		Pressed.Emit( this, IsPressed, _worldController );
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var title = Game.Language.GetPhrase( Mode == ButtonMode.Toggle ? "tool.button.toggle" : "tool.button.press" );
		return new IPressable.Tooltip( title, "touch_app", null );
	}

	bool IPressable.CanPress( IPressable.Event e ) => Enabled;

	bool IPressable.Press( IPressable.Event e )
	{
		PressFromWorld( e.Source.GameObject );
		return true;
	}

	bool IPressable.Pressing( IPressable.Event e ) => Mode == ButtonMode.Hold;

	void IPressable.Release( IPressable.Event e )
	{
		ReleaseFromWorld( e.Source.GameObject );
	}

	[Rpc.Host]
	void PressFromWorld( GameObject presser )
	{
		if ( !presser.IsValid() ) return;

		_worldController = presser.Root.GetComponent<Player>();
		if ( Mode == ButtonMode.Toggle )
			IsPressed = !IsPressed;
		else
			IsPressed = true;
	}

	[Rpc.Host]
	void ReleaseFromWorld( GameObject presser )
	{
		if ( Mode != ButtonMode.Hold ) return;

		var controller = presser?.Root.GetComponent<Player>();
		if ( _worldController.IsValid() && controller != _worldController ) return;

		IsPressed = false;
	}
}
