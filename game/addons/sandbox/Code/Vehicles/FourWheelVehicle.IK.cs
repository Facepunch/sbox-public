/// <summary>
/// The driver's hands on the steering wheel, by IK. Put the hand targets under the <c>steering_wheel</c> bone's object
/// so they turn with it (<see cref="PoseRig"/> poses it before we read them), each oriented the way the hand should
/// grip. While the horn's held (<see cref="VehicleSounds.HornDown"/>) the right hand moves off the wheel onto
/// <see cref="HornHand"/> and opens flat.
///
/// The seated pose sits too far back to reach a turned wheel, and the body's aim only bends the neck, so the whole body
/// model tilts forward about the hips (<see cref="DriverTilt"/>) before it's animated - the IK then solves from there.
/// Foot targets (under the pedal bones, say) keep the legs from swinging down through the floor with it.
///
/// Runs on everyone, from the synced steering and horn, on whoever's in <see cref="Seat"/>.
/// </summary>
public sealed partial class FourWheelVehicle
{
	[Property, Group( "Driver Hands" )] public GameObject LeftHand { get; set; }
	[Property, Group( "Driver Hands" )] public GameObject RightHand { get; set; }

	/// <summary>
	/// Where the right hand goes to press the horn.
	/// </summary>
	[Property, Group( "Driver Hands" )] public GameObject HornHand { get; set; }

	/// <summary>
	/// How long the right hand takes to move between the wheel and the horn, seconds.
	/// </summary>
	[Property, Group( "Driver Hands" )] public float HornHandTime { get; set; } = 0.1f;

	/// <summary>
	/// The animgraph hold type that shapes the right hand on the horn (<c>holdtype</c>), and its hand pose
	/// (<c>holdtype_pose_hand</c>) - tune these for the open palm. Empty leaves the hand as it is.
	/// </summary>
	[Property, Group( "Driver Hands" )] public string HornHoldType { get; set; } = "holditem";
	[Property, Group( "Driver Hands" )] public float HornHandPose { get; set; } = 1f;

	/// <summary>
	/// Foot targets, where each foot rests on its pedal (at the ankle, the way IK places it). Under the pedal's bone or
	/// anywhere else - riding on the pedal is undone, the foot works it itself (<see cref="PedalFoot"/>). The left foot
	/// only goes to the clutch while it's in, and is left to the animation otherwise. The right foot rests on the gas
	/// and moves across to <see cref="BrakeFoot"/> while braking, staying on whichever it last pressed.
	/// </summary>
	[Property, Group( "Driver Feet" ), Title( "Clutch Foot" )] public GameObject LeftFoot { get; set; }
	[Property, Group( "Driver Feet" ), Title( "Gas Foot" )] public GameObject RightFoot { get; set; }
	[Property, Group( "Driver Feet" )] public GameObject BrakeFoot { get; set; }

	/// <summary>
	/// Pressing a pedal, the foot pivots about its heel - the heel stays on the floor and the toes tip down this many
	/// degrees with the pedal all the way in.
	/// </summary>
	[Property, Group( "Driver Feet" ), Range( 0, 60 )] public float FootPressAngle { get; set; } = 20f;

	/// <summary>
	/// Where the heel is from a foot target, inches, along the car (forward, left, up) - what the foot pivots about.
	/// </summary>
	[Property, Group( "Driver Feet" )] public Vector3 FootHeelOffset { get; set; } = new( -2f, 0f, -3f );

	/// <summary>
	/// How far in the clutch has to be before the left foot goes to it, 0 to 1.
	/// </summary>
	[Property, Group( "Driver Feet" ), Range( 0, 1 )] public float ClutchFootThreshold { get; set; } = 0.1f;

	/// <summary>
	/// How long the right foot takes to move between the gas and the brake, seconds, and how far it lifts on the way
	/// over, inches.
	/// </summary>
	[Property, Group( "Driver Feet" )] public float FootSwitchTime { get; set; } = 0.15f;
	[Property, Group( "Driver Feet" )] public float FootLift { get; set; } = 2.5f;

	/// <summary>
	/// How far the driver's whole body tilts forward towards the wheel, degrees, about <see cref="TiltPivot"/>.
	/// </summary>
	[Property, Group( "Driver Hands" ), Range( 0, 45 )] public float DriverTilt { get; set; } = 15f;

	/// <summary>
	/// Most the driver sways to the side in a turn, degrees, thrown to the outside of it.
	/// </summary>
	[Property, Group( "Driver Sway" ), Range( 0, 30 )] public float MaxSway { get; set; } = 10f;

	/// <summary>
	/// Cornering (sideways acceleration, in/s²) that sways the driver all the way. The tuning's gravity is 600.
	/// </summary>
	[Property, Group( "Driver Sway" )] public float SwayFullAcceleration { get; set; } = 600f;

	/// <summary>
	/// The sway's a spring: how stiffly it follows the cornering, and how quickly it stops wobbling.
	/// </summary>
	[Property, Group( "Driver Sway" )] public float SwayStiffness { get; set; } = 60f;
	[Property, Group( "Driver Sway" )] public float SwayDamping { get; set; } = 7f;

	/// <summary>
	/// What the body tilts about. Empty, it's the driver's pelvis, so they stay sat on the seat.
	/// </summary>
	[Property, Group( "Driver Hands" )] public GameObject TiltPivot { get; set; }

	private const string LeftHandIk = "hand_left";
	private const string RightHandIk = "hand_right";
	private const string LeftFootIk = "foot_left";
	private const string RightFootIk = "foot_right";

	private Player _driver;
	private SkinnedModelRenderer _handsRenderer;
	private VehicleSounds _sounds;
	private float _hornHand;
	private bool _palmOpen;

	private float _footOnBrake;

	private float _sway;
	private float _swayVelocity;
	private float _lastYaw;
	private bool _hasLastYaw;

	// The lean last applied to the driver's body, so the first person camera can lean with it.
	private Vector3 _leanPivot;
	private Rotation _lean = Rotation.Identity;
	private bool _hasLean;

	/// <summary>
	/// Carry a first person eye position along with the driver's lean. The seat's eye is placed for an upright body,
	/// so without this the body tilts forward out from under the camera and it ends up inside the chest.
	/// </summary>
	public Vector3 LeanEyePosition( Player player, Vector3 eye )
	{
		if ( !_hasLean || player != _driver ) return eye;
		return _leanPivot + _lean * (eye - _leanPivot);
	}

	protected override void OnDisabled() => ReleaseHands();
	protected override void OnDestroy() => ReleaseHands();

	/// <summary>
	/// After <see cref="PoseRig"/>, so the targets on the steering wheel are where it is this frame.
	/// </summary>
	private void UpdateDriverHands()
	{
		var driver = Seat?.GetOccupant()?.GetComponent<Player>();
		var renderer = driver?.Controller?.Renderer;
		if ( !renderer.IsValid() || (!LeftHand.IsValid() && !RightHand.IsValid()) )
		{
			ReleaseHands();
			return;
		}

		// someone else got in
		if ( renderer != _handsRenderer )
		{
			ReleaseHands();
			_driver = driver;
			_handsRenderer = renderer;
		}

		if ( !_sounds.IsValid() )
			_sounds = GetComponent<VehicleSounds>();

		var horn = HornHand.IsValid() && (_sounds?.HornDown ?? false);
		_hornHand = _hornHand.Approach( horn ? 1 : 0, Time.Delta / MathF.Max( HornHandTime, 0.001f ) );

		// the palm opens as the hand gets most of the way to the horn, and closes again as it leaves
		_palmOpen = _hornHand > 0.5f;

		UpdateSway();

		// the right foot goes to whichever pedal's being pressed, and stays on the last one when neither is
		var footTarget = _brakePedal > 0.05f ? 1f : _gasPedal > 0.05f ? 0f : (_footOnBrake > 0.5f ? 1f : 0f);
		if ( !BrakeFoot.IsValid() ) footTarget = 0;
		_footOnBrake = _footOnBrake.Approach( footTarget, Time.Delta / MathF.Max( FootSwitchTime, 0.001f ) );
	}

	/// <summary>
	/// The hand and foot IK targets. <see cref="SkinnedModelRenderer.SetIk"/> takes them relative to the body as it is
	/// when it's called, so this has to come after the tilt or the targets get carried forward with it.
	/// </summary>
	private void SetDriverIk( SkinnedModelRenderer renderer )
	{
		SetIk( renderer, LeftHandIk, LeftHand );
		SetLeftFootIk( renderer );
		SetRightFootIk( renderer );

		var right = RightHand.IsValid() ? RightHand.WorldTransform : (global::Transform?)null;
		if ( HornHand.IsValid() && _hornHand > 0 )
		{
			var to = HornHand.WorldTransform;
			var from = right ?? to;
			var t = _hornHand * _hornHand * (3 - 2 * _hornHand); // ease in and out
			right = new global::Transform( Vector3.Lerp( from.Position, to.Position, t ), Rotation.Slerp( from.Rotation, to.Rotation, t ) );
		}

		if ( right is { } rightTx )
			SetIkTarget( renderer, RightHandIk, rightTx );
		else
			ClearIkTarget( renderer, RightHandIk );
	}

	private void UpdateSway()
	{
		var dt = MathF.Min( Time.Delta, 0.05f );
		var yaw = WorldRotation.Angles().yaw;

		var yawRate = 0f;
		if ( _hasLastYaw && dt > 0 )
			yawRate = MathX.DeltaDegrees( _lastYaw, yaw ).DegreeToRadian() / dt;

		_lastYaw = yaw;
		_hasLastYaw = true;

		var lateral = Speed * yawRate;
		var target = MaxSway * (lateral / MathF.Max( SwayFullAcceleration, 1f )).Clamp( -1f, 1f );

		_swayVelocity += ((target - _sway) * SwayStiffness - _swayVelocity * SwayDamping) * dt;
		_sway = (_sway + _swayVelocity * dt).Clamp( -MaxSway * 1.5f, MaxSway * 1.5f );
	}

	/// <summary>
	/// The right foot, on the gas or the brake, or part way between them lifted off the floor in an arc.
	/// </summary>
	private void SetRightFootIk( SkinnedModelRenderer renderer )
	{
		var rig = GetRig();
		var gas = RightFoot.IsValid() ? PedalFoot( RightFoot, rig?.GasPedal ?? -1, _gasPedal ) : (global::Transform?)null;
		var brake = BrakeFoot.IsValid() ? PedalFoot( BrakeFoot, rig?.BrakePedal ?? -1, _brakePedal ) : (global::Transform?)null;

		if ( gas is null && brake is null )
		{
			ClearIkTarget( renderer, RightFootIk );
			return;
		}

		var from = gas ?? brake.Value;
		var to = brake ?? from;
		var t = _footOnBrake * _footOnBrake * (3 - 2 * _footOnBrake); // ease in and out
		var lift = WorldRotation.Up * (MathF.Sin( t * MathF.PI ) * FootLift * VehicleScale);

		SetIkTarget( renderer, RightFootIk, new global::Transform(
			Vector3.Lerp( from.Position, to.Position, t ) + lift,
			Rotation.Slerp( from.Rotation, to.Rotation, t ) ) );
	}

	/// <summary>
	/// The left foot works the clutch while it's in, and otherwise sits however the animation has it.
	/// </summary>
	private void SetLeftFootIk( SkinnedModelRenderer renderer )
	{
		if ( !LeftFoot.IsValid() || _clutchPedal < ClutchFootThreshold )
		{
			ClearIkTarget( renderer, LeftFootIk );
			return;
		}

		SetIkTarget( renderer, LeftFootIk, PedalFoot( LeftFoot, GetRig()?.ClutchPedal ?? -1, _clutchPedal ) );
	}

	/// <summary>
	/// A foot working a pedal pushed in by <paramref name="amount"/> (0 to 1). From where the target has it resting -
	/// first undoing the pedal's swing if the target rides on the pedal (<see cref="PushPedal"/> swings it about its
	/// bone, across the car) - the foot pivots about its heel, so the heel stays put and the toes tip down onto the
	/// pedal.
	/// </summary>
	private global::Transform PedalFoot( GameObject target, int pedalBone, float amount )
	{
		var foot = target.WorldTransform;
		var hinge = WorldRotation.Left;

		var pedal = pedalBone >= 0 && _renderer.IsValid() ? _renderer.GetBoneObject( pedalBone ) : null;
		if ( pedal.IsValid() && IsUnder( target, pedal ) )
			foot = RotateAbout( foot, pedal.WorldPosition, Rotation.FromAxis( hinge, -amount * PedalTravel ) );

		// positive about the car's left tips the toes down
		var heel = foot.Position + WorldRotation * (FootHeelOffset * VehicleScale);
		return RotateAbout( foot, heel, Rotation.FromAxis( hinge, amount * FootPressAngle ) );
	}

	private static global::Transform RotateAbout( global::Transform tx, Vector3 pivot, Rotation rotation )
	{
		return new global::Transform( pivot + rotation * (tx.Position - pivot), rotation * tx.Rotation, tx.Scale );
	}

	private static bool IsUnder( GameObject child, GameObject parent )
	{
		for ( var go = child.Parent; go.IsValid(); go = go.Parent )
		{
			if ( go == parent ) return true;
		}

		return false;
	}

	private static void SetIk( SkinnedModelRenderer renderer, string ik, GameObject target )
	{
		if ( target.IsValid() )
			SetIkTarget( renderer, ik, target.WorldTransform );
		else
			ClearIkTarget( renderer, ik );
	}

	private static void SetIkTarget( SkinnedModelRenderer renderer, string ik, global::Transform world )
	{
		if ( renderer.Animation.Graph is null )
		{
			renderer.SetIk( ik, world );
			return;
		}

		renderer.Animation.Parameters.TrySet( $"ik.{ik}", renderer.WorldTransform.ToLocal( world ) );
		renderer.Animation.Parameters.TrySet( $"ik.{ik}.enabled", true );
	}

	private static void ClearIkTarget( SkinnedModelRenderer renderer, string ik )
	{
		if ( renderer.Animation.Graph is null )
			renderer.ClearIk( ik );
		else
			renderer.Animation.Parameters.TrySet( $"ik.{ik}.enabled", false );
	}

	internal void ApplyDriverPose()
	{
		if ( !_handsRenderer.IsValid() || !_driver.IsValid() ) return;

		var body = _handsRenderer.GameObject;

		// the renderer has to be on its own object under the player, or we'd be moving the player
		if ( body != _driver.GameObject )
		{
			var root = _driver.WorldTransform;
			var pivot = TiltPivot.IsValid() ? TiltPivot.WorldPosition : PelvisPivot( body, root );
			// lean forward over the wheel, and sway to the side (rolling about the car's length: positive leans right)
			var tilt = Rotation.FromAxis( WorldRotation.Forward, _sway ) * Rotation.FromAxis( WorldRotation.Left, DriverTilt );

			body.WorldTransform = new global::Transform( pivot + tilt * (root.Position - pivot), tilt * root.Rotation, body.WorldScale );

			_leanPivot = pivot;
			_lean = tilt;
			_hasLean = true;
		}
		else
		{
			_hasLean = false;
		}

		SetDriverIk( _handsRenderer );
		ApplyHornHandPose( _handsRenderer );
	}

	/// <summary>
	/// Where the driver's pelvis is with the body upright - so it stays on the seat while the body tilts over it.
	/// </summary>
	private Vector3 PelvisPivot( GameObject body, global::Transform root )
	{
		var model = _handsRenderer.Model;
		if ( model is null || !model.Bones.HasBone( PelvisBone ) ) return root.Position;

		if ( !_handsRenderer.CreateBoneObjects )
			_handsRenderer.CreateBoneObjects = true;

		var pelvis = _handsRenderer.GetBoneObject( model.Bones.GetBone( PelvisBone ).Index );
		if ( !pelvis.IsValid() ) return root.Position;

		var inBody = body.WorldTransform.PointToLocal( pelvis.WorldPosition );
		return root.PointToWorld( inBody );
	}

	private const string PelvisBone = "pelvis";

	/// <summary>
	/// Shape the right hand for the horn with the animgraph's hold type, right hand only - the arm's on IK, so only the
	/// fingers take it. <see cref="Player"/> puts the hold type back to none every update while there's no weapon out,
	/// so this is set from the late pose step each frame it's wanted and simply stops when it isn't.
	/// </summary>
	private void ApplyHornHandPose( SkinnedModelRenderer renderer )
	{
		if ( !_palmOpen || string.IsNullOrEmpty( HornHoldType ) ) return;

		if ( renderer.Animation.Graph is null )
		{
			renderer.Set( "holdtype", HornHoldType );
			renderer.Set( "holdtype_handedness", 1 );
			renderer.Set( "holdtype_pose_hand", HornHandPose );
		}
		else
		{
			renderer.Animation.Parameters.TrySet( "holdtype", HornHoldType );
			renderer.Animation.Parameters.TrySet( "holdtype_handedness", 1 );
			renderer.Animation.Parameters.TrySet( "holdtype_pose_hand", HornHandPose );
		}
	}

	/// <summary>
	/// Give the last driver their hands, feet, fingers and posture back.
	/// </summary>
	private void ReleaseHands()
	{
		if ( _handsRenderer.IsValid() )
		{
			ClearIkTarget( _handsRenderer, LeftHandIk );
			ClearIkTarget( _handsRenderer, RightHandIk );
			ClearIkTarget( _handsRenderer, LeftFootIk );
			ClearIkTarget( _handsRenderer, RightFootIk );

			// back upright on the player (the body sits at its origin in the prefab)
			if ( _driver.IsValid() && _handsRenderer.GameObject != _driver.GameObject )
			{
				_handsRenderer.GameObject.LocalPosition = Vector3.Zero;
				_handsRenderer.GameObject.LocalRotation = Rotation.Identity;
			}
		}

		_driver = null;
		_handsRenderer = null;
		_hornHand = 0;
		_palmOpen = false;
		_footOnBrake = 0;
		_sway = 0;
		_swayVelocity = 0;
		_hasLastYaw = false;
		_hasLean = false;
	}
}

/// <summary>
/// Tilts drivers forward over their wheels and sets their IK (<see cref="FourWheelVehicle.ApplyDriverPose"/>) once every component has
/// updated - the player controller places the body in its update, so ours has to land after it - and before the bones
/// are animated, so the IK solves from the tilted body.
/// </summary>
public sealed class VehicleDriverPoseSystem : GameObjectSystem<VehicleDriverPoseSystem>
{
	private readonly List<FourWheelVehicle> _vehicles = new();

	public VehicleDriverPoseSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, -1000, PoseDrivers, "PoseVehicleDrivers" );
	}

	private void PoseDrivers()
	{
		_vehicles.Clear();
		Scene.GetAll( _vehicles );

		foreach ( var vehicle in _vehicles )
			vehicle.ApplyDriverPose();
	}
}
