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
			_renderer?.Set( "b_attack", true );
		else if ( string.Equals( name, WeaponEvents.Reload, StringComparison.OrdinalIgnoreCase ) )
			_renderer?.Set( "b_reload", true );
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
		if ( !layer.IsValid() || !Npc.IsValid() )
			return;

		if ( layer.IsLooking )
			ApplyAim( (layer.CalculateLookPosition() - GetAimOrigin()).Normal, 1f );
		else
			ClearAim();

		ApplyMoveToRenderer( layer.MoveVelocity, Npc.WorldRotation );

		var weapon = Weapon;
		_renderer?.Set( "holdtype", HoldTypeOption( weapon ) );
		_renderer?.Set( "holdtype_handedness", (int)(weapon?.Handedness ?? WeaponHandedness.Both) );

		_renderer?.Set( "b_weapon_lower", layer.WeaponStance == AnimationLayer.WeaponStances.Lowered );

		_renderer?.Set( "b_grounded", layer.IsGrounded );

		// Climb pose on the vertical part only; the step on or off at the top walks.
		var traversal = layer.Traversal;
		var climbing = traversal is not null && (traversal == NavMeshLinkType.Ladder || traversal == NavMeshLinkType.Climb);
		_renderer?.Set( "b_climb", climbing && MathF.Abs( layer.MoveVelocity.z ) > 1f );

		var jump = NavMeshLinkType.Jump;
		if ( traversal is not null && traversal == jump && _shownTraversal != jump )
			_renderer?.Set( "b_jump", true );
		_shownTraversal = traversal;
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

		_renderer.SetLookDirection( "aim_eyes", forward, 0f );
		_renderer.SetLookDirection( "aim_head", forward, 0f );
		_renderer.SetLookDirection( "aim_body", forward, 0f );
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

		_renderer.Set( "move_direction", angle );
		_renderer.Set( "move_speed", velocity.Length );
		_renderer.Set( "move_groundspeed", groundSpeed );
		_renderer.Set( "move_y", sideward );
		_renderer.Set( "move_x", forward );
		_renderer.Set( "move_z", velocity.z );
		_renderer.Set( "move_rotationspeed", rotationSpeed );

		_renderer.Set( "wish_groundspeed", groundSpeed );
		_renderer.Set( "wish_x", forward );
		_renderer.Set( "wish_y", sideward );
		_renderer.Set( "wish_z", velocity.z );
	}
}
