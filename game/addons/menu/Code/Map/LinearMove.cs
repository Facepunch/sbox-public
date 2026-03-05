namespace Sandbox.Mapping;

/// <summary>
/// Moves an object linearly between two positions.
/// </summary>
[EditorHandle( Icon = "expand" )]
[Category( "Gameplay" ), Icon( "open_with" )]
public sealed class LinearMove : Component, Component.IPressable
{
	/// <summary>
	/// Animation curve to use, X is the time between 0-1 and Y is how much the object has moved from 0-1.
	/// </summary>
	[Property] public Curve AnimationCurve { get; set; } = new Curve( new Curve.Frame( 0f, 0f ), new Curve.Frame( 1f, 1.0f ) );

	/// <summary>
	/// Sound to play when opening starts.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent OpenSound { get; set; }

	/// <summary>
	/// Sound to play when fully opened.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent OpenFinishedSound { get; set; }

	/// <summary>
	/// Sound to play when closing starts.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent CloseSound { get; set; }

	/// <summary>
	/// Sound to play when fully closed.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent CloseFinishedSound { get; set; }

	/// <summary>
	/// The distance to move from the starting position.
	/// </summary>
	[Property] public Vector3 MoveDistance { get; set; } = Vector3.Up * 100.0f;

	/// <summary>
	/// How long in seconds should it take to move to the target position.
	/// </summary>
	[Property] public float MoveTime { get; set; } = 1.0f;

	/// <summary>
	/// Start in the open (moved) position.
	/// </summary>
	[Property] public bool StartOpen { get; set; } = false;

	/// <summary>
	/// The mover's state
	/// </summary>
	public enum MoverState
	{
		Open,
		Opening,
		Closing,
		Closed
	}

	Transform _startTransform;

	/// <summary>
	/// Is this mover locked? If locked, it cannot be toggled.
	/// </summary>
	[Property, Sync] public bool IsLocked { get; set; }

	[Sync] private TimeSince LastStateChange { get; set; }
	[Sync] private MoverState _state { get; set; }

	/// <summary>
	/// Called when the mover's state changes.
	/// </summary>
	[Property, Group( "Events" )]
	public Action<MoverState> OnStateChanged { get; set; }

	/// <summary>
	/// Called when the mover starts opening.
	/// </summary>
	[Property, Group( "Events" )]
	public Action OnOpenStart { get; set; }

	/// <summary>
	/// Called when the mover finishes opening.
	/// </summary>
	[Property, Group( "Events" )]
	public Action OnOpenEnd { get; set; }

	/// <summary>
	/// Called when the mover starts closing.
	/// </summary>
	[Property, Group( "Events" )]
	public Action OnCloseStart { get; set; }

	/// <summary>
	/// Called when the mover finishes closing.
	/// </summary>
	[Property, Group( "Events" )]
	public Action OnCloseEnd { get; set; }

	/// <summary>
	/// Called every frame while the mover is moving (opening or closing). Receives the current progress (0-1).
	/// </summary>
	[Property, Group( "Events" )]
	public Action<float> OnMoving { get; set; }

	public MoverState State
	{
		get => _state;
		private set
		{
			if ( _state == value )
				return;

			_state = value;
			OnMoverStateChanged( value );
		}
	}

	void OnMoverStateChanged( MoverState value )
	{
		OnStateChanged?.Invoke( value );

		if ( IsProxy )
			return;

		if ( value == MoverState.Open )
		{
			if ( OpenFinishedSound is not null )
				PlaySound( OpenFinishedSound );

			OnOpenEnd?.Invoke();
		}
		else if ( value == MoverState.Closed )
		{
			if ( CloseFinishedSound is not null )
				PlaySound( CloseFinishedSound );

			OnCloseEnd?.Invoke();
		}
		else if ( value == MoverState.Opening )
		{
			OnOpenStart?.Invoke();
		}
		else if ( value == MoverState.Closing )
		{
			OnCloseStart?.Invoke();
		}
	}

	protected override void OnStart()
	{
		_startTransform = Transform.Local;

		// If starting open, set initial position
		if ( StartOpen )
		{
			Transform.Local = Transform.Local.WithPosition( _startTransform.Position + _startTransform.Rotation * MoveDistance );
			State = MoverState.Open;
		}
		else
		{
			State = MoverState.Closed;
		}
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Transform = WorldTransform;

		var bbox = GameObject.GetLocalBounds();

		// Draw start position
		Gizmo.Draw.Color = Color.Green;
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.LineBBox( bbox );

		// Draw end position
		Gizmo.Draw.Color = Color.Red;
		var endBBox = bbox;
		endBBox += MoveDistance;
		Gizmo.Draw.LineBBox( endBBox );

		// Draw movement line
		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.Line( Vector3.Zero, MoveDistance );

		// Draw animated position
		var animPos = MoveDistance * MathF.Sin( RealTime.Now * 2.0f ).Remap( -1, 1, 0, 1 );
		var animBBox = bbox;
		animBBox += animPos;
		Gizmo.Draw.Color = Color.Cyan;
		Gizmo.Draw.LineThickness = 3;
		Gizmo.Draw.LineBBox( animBBox );
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Color.Cyan.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( animBBox );
	}

	bool IPressable.CanPress( IPressable.Event e )
	{
		return State is MoverState.Open or MoverState.Closed;
	}

	bool IPressable.Press( IPressable.Event e )
	{
		Toggle();
		return true;
	}

	/// <summary>
	/// Opens the mover. Does nothing if already open or opening.
	/// </summary>
	[Rpc.Host]
	public void Open()
	{
		if ( State is MoverState.Open or MoverState.Opening )
		{
			return;
		}

		if ( IsLocked )
		{
			return;
		}

		LastStateChange = 0;
		State = MoverState.Opening;

		if ( OpenSound is not null )
			PlaySound( OpenSound );
	}

	/// <summary>
	/// Closes the mover. Does nothing if already closed or closing.
	/// </summary>
	[Rpc.Host]
	public void Close()
	{
		// Don't do anything if already closed or closing
		if ( State is MoverState.Closed or MoverState.Closing )
			return;

		if ( IsLocked )
		{
			return;
		}

		LastStateChange = 0;
		State = MoverState.Closing;

		if ( CloseSound is not null )
			PlaySound( CloseSound );
	}

	/// <summary>
	/// Toggles the mover between open and closed states.
	/// </summary>
	[Rpc.Host]
	public void Toggle()
	{
		if ( State is MoverState.Closed )
		{
			Open();
		}
		else if ( State is MoverState.Open )
		{
			Close();
		}
	}

	[Rpc.Broadcast]
	private void PlaySound( SoundEvent sound )
	{
		GameObject.PlaySound( sound );
	}

	protected override void OnFixedUpdate()
	{
		// Don't do anything if we're not opening or closing
		if ( State != MoverState.Opening && State != MoverState.Closing )
			return;

		// Normalize the time to the amount of time to move
		var time = LastStateChange.Relative.Remap( 0.0f, MoveTime, 0.0f, 1.0f );

		// Evaluate our animation curve
		var curve = AnimationCurve.Evaluate( time );

		// Move backwards if we're closing
		if ( State == MoverState.Closing ) curve = 1.0f - curve;

		// Call the OnMoving event with current progress
		OnMoving?.Invoke( curve );

		// Calculate the target position
		var targetPosition = _startTransform.Position + _startTransform.Rotation * (MoveDistance * curve);

		// Apply the position
		Transform.Local = Transform.Local.WithPosition( targetPosition );

		// If we're done, finalize the state
		if ( time < 1f ) return;

		State = State == MoverState.Opening ? MoverState.Open : MoverState.Closed;
	}
}
