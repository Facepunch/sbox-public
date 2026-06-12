using Sandbox;

/// <summary>
/// Controls the camera during crate unboxing: slowly tightens in as flaps open,
/// then pulls back and tilts up once fully open to make room for the reward cards.
/// Attach to the same GameObject as the camera or reference the camera via property.
/// </summary>
public sealed class RewardUnboxCamera : Component
{
	/// <summary>
	/// The RewardUnboxCrate to watch for opening progress.
	/// </summary>
	[Property] public RewardUnboxCrate Crate { get; set; }

	/// <summary>
	/// How much closer the camera moves toward the crate as flaps open (per flap).
	/// </summary>
	[Property] public float ZoomPerFlap { get; set; } = 8f;

	/// <summary>
	/// How far back the camera pulls once the crate is fully open.
	/// </summary>
	[Property] public float PullBackDistance { get; set; } = 30f;

	/// <summary>
	/// How much the camera tilts up (degrees) once fully open.
	/// </summary>
	[Property] public float TiltUpDegrees { get; set; } = 10f;

	/// <summary>
	/// Speed of camera movement (lerp rate per second).
	/// </summary>
	[Property] public float MoveSpeed { get; set; } = 3f;

	private Vector3 _initialPosition;
	private Rotation _initialRotation;

	protected override void OnStart()
	{
		_initialPosition = WorldPosition;
		_initialRotation = WorldRotation;
	}

	protected override void OnUpdate()
	{
		if ( Crate == null ) return;

		float dt = Time.Delta;
		int openFlaps = GetOpenFlapCount();
		bool fullyOpen = Crate.IsFullyOpen;

		Vector3 targetPos;
		Rotation targetRot;

		if ( fullyOpen )
		{
			// Pull back and tilt up to show the cards
			var backDir = _initialRotation.Backward;
			var upDir = _initialRotation.Up;

			targetPos = _initialPosition + backDir * PullBackDistance + upDir * (PullBackDistance * 0.3f);
			targetRot = _initialRotation * Rotation.FromPitch( -TiltUpDegrees );
		}
		else
		{
			// Tighten in toward the crate as flaps open
			var forwardDir = _initialRotation.Forward;
			float zoomAmount = openFlaps * ZoomPerFlap;

			targetPos = _initialPosition + forwardDir * zoomAmount;
			targetRot = _initialRotation;
		}

		// Smooth lerp toward target
		float lerpT = 1f - MathF.Exp( -MoveSpeed * dt );
		WorldPosition = Vector3.Lerp( WorldPosition, targetPos, lerpT );
		WorldRotation = Rotation.Slerp( WorldRotation, targetRot, lerpT );
	}

	private int GetOpenFlapCount()
	{
		if ( Crate == null || Crate.Flaps == null ) return 0;

		// IsFullyOpen means all flaps are open, otherwise count based on internal state
		// We can infer from the public IsFullyOpen and flap count
		if ( Crate.IsFullyOpen ) return Crate.Flaps.Count;

		// Use reflection-free approach: expose flap progress from RewardUnboxCrate
		return Crate.OpenFlapCount;
	}
}
