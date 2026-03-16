namespace Sandbox.Mapping;

[EditorHandle( Icon = "touch_app" )]
[Category( "Gameplay" ), Alias( "Button" ), Icon( "touch_app" )]
public sealed class Button : Component, Component.IPressable
{
	/// <summary>
	/// Sound to play when the button is pressed.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent OnSound { get; set; }

	/// <summary>
	/// Sound to play when the button is released.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent OffSound { get; set; }

	/// <summary>
	/// Called when the button's state changes.
	/// </summary>
	[Property, Group( "Events" )]
	public Action<bool> OnStateChanged { get; set; }

	/// <summary>
	/// Called when the button is pressed. Receives the GameObject that pressed it.
	/// </summary>
	[Property, Group( "Events" )]
	public Action<GameObject> OnPressed { get; set; }

	/// <summary>
	/// Called when the button is released. Receives the GameObject that released it.
	/// </summary>
	[Property, Group( "Events" )]
	public Action<GameObject> OnReleased { get; set; }

	/// <summary>
	/// Called when the button turns on. Receives the GameObject that activated it.
	/// </summary>
	[Property, Group( "Events" )]
	public Action<GameObject> OnTurnedOn { get; set; }

	/// <summary>
	/// Called when the button turns off.
	/// </summary>
	[Property, Group( "Events" )]
	public Action OnTurnedOff { get; set; }

	/// <summary>
	/// The button's behavior mode.
	/// </summary>
	public enum ButtonMode
	{
		/// <summary>
		/// Normal toggle button - click to turn on, click again to turn off.
		/// </summary>
		Toggle,
		/// <summary>
		/// Button is only on while being held down.
		/// </summary>
		Continuous,
		/// <summary>
		/// Button briefly turns on then immediately off when pressed.
		/// </summary>
		Immediate
	}

	[Property] public ButtonMode Mode { get; set; } = ButtonMode.Toggle;

	[Property, ShowIf( "Mode", ButtonMode.Toggle )] public bool AutoReset { get; set; } = true;
	[Property, ShowIf( "AutoReset", true )] public float ResetTime { get; set; } = 1.0f;

	[Property, Group( "Movement" ), Order( 0 )] public bool Move { get; set; }
	[Property, Group( "Movement" ), ShowIf( nameof( Move ), true )] public GameObject MoveTarget { get; set; }
	[Property, Group( "Movement" ), ShowIf( nameof( Move ), true )] public Vector3 MoveDelta { get; set; }

	/// <summary>
	/// Animation curve to use, X is the time between 0-1 and Y is how much the button is pressed from 0-1.
	/// </summary>
	[Property, Group( "Movement" ), ShowIf( nameof( Move ), true )] public Curve AnimationCurve { get; set; } = new Curve( new Curve.Frame( 0f, 0f ), new Curve.Frame( 1f, 1.0f ) );

	/// <summary>
	/// How long in seconds should it take to animate this button.
	/// </summary>
	[Property, Group( "Movement" ), ShowIf( nameof( Move ), true )] public float AnimationTime { get; set; } = 0.5f;

	Vector3 _initialPosition;
	Transform _startTransform;
	bool _isBeingPressed;
	bool _shouldTurnOffNextFrame;
	GameObject _lastPresser;

	[Sync] private TimeSince LastUse { get; set; }
	[Sync] private bool _isOn { get; set; }

	/// <summary>
	/// True if the button is currently on
	/// </summary>
	public bool IsOn
	{
		get => _isOn;
		private set
		{
			if ( _isOn == value )
				return;

			_isOn = value;
			OnButtonStateChanged( value );
		}
	}

	/// <summary>
	/// True if the button is currently animating
	/// </summary>
	public bool IsAnimating { get; private set; }

	void OnButtonStateChanged( bool isOn )
	{
		OnStateChanged?.Invoke( isOn );

		if ( isOn )
		{
			OnTurnedOn?.Invoke( _lastPresser );
		}
		else
		{
			OnTurnedOff?.Invoke();
		}
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		if ( !Move )
			return;

		Gizmo.Transform = WorldTransform;

		var bbox = GameObject.GetLocalBounds();
		bbox += MoveDelta * MathF.Sin( RealTime.Now * 2.0f ).Remap( -1, 1 );

		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.LineThickness = 3;
		Gizmo.Draw.LineBBox( bbox );
		Gizmo.Draw.IgnoreDepth = true;

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Draw.Color.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( bbox );
	}

	protected override void OnStart()
	{
		var moveTarget = MoveTarget.IsValid() ? MoveTarget : GameObject;
		_startTransform = moveTarget.LocalTransform;
		_initialPosition = _startTransform.Position;
	}

	bool IPressable.CanPress( IPressable.Event e )
	{
		return !IsAnimating;
	}

	bool IPressable.Pressing( IPressable.Event e )
	{
		_isBeingPressed = true;
		return Mode == ButtonMode.Continuous;
	}

	bool IPressable.Press( IPressable.Event e )
	{
		Press( e.Source.GameObject );
		return true;
	}

	void IPressable.Release( IPressable.Event e )
	{
		_isBeingPressed = false;
		Release( e.Source.GameObject );
	}

	/// <summary>
	/// Turns the button on. Does nothing if already on or animating.
	/// </summary>
	[Rpc.Host]
	public void TurnOn( GameObject presser = null )
	{
		// Don't do anything if already on or animating
		if ( IsOn || IsAnimating )
			return;

		_lastPresser = presser;
		LastUse = 0;
		IsAnimating = true;
		IsOn = true;

		if ( OnSound is not null )
			PlaySound( OnSound );

		if ( Mode == ButtonMode.Immediate )
		{
			_shouldTurnOffNextFrame = true;
		}
	}

	/// <summary>
	/// Turns the button off. Does nothing if already off or animating.
	/// </summary>
	[Rpc.Host]
	public void TurnOff()
	{
		// Don't do anything if already off or animating
		if ( !IsOn || IsAnimating )
			return;

		LastUse = 0;
		IsAnimating = true;
		IsOn = false;

		if ( OffSound is not null )
			PlaySound( OffSound );
	}

	/// <summary>
	/// Toggles the button between on and off states.
	/// </summary>
	[Rpc.Host]
	public void Toggle( GameObject presser )
	{
		if ( !IsOn )
		{
			TurnOn( presser );
		}
		else
		{
			TurnOff();
		}
	}

	[Rpc.Host]
	private void Press( GameObject presser )
	{
		if ( IsAnimating )
			return;

		_lastPresser = presser;
		OnPressed?.Invoke( presser );

		switch ( Mode )
		{
			case ButtonMode.Toggle:
				if ( IsOn && !AutoReset )
				{
					TurnOff();
				}
				else
				{
					TurnOn( presser );
				}
				break;

			case ButtonMode.Continuous:
				TurnOn( presser );
				break;

			case ButtonMode.Immediate:
				TurnOn( presser );
				break;
		}
	}

	[Rpc.Host]
	private void Release( GameObject presser )
	{
		OnReleased?.Invoke( presser );

		// For continuous mode, turn off when released
		if ( Mode == ButtonMode.Continuous && IsOn )
		{
			TurnOff();
		}
	}

	[Rpc.Broadcast]
	private void PlaySound( SoundEvent sound )
	{
		GameObject.PlaySound( sound );
	}

	protected override void OnFixedUpdate()
	{
		//
		// Okay, this part could be way better 
		//
		if ( Mode == ButtonMode.Immediate && _shouldTurnOffNextFrame && IsOn && !IsAnimating )
		{
			_shouldTurnOffNextFrame = false;
			TurnOff();
			return;
		}

		if ( Mode == ButtonMode.Toggle && !IsAnimating && IsOn && AutoReset && ResetTime >= 0.0f && LastUse >= ResetTime )
		{
			TurnOff();
			return;
		}

		if ( Mode == ButtonMode.Continuous && IsOn && !IsAnimating && !_isBeingPressed )
		{
			TurnOff();
			return;
		}

		// Continuous: Reset the pressing flag each frame - it will be set again if still being pressed
		if ( Mode == ButtonMode.Continuous )
		{
			_isBeingPressed = false;
		}

		// Don't do anything if we're not animating
		if ( !IsAnimating )
			return;

		// Normalize the last use time to the amount of time to animate
		var time = LastUse.Relative.Remap( 0.0f, AnimationTime, 0.0f, 1.0f );

		// Evaluate our animation curve
		var curve = AnimationCurve.Evaluate( time );

		// Animate backwards if we're turning off (IsOn is false when turning off)
		if ( !IsOn ) curve = 1.0f - curve;

		if ( Move )
		{
			var target = MoveTarget.IsValid() ? MoveTarget : GameObject;
			var targetPosition = _initialPosition + target.LocalTransform.Rotation * (MoveDelta * curve);
			target.LocalTransform = _startTransform.WithPosition( targetPosition );
		}

		// If we're done, finalize the animation
		if ( time < 1f ) return;

		IsAnimating = false;
	}
}
