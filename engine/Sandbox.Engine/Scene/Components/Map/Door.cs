namespace Sandbox.Mapping;

[EditorHandle( Icon = "door_back" )]
[Category( "Mapping" ), Icon( "door_back" )]
public sealed class Door : Component, Component.IPressable
{
	public enum DoorMode
	{
		Rotating,
		Sliding
	}

	/// <summary>
	/// Whether this door rotates or slides.
	/// </summary>
	[Property] public DoorMode Mode { get; set; } = DoorMode.Rotating;

	/// <summary>
	/// Animation curve to use, X is the time between 0-1 and Y is how much the door is open to its target angle from 0-1.
	/// </summary>
	[Property] public Curve AnimationCurve { get; set; } = new Curve( new Curve.Frame( 0f, 0f ), new Curve.Frame( 1f, 1.0f ) );

	/// <summary>
	/// Sound to play when a door is opened.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent OpenSound { get; set; }

	/// <summary>
	/// Sound to play when a door is interacted with while locked.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent LockedSound { get; set; }

	/// <summary>
	/// Sound to play when a door is fully opened.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent OpenFinishedSound { get; set; }

	/// <summary>
	/// Sound to play when a door is closed.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent CloseSound { get; set; }

	/// <summary>
	/// Sound to play when a door has finished closing.
	/// </summary>
	[Property, Group( "Sound" )] public SoundEvent CloseFinishedSound { get; set; }

	/// <summary>
	/// Optional linked door that opens when this door opens.
	/// Useful for double doors.
	/// </summary>
	[Property] public Door LinkedDoor { get; set; }

	/// <summary>
	/// Optional pivot point, origin will be used if not specified.
	/// </summary>
	[Property, ShowIf( "Mode", DoorMode.Rotating )] public GameObject Pivot { get; set; }

	/// <summary>
	/// How far should the door rotate.
	/// </summary>
	[Property, Range( 0.0f, 180.0f ), ShowIf( "Mode", DoorMode.Rotating )] public float TargetAngle { get; set; } = 90.0f;

	/// <summary>
	/// Local-space offset the door slides to when fully open.
	/// </summary>
	[Property, ShowIf( "Mode", DoorMode.Sliding )] public Vector3 SlideOffset { get; set; } = Vector3.Up * 100.0f;

	/// <summary>
	/// Speed. Degrees per second for rotating, units per second for sliding.
	/// </summary>
	[Property] public float Speed { get; set; } = 100.0f;

	/// <summary>
	/// Open away from the person who uses this door.
	/// </summary>
	[Property, ShowIf( "Mode", DoorMode.Rotating )] public bool OpenAwayFromPlayer { get; set; } = true;

	/// <summary>
	/// Can this door be opened by pressing it.
	/// </summary>
	[Property] public bool IsUsable { get; set; } = true;

	/// <summary>
	/// Start in the open position.
	/// </summary>
	[Property] public bool StartOpen { get; set; } = false;

	/// <summary>
	/// Automatically close after opening.
	/// </summary>
	[Property] public bool AutoClose { get; set; } = false;

	/// <summary>
	/// Delay before automatically closing (in seconds). -1 means stay open.
	/// </summary>
	[Property, ShowIf( "AutoClose", true )] public float AutoCloseDelay { get; set; } = 4.0f;

	/// <summary>
	/// The door's state
	/// </summary>
	public enum DoorState
	{
		Open,
		Opening,
		Closing,
		Closed
	}

	Transform _startTransform;
	Vector3 _pivotPosition;
	bool _reverseDirection;
	GameObject _lastPresser;

	/// <summary>
	/// Is this door locked?
	/// </summary>
	[Property, Sync] public bool IsLocked { get; set; }

	[Sync] private TimeSince LastUse { get; set; }
	[Sync] private DoorState _state { get; set; }

	/// <summary>
	/// Called when the door is opened. Receives the GameObject that opened it.
	/// </summary>
	[Property, Group( "Events" ), Doo.ArgumentHint<GameObject>( "user", Help = "The person who opened the door." )]
	public Doo OnOpen { get; set; }

	/// <summary>
	/// Called when the door is closed.
	/// </summary>
	[Property, Group( "Events" )]
	public Doo OnClose { get; set; }

	public DoorState State
	{
		get => _state;
		private set
		{
			if ( _state == value )
				return;

			_state = value;
			OnDoorStateChanged( value );
		}
	}

	void OnDoorStateChanged( DoorState value )
	{
		if ( IsProxy )
			return;

		if ( value == DoorState.Open )
		{
			if ( OpenFinishedSound is not null )
				PlaySound( OpenFinishedSound );
		}
		else if ( value == DoorState.Closed )
		{
			if ( CloseFinishedSound is not null )
				PlaySound( CloseFinishedSound );
		}
	}

	protected override void OnStart()
	{
		_startTransform = Transform.Local;
		_pivotPosition = Pivot is not null ? Pivot.WorldPosition : _startTransform.Position;

		if ( StartOpen )
		{
			if ( Mode == DoorMode.Sliding )
			{
				Transform.Local = _startTransform.WithPosition( _startTransform.Position + SlideOffset );
			}
			else
			{
				Transform.Local = _startTransform.RotateAround( _pivotPosition, Rotation.FromYaw( TargetAngle ) );
			}

			State = DoorState.Open;
		}
		else
		{
			State = DoorState.Closed;
		}

		if ( LinkedDoor.IsValid() )
		{
			if ( !LinkedDoor.LinkedDoor.IsValid() )
				LinkedDoor.LinkedDoor = this;
		}
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Gizmo.IsSelected )
			return;

		if ( Mode == DoorMode.Sliding )
		{
			var delta = MathF.Sin( RealTime.Now * 2.0f ).Remap( -1, 1 );
			DrawAtSlide( delta );
			DrawAtSlide( 0 );
			DrawAtSlide( 1 );
			return;
		}

		var pivotPos = Pivot is not null ? Pivot.LocalPosition : Vector3.Zero;

		using ( Gizmo.Scope( "Tool", new Transform( pivotPos ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.1f;

			if ( Gizmo.Control.Position( "pivot", 0, out var newPivot ) )
			{
				if ( Pivot is not null )
				{
					Pivot.LocalPosition += newPivot;
				}
			}
		}

		var delta2 = MathF.Sin( RealTime.Now * 2.0f ).Remap( -1, 1 );
		DrawAt( delta2 );
		DrawAt( 0 );
		DrawAt( 1 );
	}

	void DrawAtSlide( float f )
	{
		Gizmo.Transform = WorldTransform.WithPosition( WorldTransform.Position + WorldTransform.Rotation * (SlideOffset * f) );

		var bbox = GameObject.GetLocalBounds();

		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.LineThickness = 3;
		Gizmo.Draw.LineBBox( bbox );
		Gizmo.Draw.IgnoreDepth = true;

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Draw.Color.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( bbox );
	}

	void DrawAt( float f )
	{
		var pivotWorld = Pivot is not null ? Pivot.WorldPosition : WorldTransform.Position;
		Gizmo.Transform = WorldTransform.RotateAround( pivotWorld, Rotation.FromYaw( TargetAngle * f ) );

		var bbox = GameObject.GetLocalBounds();

		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.LineThickness = 3;
		Gizmo.Draw.LineBBox( bbox );
		Gizmo.Draw.IgnoreDepth = true;

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Draw.Color.WithAlpha( 0.3f );
		Gizmo.Draw.LineBBox( bbox );
	}

	bool IPressable.CanPress( IPressable.Event e )
	{
		return IsUsable && (State is DoorState.Open or DoorState.Closed);
	}

	bool IPressable.Press( IPressable.Event e )
	{
		if ( !IsUsable )
			return false;

		Toggle( e.Source.GameObject );
		return true;
	}

	/// <summary>
	/// Opens the door. Does nothing if already open or opening.
	/// </summary>
	[Rpc.Host]
	public void Open( GameObject presser = null )
	{
		if ( State is DoorState.Open or DoorState.Opening )
			return;

		if ( IsLocked )
		{
			if ( LockedSound is not null )
				PlaySound( LockedSound );
			return;
		}

		_lastPresser = presser;
		LastUse = 0;
		State = DoorState.Opening;

		if ( OpenSound is not null )
			PlaySound( OpenSound );

		Run( OnOpen, c => c.SetArgument( "user", _lastPresser ) );

		if ( Mode == DoorMode.Rotating && OpenAwayFromPlayer && presser.IsValid() )
		{
			var doorToPlayer = (presser.WorldPosition - _pivotPosition).Normal;
			var doorForward = Transform.Local.Rotation.Forward;

			_reverseDirection = Vector3.Dot( doorToPlayer, doorForward ) > 0;
		}
		else
		{
			_reverseDirection = false;
		}

		if ( LinkedDoor.IsValid() && LinkedDoor != this )
		{
			LinkedDoor.Open( presser );
		}
	}

	/// <summary>
	/// Closes the door. Does nothing if already closed or closing.
	/// </summary>
	[Rpc.Host]
	public void Close()
	{
		if ( State is DoorState.Closed or DoorState.Closing )
			return;

		LastUse = 0;
		State = DoorState.Closing;

		if ( CloseSound is not null )
			PlaySound( CloseSound );

		Run( OnClose );

		if ( LinkedDoor.IsValid() && LinkedDoor != this )
		{
			LinkedDoor.Close();
		}
	}

	/// <summary>
	/// Toggles the door between open and closed states.
	/// </summary>
	[Rpc.Host]
	public void Toggle( GameObject presser = null )
	{
		if ( State is DoorState.Closed )
		{
			Open( presser );
		}
		else if ( State is DoorState.Open )
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
		if ( State == DoorState.Open && AutoClose && AutoCloseDelay >= 0.0f && LastUse >= AutoCloseDelay )
		{
			Close();
			return;
		}

		if ( State is not DoorState.Opening and not DoorState.Closing )
			return;

		if ( Mode == DoorMode.Sliding )
		{
			UpdateSliding();
		}
		else
		{
			UpdateRotating();
		}
	}

	void UpdateRotating()
	{
		var openTime = MathF.Max( MathF.Abs( TargetAngle ) / MathF.Max( Speed, 1.0f ), 0.001f );
		var time = LastUse.Relative.Remap( 0.0f, openTime, 0.0f, 1.0f );

		var curve = AnimationCurve.Evaluate( time );

		if ( State == DoorState.Closing ) curve = 1.0f - curve;

		var targetAngle = TargetAngle;
		if ( _reverseDirection ) targetAngle *= -1.0f;

		Transform.Local = _startTransform.RotateAround( _pivotPosition, Rotation.FromYaw( curve * targetAngle ) );

		if ( time < 1f ) return;

		State = State == DoorState.Opening ? DoorState.Open : DoorState.Closed;

		if ( State == DoorState.Closed )
		{
			_reverseDirection = false;
		}
	}

	void UpdateSliding()
	{
		var openTime = MathF.Max( SlideOffset.Length / MathF.Max( Speed, 1.0f ), 0.001f );
		var time = LastUse.Relative.Remap( 0.0f, openTime, 0.0f, 1.0f );

		var curve = AnimationCurve.Evaluate( time );

		if ( State == DoorState.Closing ) curve = 1.0f - curve;

		Transform.Local = _startTransform.WithPosition( _startTransform.Position + SlideOffset * curve );

		if ( time < 1f ) return;

		State = State == DoorState.Opening ? DoorState.Open : DoorState.Closed;
	}

	[Property, Feature( "Tooltip" )]
	public string OpenTooltipTitle { get; set; } = "Open";

	[Property, Feature( "Tooltip" ), IconName]
	public string OpenTooltipIcon { get; set; } = "touch_app";

	[Property, Feature( "Tooltip" )]
	public string OpenTooltipDescription { get; set; } = "";

	[Header( "Close State" )]
	[Property, Feature( "Tooltip" )]
	public string CloseTooltipTitle { get; set; } = "Close";

	[Property, Feature( "Tooltip" ), IconName]
	public string CloseTooltipIcon { get; set; } = "touch_app";

	[Property, Feature( "Tooltip" )]
	public string CloseTooltipDescription { get; set; } = "";

	[Header( "Locked State" )]
	[Property, Feature( "Tooltip" )]
	public string LockedTooltipTitle { get; set; } = "Locked";

	[Property, Feature( "Tooltip" ), IconName]
	public string LockedTooltipIcon { get; set; } = "lock";

	[Property, Feature( "Tooltip" )]
	public string LockedTooltipDescription { get; set; } = "";

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		if ( !IsUsable ) return null;

		if ( IsLocked )
		{
			return new IPressable.Tooltip( LockedTooltipTitle, LockedTooltipIcon, LockedTooltipDescription );
		}
		else if ( State == DoorState.Open )
		{
			return new IPressable.Tooltip( CloseTooltipTitle, CloseTooltipIcon, CloseTooltipDescription );
		}
		else if ( State == DoorState.Closed )
		{
			return new IPressable.Tooltip( OpenTooltipTitle, OpenTooltipIcon, OpenTooltipDescription );
		}

		return new IPressable.Tooltip( OpenTooltipTitle, OpenTooltipIcon, OpenTooltipDescription );
	}
}
