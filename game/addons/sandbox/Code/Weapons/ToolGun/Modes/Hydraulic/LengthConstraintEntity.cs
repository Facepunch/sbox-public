
using Sandbox.Utility;

/// <summary>
/// A length-changing constraint: a SliderJoint whose length is puppeted between MinLength
/// and MaxLength by player input, wires, or an idle animation loop. Hydraulic, Muscle and
/// Winch are the same component with different presentation and defaults - <see cref="Kind"/>
/// just picks which one this instance is, for undo names, tool info and wiring titles.
/// </summary>
[Title( "#tool.entity.lengthconstraintentity" )]
public sealed class LengthConstraintEntity : Component, IPlayerControllable
{
	public enum ConstraintKind
	{
		Hydraulic,
		Muscle,
		Winch
	}

	[Property]
	public ConstraintKind Kind { get; set; }

	[Property, Range( 0, 1 )]
	public GameObject OnEffect { get; set; }

	[Property, Range( 0, 100 ), ClientEditable, Title( "#tool.setting.min_length" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float MinLength { get; set; } = 10f;

	[Property, Range( 0, 100 ), ClientEditable, Title( "#tool.setting.max_length" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float MaxLength { get; set; } = 100f;

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.length" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Length { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 0.25f;

	[Property, ClientEditable, Title( "#tool.setting.push" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Push { get; set; }

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.push_speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float PushSpeed { get; set; } = 0.25f;

	[Property, ClientEditable, Title( "#tool.setting.pull" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Pull { get; set; }

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.pull_speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float PullSpeed { get; set; } = 0.25f;

	[Property, ClientEditable, Title( "#tool.setting.toggle" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Toggle { get; set; }

	/// <summary>
	/// While the input is active we'll extend towards full length
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.activate" ), Description( "#tool.setting.lengthconstraintentity.activate.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Activate { get; set; }

	[Property]
	public SliderJoint Joint { get; set; }

	[Property, ClientEditable, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Title( "#tool.setting.animated" ), Order( 2 )]
	public bool Animated { get; set; }

	[Property, ClientEditable, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Range( 0, 10 ), Title( "#tool.setting.animation_speed" ), Order( 2 )]
	public float AnimationSpeed { get; set; } = 1.0f;

	[Property, ClientEditable, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Title( "#tool.setting.ease_in" ), Order( 2 )]
	public EaseType EaseIn { get; set; } = EaseType.Linear;

	[Property, ClientEditable, ToggleGroup( "Animated", Label = "#tool.setting.animated" ), Title( "#tool.setting.ease_out" ), Order( 2 )]
	public EaseType EaseOut { get; set; } = EaseType.Linear;

	/// <summary>
	/// Emits the current 0-1 extension whenever it changes. Wire it into a gauge, a
	/// dependent mechanism, or another length constraint's Push/Pull to gang two rams together.
	/// </summary>
	[SignalOutput, Title( "#tool.signal.position" ), Description( "#tool.signal.position.description" )]
	public SignalOutput Position { get; set; } = new();

	public enum EaseType
	{
		[Title( "#tool.option.linear" )]
		Linear,
		[Title( "#tool.option.ease_in" )]
		EaseIn,
		[Title( "#tool.option.ease_out" )]
		EaseOut,
		[Title( "#tool.option.ease_in_out" )]
		EaseInOut,
		[Title( "#tool.option.bounce" )]
		Bounce
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		OnEffect?.Enabled = false;
	}

	bool _state;

	public void SetActiveState( bool state )
	{
		if ( _state == state ) return;

		_state = state;

		OnEffect?.Enabled = state;

		Network.Refresh();

	}

	float? _lastTargetValue;
	float? _targetValue;
	float _lastEmitted = -1f;

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		if ( _targetValue.HasValue )
		{
			if ( _targetValue > Length )
			{
				Length += PushSpeed * Time.Delta * 5.0f;

				if ( Length > 1 )
				{
					_targetValue = null;
				}
			}
			else
			{
				Length -= PullSpeed * Time.Delta * 5.0f;

				if ( Length < 0 )
				{
					_targetValue = null;
				}
			}
		}

		Length = Length.Clamp( 0, 1 );

		if ( !_lastEmitted.AlmostEqual( Length, 0.001f ) )
		{
			_lastEmitted = Length;
			Position.Emit( this, Length );
		}
	}

	[SignalInput( Id = nameof( Push ), Default = true )]
	public void PushSignal( float amount ) => PushLength( amount );

	[SignalInput( Id = nameof( Pull ) )]
	public void PullSignal( float amount ) => PullLength( amount );

	[SignalInput( Id = nameof( Toggle ) )]
	public void ToggleSignal() => ToggleTarget();

	[SignalInput( Id = nameof( Activate ) )]
	public void ActivateSignal( SignalEvent value ) => ActivateLength( value.Analog, value.Released );

	public void OnControl()
	{
		PushLength( Push.GetAnalog() );
		PullLength( Pull.GetAnalog() );
		ActivateLength( Activate.GetAnalog(), Activate.Released() );
		if ( Toggle.Pressed() ) ToggleTarget();
	}

	private void PushLength( float amount )
	{
		if ( Networking.IsHost && amount > 0.5f )
			Length = (Length + PushSpeed * Time.Delta * 5f).Clamp( 0f, 1f );
	}

	private void PullLength( float amount )
	{
		if ( Networking.IsHost && amount > 0.5f )
			Length = (Length - PullSpeed * Time.Delta * 5f).Clamp( 0f, 1f );
	}

	private void ActivateLength( float amount, bool released )
	{
		if ( !Networking.IsHost ) return;
		if ( amount > 0.5f ) Length = (Length + Speed * Time.Delta).Clamp( 0f, 1f );
		else if ( released ) Length = 0f;
	}

	private void ToggleTarget()
	{
		_targetValue = _lastTargetValue.HasValue ? (_lastTargetValue > 0.5f ? 0f : 1f) : 1f;
		_lastTargetValue = _targetValue;
	}

	float _animTime = 0;

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !Joint.IsValid() ) return;

		var line = Joint.Body.WorldPosition - Joint.GameObject.WorldPosition;
		var line_rot = Rotation.LookAt( line, WorldRotation.Up );

		DebugOverlay.Line( Joint.GameObject.WorldPosition, Joint.Body.WorldPosition, Color.Green );

		if ( Animated )
		{
			_animTime += Time.Delta * AnimationSpeed * 0.33f;
			_animTime = _animTime % 2;

			var delta = _animTime;
			if ( delta > 1 )
			{
				delta = 2 - delta;
				delta = GetEase( 1 - delta, EaseOut );
				delta = 1 - delta;
			}
			else
			{
				delta = GetEase( delta, EaseIn );
			}

			Length = (delta);
		}


		Joint.MinLength = MinLength + (Length * (MaxLength - MinLength));
		Joint.MaxLength = MinLength + (Length * (MaxLength - MinLength));

		if ( GetComponent<CapsuleCollider>() is CapsuleCollider capsule )
		{
			capsule.Static = false;
			capsule.Start = capsule.WorldTransform.PointToLocal( Joint.GameObject.WorldPosition );
			capsule.End = capsule.WorldTransform.PointToLocal( Joint.Body.WorldPosition );
			capsule.Radius = 1.0f;
			capsule.ColliderFlags = ColliderFlags.IgnoreMass;
			capsule.Tags.Set( "trigger", true );
		}

		if ( GetComponent<SkinnedModelRenderer>() is SkinnedModelRenderer renderer )
		{
			renderer.CreateBoneObjects = true;

			var len = line.Length - MinLength;

			var a = Joint.GameObject.WorldPosition;
			var b = a + line.Normal * MinLength * 0.5f;
			var c = b + line.Normal * len;
			var d = c + line.Normal * MinLength * 0.5f;

			renderer.GetBoneObject( 0 )?.WorldTransform = new Transform( a, line_rot );
			renderer.GetBoneObject( 0 )?.Flags |= GameObjectFlags.ProceduralBone;
			renderer.GetBoneObject( 1 )?.WorldTransform = new Transform( b, line_rot ); ;
			renderer.GetBoneObject( 1 )?.Flags |= GameObjectFlags.ProceduralBone;
			renderer.GetBoneObject( 2 )?.WorldTransform = new Transform( c, line_rot ); ;
			renderer.GetBoneObject( 2 )?.Flags |= GameObjectFlags.ProceduralBone;
			renderer.GetBoneObject( 3 )?.WorldTransform = new Transform( d, line_rot ); ;
			renderer.GetBoneObject( 3 )?.Flags |= GameObjectFlags.ProceduralBone;
		}

	}

	private float GetEase( float delta, EaseType easeIn )
	{
		switch ( easeIn )
		{
			case EaseType.Linear: return delta;
			case EaseType.EaseIn: return Easing.EaseIn( delta );
			case EaseType.EaseOut: return Easing.EaseOut( delta );
			case EaseType.EaseInOut: return Easing.EaseInOut( delta );
			case EaseType.Bounce: return Easing.BounceOut( delta );
		}

		return delta;
	}
}
