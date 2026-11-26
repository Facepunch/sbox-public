using Sandbox.Engine.Resources;
using Sandbox.Citizen;
using Sandbox.Npcs.Layers;
using static Sandbox.BaseCombatWeapon;

namespace Sandbox.Npcs;

#pragma warning disable CS0618

/// <summary>
/// Drives the citizen animgraph on the NPC's <see cref="BaseNpc.Renderer"/> from its <see cref="AnimationLayer"/>
/// state, on every client: movement, grounding, link traversal, the held weapon's pose and reactions, and eyes,
/// head and body aim.
/// </summary>
public class CitizenNpcAnimator : Component, IEvents
{
	internal float AimStrengthEyes { get; set; } = 1.0f;
	internal float AimStrengthHead { get; set; } = 1.0f;
	internal float AimStrengthBody { get; set; } = 1.0f;

	/// <summary>How far the head can aim away from the body, in degrees of yaw.</summary>
	internal float MaxAimYaw { get; set; } = 80f;

	/// <summary>How quickly the head sweeps onto (and lets go of) a look target.</summary>
	internal float AimSmoothSpeed { get; set; } = 6f;

	private BaseNpc _npc;
	private BaseNpc Npc => _npc ??= GetComponent<BaseNpc>();

	private AnimationLayer _layer;
	private AnimationLayer Layer => _layer ??= GetComponent<AnimationLayer>();

	private SkinnedModelRenderer _renderer => Npc.IsValid() ? Npc.Renderer : null;

	private float _lastYaw = float.NaN;
	private NavMeshLinkType _shownTraversal;
	private float _shownCrouch;

	// The aim we're actually feeding the animgraph. Look targets appear out of
	// nowhere (senses noticing someone) and jump around; the neck eases toward
	// them and releases the same way, instead of teleporting.
	private Vector3 _aimDirection;
	private float _aimWeight;

	// Which side the head swung for a target directly behind us. Straight behind,
	// the shortest way round is ambiguous and noise flips it left/right every
	// frame -- pick a side and stay on it until the target comes back around.
	private int _aimSide;

	protected override void OnEnabled()
	{
		_lastYaw = float.NaN;
	}

	/// <summary>
	/// The weapon the NPC holds: its combat weapon while that's still in our hierarchy (not dropped).
	/// </summary>
	private BaseCombatWeapon Weapon => Npc is BaseCombatNpc { Weapon: { } weapon } && weapon.IsValid() && weapon.GameObject.IsAncestor( GameObject ) ? weapon : null;

	void IEvents.OnWeaponEvent( BaseCombatWeapon weapon, string name )
	{
		if ( string.Equals( name, WeaponEvents.Attack, StringComparison.OrdinalIgnoreCase ) )
			TriggerGesture( "b_attack" );
		else if ( string.Equals( name, WeaponEvents.Reload, StringComparison.OrdinalIgnoreCase ) )
			TriggerGesture( "b_reload" );
	}

	/// <summary>
	/// The citizen holdtype option for a weapon: its <see cref="BaseCombatWeapon.HoldType"/> when that names
	/// one, else the closest to it or to its <see cref="BaseCombatWeapon.HoldFallback"/>.
	/// </summary>
	private static string HoldTypeOption( BaseCombatWeapon weapon )
	{
		if ( weapon is null )
			return "none";

		if ( CitizenHoldTypes.IsOption( weapon.HoldType ) )
			return weapon.HoldType;

		return CitizenHoldTypes.For( weapon.HoldType, weapon.HoldFallback );
	}

	protected override void OnUpdate()
	{
		var layer = Layer;
		if ( !layer.IsValid() || !_renderer.IsValid() )
			return;

		if ( layer.IsLooking )
			ApplyAim( (layer.CalculateLookPosition() - GetAimOrigin()).Normal, 1f );
		else
			ClearAim();

		ApplyMoveToRenderer( layer.IsCarried ? Struggle( layer.MoveVelocity ) : layer.MoveVelocity, Npc.WorldRotation );

		var weapon = Weapon;
		var hold = HoldTypeOption( weapon );
		var handedness = (int)(weapon?.Handedness ?? WeaponHandedness.Both);
		if ( _renderer.Animation.Graph is not null )
		{
			_renderer.Animation.Parameters.TrySet( "holdtype", hold );
			_renderer.Animation.Parameters.TrySet( "holdtype_handedness", handedness );
		}
		else
		{
			_renderer.Set( "holdtype", hold );
			_renderer.Set( "holdtype_handedness", handedness );
		}

		SetBool( "b_weapon_lower", layer.WeaponStance == AnimationLayer.WeaponStances.Lowered );

		SetBool( "b_grounded", layer.IsGrounded );

		// Climb pose on the vertical part only; the step on or off at the top walks.
		var traversal = layer.Traversal;
		var climbing = traversal is not null && (traversal == NavMeshLinkType.Ladder || traversal == NavMeshLinkType.Climb);
		SetBool( "b_climb", climbing && MathF.Abs( layer.MoveVelocity.z ) > 1f );

		var jump = NavMeshLinkType.Jump;
		if ( traversal is not null && traversal == jump && _shownTraversal != jump )
			TriggerGesture( "b_jump" );
		_shownTraversal = traversal;

		// Only on a change, so a game can still duck the model itself
		if ( layer.Crouch != _shownCrouch )
		{
			_shownCrouch = layer.Crouch;
			SetFloat( "duck", _shownCrouch );
		}
	}

	// The in-air pose flails with the velocity it is shown, so a held NPC is shown falling and kicking about even held still
	private const float StruggleFallSpeed = 500f;
	private const float StruggleSpeed = 300f;

	private Vector3 Struggle( Vector3 velocity )
	{
		velocity.z = MathF.Min( velocity.z, -StruggleFallSpeed );

		var flat = velocity.WithZ( 0 );
		if ( flat.Length >= StruggleSpeed ) return velocity;

		var angle = Time.Now * 1.5f + (Npc.GameObject.Id.GetHashCode() & 0xff) * 0.1f;
		var kick = (Npc.WorldRotation.Right * MathF.Cos( angle ) + Npc.WorldRotation.Forward * MathF.Sin( angle )).WithZ( 0 );
		return (flat + kick.Normal * (StruggleSpeed - flat.Length)).WithZ( velocity.z );
	}

	/// <summary>
	/// Where the head aims from - the model's "eyes" attachment, so we meet the target's gaze
	/// rather than tilting at them from our feet.
	/// </summary>
	private Vector3 GetAimOrigin()
	{
		if ( _renderer.IsValid() && _renderer.GetAttachment( "eyes" ) is { } eyes )
			return eyes.Position;

		return Npc.EyePosition;
	}

	// Release the aim back to the animation - ease the weight off toward body
	// forward rather than dropping it, so the head settles instead of popping.
	// Called every frame while there's no target, which is what drives the ease.
	private void ClearAim()
	{
		if ( _aimWeight <= 0.01f )
		{
			ResetAim();
			return;
		}

		ApplyAim( Npc.WorldRotation.Forward, 0f );
	}

	// Hard-drop the aim with no easing. Uses body forward rather than a zero
	// vector - zero isn't a direction, and the graph does strange things with it.
	private void ResetAim()
	{
		_aimWeight = 0f;
		_aimSide = 0;

		if ( !_renderer.IsValid() )
			return;

		var forward = Npc.WorldRotation.Forward;
		_aimDirection = forward;

		UpdateLookParameters();
	}

	/// <summary>
	/// Ease the applied aim toward a desired direction and weight, clamped to what
	/// a neck can actually do. Every look path feeds through here, so the head
	/// sweeps onto targets instead of snapping, and never flip-flops on a target
	/// directly behind us.
	/// </summary>
	private void ApplyAim( Vector3 desiredDirection, float desiredWeight )
	{
		if ( !_renderer.IsValid() )
			return;

		desiredDirection = ClampAimYaw( desiredDirection );

		// Starting to look from rest - sweep out from where the head naturally sits
		if ( _aimWeight <= 0.01f && desiredWeight > 0f )
			_aimDirection = Npc.WorldRotation.Forward;

		var t = 1f - MathF.Exp( -AimSmoothSpeed * Time.Delta );
		_aimDirection = Vector3.Slerp( _aimDirection, desiredDirection, t, clamp: false ).Normal;
		_aimWeight = _aimWeight.LerpTo( desiredWeight, t );

		UpdateLookParameters();
	}

	private void UpdateLookParameters()
	{
		if ( _renderer.Animation.Graph is not null )
		{
			var aim = _renderer.WorldRotation.Inverse * _aimDirection;
			var angles = Rotation.LookAt( aim ).Angles();
			var parameters = _renderer.Animation.Parameters;
			parameters.TrySet( "aim_body", aim );
			parameters.TrySet( "aim_body_pitch", angles.pitch );
			parameters.TrySet( "aim_body_yaw", angles.yaw );
			parameters.TrySet( "aim_body_weight", AimStrengthBody * _aimWeight );
			parameters.TrySet( "aim_head", aim );
			parameters.TrySet( "aim_head_pitch", angles.pitch );
			parameters.TrySet( "aim_head_yaw", angles.yaw );
			parameters.TrySet( "aim_head_weight", AimStrengthHead * _aimWeight );
			parameters.TrySet( "aim_eyes", aim );
			parameters.TrySet( "aim_eyes_weight", AimStrengthEyes * _aimWeight );
			return;
		}

		_renderer.SetLookDirection( "aim_eyes", _aimDirection, AimStrengthEyes * _aimWeight );
		_renderer.SetLookDirection( "aim_head", _aimDirection, AimStrengthHead * _aimWeight );
		_renderer.SetLookDirection( "aim_body", _aimDirection, AimStrengthBody * _aimWeight );
	}

	// Keep the aim within reach of the neck. A target beyond MaxAimYaw pins the
	// head at the limit on that side while the body turns to catch up; directly
	// behind, the side is ambiguous, so we stick with the one we already chose.
	private Vector3 ClampAimYaw( Vector3 direction )
	{
		var forward = Npc.WorldRotation.Forward.WithZ( 0 ).Normal;
		var flat = direction.WithZ( 0 );

		if ( flat.Length < 0.001f )
			return direction;

		var flatNormal = flat.Normal;
		var yaw = MathF.Atan2( forward.Cross( flatNormal ).z, forward.Dot( flatNormal ) ).RadianToDegree();

		if ( MathF.Abs( yaw ) > 150f )
		{
			if ( _aimSide == 0 )
				_aimSide = yaw >= 0f ? 1 : -1;

			yaw = _aimSide * MaxAimYaw;
		}
		else
		{
			_aimSide = yaw >= 0f ? 1 : -1;
			yaw = Math.Clamp( yaw, -MaxAimYaw, MaxAimYaw );
		}

		var clampedFlat = Rotation.FromAxis( Vector3.Up, yaw ) * forward * flat.Length;
		return (clampedFlat + Vector3.Up * direction.z).Normal;
	}

	private void TriggerGesture( string name )
	{
		var renderer = _renderer;
		if ( !renderer.IsValid() ) return;

		if ( renderer.Animation.Graph is not null )
		{
			renderer.Animation.Parameters.TryTrigger( name );
		}
		else
		{
			renderer.Set( name, true );
		}
	}

	private void SetFloat( string name, float value )
	{
		if ( _renderer.Animation.Graph is not null )
		{
			_renderer.Animation.Parameters.TrySet( name, value );
		}
		else
		{
			_renderer.Set( name, value );
		}
	}

	private void SetBool( string name, bool value )
	{
		if ( _renderer.Animation.Graph is not null )
		{
			_renderer.Animation.Parameters.TrySet( name, value );
		}
		else
		{
			_renderer.Set( name, value );
		}
	}

	private void ApplyMoveToRenderer( Vector3 velocity, Rotation reference )
	{
		if ( _renderer is null ) return;

		var forward = reference.Forward.Dot( velocity );
		var sideward = reference.Right.Dot( velocity );
		var groundSpeed = velocity.WithZ( 0 ).Length;
		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

		var yaw = reference.Angles().yaw.NormalizeDegrees();
		float rotationSpeed = 0f;

		if ( float.IsNaN( _lastYaw ) )
		{
			_lastYaw = yaw;
		}
		else
		{
			var deltaYaw = Angles.NormalizeAngle( yaw - _lastYaw );
			rotationSpeed = Time.Delta > 0f ? MathF.Abs( deltaYaw ) / Time.Delta : 0f;
			_lastYaw = yaw;
		}

		SetFloat( "move_direction", angle );
		SetFloat( "move_speed", velocity.Length );
		SetFloat( "move_groundspeed", groundSpeed );
		SetFloat( "move_y", sideward );
		SetFloat( "move_x", forward );
		SetFloat( "move_z", velocity.z );
		SetFloat( "move_rotationspeed", rotationSpeed );

		SetFloat( "wish_groundspeed", groundSpeed );
		SetFloat( "wish_x", forward );
		SetFloat( "wish_y", sideward );
		SetFloat( "wish_z", velocity.z );
	}
}
