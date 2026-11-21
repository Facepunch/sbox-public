/// <summary>
/// Posing the model, a <see cref="SkinnedModelRenderer"/>. Every machine poses its own from wherever it has the body:
/// Wheel travel follows the host suspension state, interpolated between updates.
///
/// The bones, where {c} is fl, fr, rl or rr:
/// <list type="bullet">
/// <item><c>wheel_{c}</c> - the knuckle, where the arm meets it (at the front, on the steering axis). It moves up and
/// down with the suspension. The arm and shock are skinned from the body to this, so they follow.</item>
/// <item><c>wheel_{c}_steer</c> - a child of the knuckle where it is, that turns to steer: the front axle. Without
/// one, a front knuckle steers itself.</item>
/// <item><c>wheel_{c}_spin</c> - a child of those at the tyre's centre, which is where the suspension holds the
/// wheel. It rolls about whichever of its axes runs closest to the axle.</item>
/// <item><c>steering_wheel</c> - turns about <see cref="SteeringWheelAxis"/>.</item>
/// <item><c>pedal_gas</c>, <c>pedal_brake</c>, <c>pedal_clutch</c> - on their hinges, pushed forward
/// <see cref="PedalTravel"/> - from the driver's input on their machine, the synced throttle and brake on everyone
/// else's (see <see cref="UpdatePedals"/>).</item>
/// </list>
/// Any other bone rides on its parent.
/// </summary>
public sealed partial class FourWheelVehicle
{
	private static readonly string[] Corners = ["fl", "fr", "rl", "rr"];

	private static string WheelBone( string corner ) => $"wheel_{corner}";
	private static string SteerBone( string corner ) => $"wheel_{corner}_steer";
	private static string SpinBone( string corner ) => $"wheel_{corner}_spin";
	private const string SteeringWheelBone = "steering_wheel";
	private const string GasPedalBone = "pedal_gas";
	private const string BrakePedalBone = "pedal_brake";
	private const string ClutchPedalBone = "pedal_clutch";

	/// <summary>
	/// How far the pedals swing on their hinges when they're pushed all the way, degrees.
	/// </summary>
	[Property, Group( "Pedals" )] public float PedalTravel { get; set; } = 25f;

	/// <summary>
	/// How long the clutch is in for a gear change, seconds.
	/// </summary>
	[Property, Group( "Pedals" )] public float ClutchShiftTime { get; set; } = 0.3f;

	private SkinnedModelRenderer _renderer;
	private Model _rigModel;
	private Rig _rig;

	/// <summary>
	/// The rig the wheels were set up from.
	/// </summary>
	private Rig _wheelsRig;

	private readonly float[] _spin = new float[WheelCount];
	private readonly float[] _visualTravel = new float[WheelCount];
	private bool _visualTravelReady;

	/// <summary>
	/// How far each wheel centre sits below its hard point as posed last frame, inches, and how fast that's shrinking -
	/// the wheel being pushed up into the body - in/s. Where it is in the world, for sounds.
	/// </summary>
	private readonly float[] _travel = new float[WheelCount];
	private readonly float[] _compressionSpeed = new float[WheelCount];
	private readonly Vector3[] _wheelCenter = new Vector3[WheelCount];
	private Rig _travelRig;

	/// <summary>
	/// How fast a wheel is being pushed up into the body, in/s (negative while it drops away). From the posed wheels,
	/// so it's the same on every machine - a landing or a bump hits hard.
	/// </summary>
	public float SuspensionSpeed( int wheel ) => _compressionSpeed[wheel];

	/// <summary>
	/// Where a wheel's centre is in the world, as posed.
	/// </summary>
	public Vector3 WheelPosition( int wheel ) => _wheelCenter[wheel];

	private float _gasPedal, _brakePedal, _clutchPedal;
	private int _lastGear;
	private TimeSince _timeSinceShift = 10f;

	/// <summary>
	/// The model's rig, or null if there's no model or it doesn't have the wheel bones.
	/// </summary>
	private Rig GetRig()
	{
		if ( !_renderer.IsValid() )
			_renderer = GetComponent<SkinnedModelRenderer>();

		var model = _renderer?.Model;
		if ( model != _rigModel )
		{
			_rigModel = model;
			_rig = model is null || model.IsError ? null : Rig.Create( model );
		}

		return _rig;
	}

	/// <summary>
	/// Put the wheels where the ground is, steer and roll them, and turn the steering wheel.
	/// </summary>
	private void PoseRig()
	{
		var rig = GetRig();
		if ( rig is null )
		{
			_travelRig = null;
			Array.Clear( _compressionSpeed );
			return;
		}

		// a new rig, or the first synced suspension state snapping in, has nothing to compare against yet
		var hadTravel = _travelRig == rig && _visualTravelReady;
		_travelRig = rig;

		if ( !_renderer.CreateBoneObjects )
			_renderer.CreateBoneObjects = true;

		rig.Reset();

		var tx = WorldTransform;
		for ( int i = 0; i < WheelCount; i++ )
		{
			var axle = AxleOf( i );
			var hardPoint = rig.HardPoint( i );
			var steer = i < 2 ? Rotation.FromAxis( Vector3.Up, -SteerAngle ) : Rotation.Identity;

			// Pose from the same suspension state that produced the chassis forces.
			var targetTravel = HasSuspensionState ? SuspensionTravel[i] / VehicleScale : axle.SpringAdditionalLength;
			if ( !_visualTravelReady ) _visualTravel[i] = targetTravel;
			else _visualTravel[i] += (targetTravel - _visualTravel[i]) * (1f - MathF.Exp( -30f * Time.Delta ));
			var travel = _visualTravel[i];
			
			_compressionSpeed[i] = hadTravel && Time.Delta > 0 ? (_travel[i] - travel) / Time.Delta : 0;
			_travel[i] = travel;
			_wheelCenter[i] = tx.PointToWorld( hardPoint + Vector3.Down * travel );
			if ( rig.Steer[i] >= 0 )
			{
				rig.Move( rig.Wheel[i], Vector3.Down * travel, Rotation.Identity );
				rig.Move( rig.Steer[i], Vector3.Zero, steer );
			}
			else
			{
				rig.Move( rig.Wheel[i], Vector3.Down * travel, steer );
			}

			// roll, unless the wheels are locked
			if ( !WheelsLocked )
				_spin[i] = (_spin[i] + (Speed / (axle.WheelRadius * VehicleScale)).RadianToDegree() * Time.Delta) % 360f;

			rig.Move( rig.Spin[i], Vector3.Zero, Rotation.FromAxis( rig.Axle( i ), _spin[i] ) );
		}

		_visualTravelReady = HasSuspensionState;

		if ( rig.SteeringWheel >= 0 )
			rig.Move( rig.SteeringWheel, Vector3.Zero, Rotation.FromAxis( SteeringWheelAxis, -_steerPose * SteeringWheelAngle ) );

		UpdatePedals();
		PushPedal( rig, rig.GasPedal, _gasPedal );
		PushPedal( rig, rig.BrakePedal, _brakePedal );
		PushPedal( rig, rig.ClutchPedal, _clutchPedal );

		// parents first: moving a bone object carries its children
		foreach ( var bone in rig.Order )
		{
			var pose = rig.Pose( bone );
			var boneObject = _renderer.GetBoneObject( bone );
			if ( !boneObject.IsValid() ) continue;

			boneObject.Flags |= GameObjectFlags.ProceduralBone;
			boneObject.WorldTransform = tx.ToWorld( pose );
		}
	}

	/// <summary>
	/// The driver's feet on the pedals. On the driver's own machine it's straight from their input - gas and brake
	/// split the way the car splits it (the throttle brakes when it's against the way we're going), a gamepad's trigger
	/// part way. Everyone else goes from what the host syncs: the throttle and <see cref="BrakeInput"/>.
	/// The clutch goes in for a gear change, lifting off the gas, and is held in at a standstill until we pull away.
	/// </summary>
	private void UpdatePedals()
	{
		float gas = 0, brake = 0, clutch = 0;

		var driver = Seat?.GetOccupant()?.GetComponent<Player>();
		if ( driver.IsValid() )
		{
			if ( driver.IsLocalPlayer )
			{
				var forward = LocalAmount( Forward, driver );
				var back = LocalAmount( Back, driver );

				if ( forward > 0 )
				{
					if ( Speed <= -SpeedThrottleAsBrake ) brake = forward;
					else gas = forward;
				}
				else if ( back > 0 )
				{
					if ( Speed >= SpeedThrottleAsBrake ) brake = back;
					else gas = back;
				}
			}
			else
			{
				gas = ThrottleDown ? 1f : 0f;
				brake = BrakeInput;
			}

			if ( Gear != _lastGear )
			{
				_lastGear = Gear;
				_timeSinceShift = 0;
			}

			var shifting = _timeSinceShift < ClutchShiftTime ? 1f : 0f;
			var standstill = IsEngineOn ? 1f - (MathF.Abs( Speed ) / MphToIns( 5f )).Clamp( 0f, 1f ) : 0f;
			clutch = MathF.Max( shifting, standstill );
			gas *= 1f - shifting;
		}

		// feet take a moment
		var ease = 1f - MathF.Exp( -15f * Time.Delta );
		_gasPedal = _gasPedal.LerpTo( gas, ease );
		_brakePedal = _brakePedal.LerpTo( brake, ease );
		_clutchPedal = _clutchPedal.LerpTo( clutch, ease );
	}

	/// <summary>
	/// How far the local driver's pressing an input, 0 to 1 - a gamepad's trigger part way (<see cref="Player.ActionAmounts"/>).
	/// </summary>
	private static float LocalAmount( ClientInput input, Player driver )
	{
		if ( !input.IsEnabled || !input.LocalDown() ) return 0f;
		if ( input.HasKey ) return 1f;
		return driver.ActionAmounts.TryGetValue( input.Action, out var amount ) ? amount : 1f;
	}

	/// <summary>
	/// Swing a pedal forward on its hinge, which runs across the car.
	/// </summary>
	private void PushPedal( Rig rig, int bone, float amount )
	{
		if ( bone >= 0 )
			rig.Move( bone, Vector3.Zero, Rotation.FromAxis( Vector3.Left, amount * PedalTravel ) );
	}

	/// <summary>
	/// A model's bones, where they rest in body space, and their pose this frame.
	/// </summary>
	private sealed class Rig
	{
		public Model Model;
		public global::Transform[] Rest;
		public int[] Parent;

		/// <summary>
		/// Every bone, parents before their children.
		/// </summary>
		public int[] Order;

		public readonly int[] Wheel = new int[WheelCount];
		public readonly int[] Steer = new int[WheelCount];
		public readonly int[] Spin = new int[WheelCount];
		public int SteeringWheel;
		public int GasPedal, BrakePedal, ClutchPedal;

		/// <summary>
		/// How each bone has been moved this frame, in its own rest frame.
		/// </summary>
		private global::Transform[] _moved;
		private global::Transform[] _pose;

		public static Rig Create( Model model )
		{
			var bones = model.Bones.AllBones.OrderBy( b => b.Index ).ToArray();
			int Find( string name ) => model.Bones.HasBone( name ) ? model.Bones.GetBone( name ).Index : -1;

			var rig = new Rig
			{
				Model = model,
				Rest = [.. bones.Select( b => b.LocalTransform )],
				Parent = [.. bones.Select( b => b.Parent?.Index ?? -1 )],
				SteeringWheel = Find( SteeringWheelBone ),
				GasPedal = Find( GasPedalBone ),
				BrakePedal = Find( BrakePedalBone ),
				ClutchPedal = Find( ClutchPedalBone ),
				_moved = new global::Transform[bones.Length],
				_pose = new global::Transform[bones.Length],
			};

			for ( int i = 0; i < WheelCount; i++ )
			{
				rig.Wheel[i] = Find( WheelBone( Corners[i] ) );
				rig.Steer[i] = Find( SteerBone( Corners[i] ) );
				rig.Spin[i] = Find( SpinBone( Corners[i] ) );
				if ( rig.Wheel[i] < 0 || rig.Spin[i] < 0 )
					return null;
			}

			int Depth( int b ) => rig.Parent[b] < 0 ? 0 : 1 + Depth( rig.Parent[b] );
			rig.Order = [.. Enumerable.Range( 0, bones.Length ).OrderBy( Depth )];

			return rig;
		}

		/// <summary>
		/// Where the suspension holds the wheel: the tyre's centre at rest.
		/// </summary>
		public Vector3 HardPoint( int wheel ) => Rest[Spin[wheel]].Position;

		/// <summary>
		/// The axle a wheel rolls about at rest, pointing left: the spin bone's axis closest to the car's left, so it
		/// doesn't matter which way the bone was built.
		/// </summary>
		public Vector3 Axle( int wheel )
		{
			var rotation = Rest[Spin[wheel]].Rotation;
			var axle = new[] { rotation.Forward, rotation.Left, rotation.Up }.MaxBy( a => MathF.Abs( a.y ) );
			return axle.y < 0 ? -axle : axle;
		}

		public void Reset() => Array.Fill( _moved, global::Transform.Zero );

		/// <summary>
		/// Move a bone by an offset and turn it about itself, both in body space as it sits at rest. Its children go
		/// with it.
		/// </summary>
		public void Move( int bone, Vector3 offset, Rotation rotation )
		{
			var rest = Rest[bone].Rotation;
			_moved[bone] = new global::Transform( rest.Inverse * offset, rest.Inverse * rotation * rest );
		}

		/// <summary>
		/// A bone's pose in body space this frame: riding on its parent as it did at rest, then moved. Call it in
		/// <see cref="Order"/>, so the parent's is ready.
		/// </summary>
		public global::Transform Pose( int bone )
		{
			var parent = Parent[bone];
			var follow = parent < 0 ? Rest[bone] : _pose[parent].ToWorld( Rest[parent].ToLocal( Rest[bone] ) );
			return _pose[bone] = follow.ToWorld( _moved[bone] );
		}
	}
}
