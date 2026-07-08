using System.Text.Json.Serialization;

namespace Sandbox.Citizen;

/// <summary>
/// Used to control the Citizen animation state. You don't have to use this to animate your citizen avatar, but our
/// aim is to put everything you need in this class, so you can easily see what variables are available.
/// </summary>
[Title( "Citizen Animation Helper" )]
[Category( "Citizen" )]
[Icon( "directions_run" )]
[Alias( "CitizenAnimation" )]
public sealed class CitizenAnimationHelper : Component, Component.ExecuteInEditor
{
	SkinnedModelRenderer _target;

	/// <summary>
	/// The skinned model renderer that we'll apply all parameters to.
	/// </summary>
	[Property]
	public SkinnedModelRenderer Target
	{
		get => _target.IsValid() ? _target : null;
		set => _target = value;
	}

	/// <summary>
	/// Where are the eyes of our character?
	/// </summary>
	[Property] public GameObject EyeSource { get; set; }

	/// <summary>
	/// How tall are we?
	/// </summary>
	[Property, Range( 0.5f, 1.5f ), Title( "Avatar Height Scale" )] public float? Height { get; set; }

	/// <summary>
	/// Are we looking at something? Useful for stuff like cutscenes, where you want an NPC to stare at you.
	/// </summary>
	[Property, FeatureEnabled( "Look At", Icon = "visibility" )]
	public bool LookAtEnabled { get; set; } = false;

	/// <summary>
	/// Which GameObject should the head/body rotate towards? Eyes will also use this unless a separate eye target is set.
	/// </summary>
	[Property, Feature( "Look At", Icon = "visibility" ), Title( "Look At Target (Head)" )] public GameObject LookAt { get; set; }

	/// <summary>
	/// Optional separate target for eye direction. If not set, the eyes will follow the main Look At target.
	/// </summary>
	[Property, Feature( "Look At", Icon = "visibility" ), Title( "Look At Target (Eyes)" )] public GameObject LookAtEyes { get; set; }

	[Property, Feature( "Look At", Icon = "visibility" ), Range( 0, 1 )] public float EyesWeight { get; set; } = 1.0f;
	[Property, Feature( "Look At", Icon = "visibility" ), Range( 0, 1 )] public float HeadWeight { get; set; } = 1.0f;
	[Property, Feature( "Look At", Icon = "visibility" ), Range( 0, 1 )] public float BodyWeight { get; set; } = 1.0f;

	/// <summary>
	/// IK will try to place the limb where this GameObject is in the world.
	/// </summary>
	[Property, Group( "Inverse kinematics" ), Title( "Left Hand" )] public GameObject IkLeftHand { get; set; }

	/// <inheritdoc cref="IkLeftHand"/>
	[Property, Group( "Inverse kinematics" ), Title( "Right Hand" )] public GameObject IkRightHand { get; set; }

	/// <inheritdoc cref="IkLeftHand"/>
	[Property, Group( "Inverse kinematics" ), Title( "Left Foot" )] public GameObject IkLeftFoot { get; set; }

	/// <inheritdoc cref="IkLeftHand"/>
	[Property, Group( "Inverse kinematics" ), Title( "Right Foot" )] public GameObject IkRightFoot { get; set; }

	protected override void OnUpdate()
	{
		if ( !Target.IsValid() )
			return;

		if ( LookAtEnabled )
		{
			var eyePos = EyeWorldTransform.Position;

			if ( LookAt.IsValid() )
			{
				var headDir = (LookAt.WorldPosition - eyePos).Normal;
				Target.SetLookDirection( "aim_head", headDir, HeadWeight );
				Target.SetLookDirection( "aim_body", headDir, BodyWeight );

				// Use separate eye target if set, otherwise fall back to main LookAt
				var eyeTarget = LookAtEyes.IsValid() ? LookAtEyes : LookAt;
				var eyeDir = (eyeTarget.WorldPosition - eyePos).Normal;
				Target.SetLookDirection( "aim_eyes", eyeDir, EyesWeight );
			}
			else if ( LookAtEyes.IsValid() )
			{
				// Only eye target is set, use it for everything
				var eyeDir = (LookAtEyes.WorldPosition - eyePos).Normal;
				WithLook( eyeDir, EyesWeight, HeadWeight, BodyWeight );
			}
		}

		if ( Height.HasValue )
		{
			Target.Set( "scale_height", Height.Value );
		}

		if ( IkLeftHand.IsValid() && IkLeftHand.Active ) Target.SetIk( "hand_left", IkLeftHand.WorldTransform );
		else Target.ClearIk( "hand_left" );

		if ( IkRightHand.IsValid() && IkRightHand.Active ) Target.SetIk( "hand_right", IkRightHand.WorldTransform );
		else Target.ClearIk( "hand_right" );

		if ( IkLeftFoot.IsValid() && IkLeftFoot.Active ) Target.SetIk( "foot_left", IkLeftFoot.WorldTransform );
		else Target.ClearIk( "foot_left" );

		if ( IkRightFoot.IsValid() && IkRightFoot.Active ) Target.SetIk( "foot_right", IkRightFoot.WorldTransform );
		else Target.ClearIk( "foot_right" );
	}

	public void ProceduralHitReaction( DamageInfo info, float damageScale = 1.0f, Vector3 force = default )
	{
		if ( Target is null ) return;

		var boneId = info.Hitbox?.Bone?.Index ?? 0;
		var bone = Target.GetBoneObject( boneId );

		var localToBone = bone.LocalPosition;
		if ( localToBone == Vector3.Zero ) localToBone = Vector3.One;

		Target.Set( "hit", true );
		Target.Set( "hit_bone", boneId );
		Target.Set( "hit_offset", localToBone );
		Target.Set( "hit_direction", force.Normal );
		Target.Set( "hit_strength", (force.Length / 1000.0f) * damageScale );
	}

	/// <summary>
	/// The transform of the eyes, in world space. This is worked out from EyeSource is it's set.
	/// </summary>
	public Transform EyeWorldTransform
	{
		get
		{
			if ( EyeSource.IsValid() ) return EyeSource.WorldTransform;

			return WorldTransform;
		}
	}


	/// <summary>
	/// Have the player look at this point in the world
	/// </summary>
	public void WithLook( Vector3 lookDirection, float eyesWeight = 1.0f, float headWeight = 1.0f, float bodyWeight = 1.0f )
	{
		Target?.SetLookDirection( "aim_eyes", lookDirection, eyesWeight );
		Target?.SetLookDirection( "aim_head", lookDirection, headWeight );
		Target?.SetLookDirection( "aim_body", lookDirection, bodyWeight );
	}

	/// <summary>
	/// Have the player animate moving with a set velocity (this doesn't move them! Your character controller is responsible for that)
	/// </summary>
	/// <param name="Velocity"></param>
	public void WithVelocity( Vector3 Velocity )
	{
		if ( Target is null ) return;

		var dir = Velocity;
		var forward = Target.WorldRotation.Forward.Dot( dir );
		var sideward = Target.WorldRotation.Right.Dot( dir );

		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

		Target.Set( "move_direction", angle );
		Target.Set( "move_speed", Velocity.Length );
		Target.Set( "move_groundspeed", Velocity.WithZ( 0 ).Length );
		Target.Set( "move_y", sideward );
		Target.Set( "move_x", forward );
		Target.Set( "move_z", Velocity.z );
	}

	/// <summary>
	/// Animates the wish for the character to move in a certain direction. For example, when in the air, your character will swing their arms in that direction.
	/// </summary>
	/// <param name="Velocity"></param>
	public void WithWishVelocity( Vector3 Velocity )
	{
		if ( Target is null ) return;

		var dir = Velocity;
		var forward = Target.WorldRotation.Forward.Dot( dir );
		var sideward = Target.WorldRotation.Right.Dot( dir );

		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

		Target.Set( "wish_direction", angle );
		Target.Set( "wish_speed", Velocity.Length );
		Target.Set( "wish_groundspeed", Velocity.WithZ( 0 ).Length );
		Target.Set( "wish_y", sideward );
		Target.Set( "wish_x", forward );
		Target.Set( "wish_z", Velocity.z );
	}

	/// <summary>
	/// Where are we aiming?
	/// </summary>
	public Rotation AimAngle
	{
		set
		{
			if ( Target is null ) return;

			value = Target.WorldRotation.Inverse * value;
			var ang = value.Angles();

			Target.Set( "aim_body_pitch", ang.pitch );
			Target.Set( "aim_body_yaw", ang.yaw );
		}
	}

	/// <summary>
	/// The weight of the aim angle, but specifically for the Citizen's eyes.
	/// </summary>
	public float AimEyesWeight
	{
		get => Target?.GetFloat( "aim_eyes_weight" ) ?? 0f;
		set => Target?.Set( "aim_eyes_weight", value );
	}

	/// <summary>
	/// The weight of the aim angle, but specifically for the Citizen's head.
	/// </summary>
	public float AimHeadWeight
	{
		get => Target?.GetFloat( "aim_head_weight" ) ?? 0f;
		set => Target?.Set( "aim_head_weight", value );
	}


	/// <summary>
	/// The weight of the aim angle, but specifically for the Citizen's body.
	/// </summary>
	public float AimBodyWeight
	{
		get => Target?.GetFloat( "aim_body_weight" ) ?? 0f;
		set => Target?.Set( "aim_body_weight", value );
	}

	/// <summary>
	/// How much the character is rotating in degrees per second, this controls feet shuffling.
	/// If rotating clockwise this should be positive, if rotating counter-clockwise this should be negative.
	/// </summary>
	public float MoveRotationSpeed
	{
		get => Target?.GetFloat( "move_rotationspeed" ) ?? 0f;
		set => Target?.Set( "move_rotationspeed", value );
	}

	[Obsolete( "Use MoveRotationSpeed" )]
	public float FootShuffle
	{
		get => Target?.GetFloat( "move_shuffle" ) ?? 0f;
		set => Target?.Set( "move_shuffle", value );
	}

	/// <summary>
	/// The scale of being ducked (crouched) (0 - 1)
	/// </summary>
	[Property, JsonIgnore, Group( "Movement" ), Range( 0, 1 )]
	public float DuckLevel
	{
		get => Target?.GetFloat( "duck" ) ?? 0f;
		set => Target?.Set( "duck", value );
	}

	/// <summary>
	/// How loud are we talking?
	/// </summary>
	public float VoiceLevel
	{
		get => Target?.GetFloat( "voice" ) ?? 0f;
		set => Target?.Set( "voice", value );
	}

	/// <summary>
	/// Are we sitting down?
	/// </summary>
	public bool IsSitting
	{
		get => Target?.GetBool( "b_sit" ) ?? false;
		set => Target?.Set( "b_sit", value );
	}

	/// <summary>
	/// Are we on the ground?
	/// </summary>
	[Property, JsonIgnore, Group( "Movement" )]
	public bool IsGrounded
	{
		get => Target?.GetBool( "b_grounded" ) ?? false;
		set => Target?.Set( "b_grounded", value );
	}

	/// <summary>
	/// Are we swimming?
	/// </summary>
	[Property, JsonIgnore, Group( "Movement" )]
	public bool IsSwimming
	{
		get => Target?.GetBool( "b_swim" ) ?? false;
		set => Target?.Set( "b_swim", value );
	}

	/// <summary>
	/// Are we climbing?
	/// </summary>
	public bool IsClimbing
	{
		get => Target?.GetBool( "b_climbing" ) ?? false;
		set => Target?.Set( "b_climbing", value );
	}

	/// <summary>
	/// Are we noclipping?
	/// </summary>
	[Property, JsonIgnore, Group( "Movement" )]
	public bool IsNoclipping
	{
		get => Target?.GetBool( "b_noclip" ) ?? false;
		set => Target?.Set( "b_noclip", value );
	}

	/// <summary>
	/// Is the weapon lowered? By default, this'll happen when the character hasn't been shooting for a while.
	/// </summary>
	[Property, JsonIgnore, Feature( "Weapon", Icon = "sports_martial_arts" )]
	public bool IsWeaponLowered
	{
		get => Target?.GetBool( "b_weapon_lower" ) ?? false;
		set => Target?.Set( "b_weapon_lower", value );
	}

	public enum HoldTypes
	{
		None,
		Pistol,
		Rifle,
		Shotgun,
		HoldItem,
		Punch,
		Swing,
		RPG,
		Physgun
	}

	/// <summary>
	/// What kind of weapon are we holding?
	/// </summary>
	[Property, JsonIgnore, Feature( "Weapon", Icon = "sports_martial_arts" )]
	public HoldTypes HoldType
	{
		get => (HoldTypes)(Target?.GetInt( "holdtype" ) ?? 0);
		set => Target?.Set( "holdtype", (int)value );
	}

	public enum Hand
	{
		Both,
		Right,
		Left
	}

	/// <summary>
	/// What's the handedness of our weapon? Left handed, right handed, or both hands? This is only supported by some holdtypes, like Pistol, HoldItem.
	/// </summary>
	[Property, JsonIgnore, Feature( "Weapon", Icon = "sports_martial_arts" )]
	public Hand Handedness
	{
		get => (Hand)(Target?.GetInt( "holdtype_handedness" ) ?? 0);
		set => Target?.Set( "holdtype_handedness", (int)value );
	}

	/// <summary>
	/// Triggers a jump animation
	/// </summary>
	public void TriggerJump()
	{
		Target?.Set( "b_jump", true );
	}

	/// <summary>
	/// Triggers a weapon deploy animation
	/// </summary>
	public void TriggerDeploy()
	{
		Target?.Set( "b_deploy", true );
	}

	public enum MoveStyles
	{
		Auto,
		Walk,
		Run
	}

	/// <summary>
	/// We can force the model to walk or run, or let it decide based on the speed.
	/// </summary>
	public MoveStyles MoveStyle
	{
		get => (MoveStyles)(Target?.GetInt( "move_style" ) ?? 0);
		set => Target?.Set( "move_style", (int)value );
	}

	public enum SpecialMoveStyle
	{
		None,
		LedgeGrab,
		Roll,
		Slide
	}

	/// <summary>
	/// We can force the model to have a specific movement state, instead of just running around.
	/// <see cref="SpecialMoveStyle.LedgeGrab"/> is good for shimmying across a ledge.
	/// <see cref="SpecialMoveStyle.Roll"/> is good for a platformer game where the character is rolling around continuously.
	/// <see cref="SpecialMoveStyle.Slide"/> is good for a shooter game or a platformer where the character is sliding.
	/// </summary>
	[Property, Group( "Movement" )]
	public SpecialMoveStyle SpecialMove
	{
		get => (SpecialMoveStyle)(Target?.GetInt( "special_movement_states" ) ?? 0);
		set => Target?.Set( "special_movement_states", (int)value );
	}

	//Sitting
	public enum SittingStyle
	{
		None,
		Chair1,
		Chair2,
		Chair3,
		Floor1,
		Floor2,
		Floor3,
		Floor4
	}

	/// <summary>
	/// How are we sitting down?
	/// </summary>
	[Property, Feature( "Sitting", Icon = "chair" )]
	public SittingStyle Sitting
	{
		get => (SittingStyle)(Target?.GetInt( "sit" ) ?? 0);
		set => Target?.Set( "sit", (int)value );
	}

	/// <summary>
	/// How far up are we sitting down from the floor?
	/// </summary>
	[Property, Feature( "Sitting", Icon = "chair" )]
	public float SittingOffsetHeight
	{
		get => Target?.GetFloat( "sit_offset_height" ) ?? 0f;
		set => Target?.Set( "sit_offset_height", value );
	}
}