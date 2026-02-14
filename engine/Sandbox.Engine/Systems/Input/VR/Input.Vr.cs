namespace Sandbox.VR;

public class VRInput
{
	/// <summary>
	/// The current relevant <see cref="VRInput"/> instance.
	/// </summary>
	public static VRInput Current { get; internal set; }

	private float _scale = 1.0f;

	/// <summary>
	/// Get or set the player's scale in the world. If you set it to 2 the player will be twice as big.
	/// </summary>
	public float Scale
	{
		get => _scale;
		set
		{
			if ( value == _scale ) return;
			if ( value == 0 ) return;

			_scale = value;
			VRSystem.WorldScale = 1.0f / value;
		}
	}

	/// <summary>
	/// Gets or sets where the center of the VR play area is in world space.
	/// </summary>
	public Transform Anchor { get; set; } = Transform.Zero;

	/// <summary>
	/// Returns true if SteamVR is drawing the controllers
	/// </summary>
	[Obsolete]
	public bool ControllersAreDrawing => false;

	/// <summary>
	/// Returns true if the left hand is dominant
	/// </summary>
	[Obsolete]
	public bool IsLeftHandDominant => false;

	internal Transform _head = Transform.Zero;

	/// <summary>
	/// Position and rotation of the Head Mounted Display in local space coordinates.
	/// </summary>
	public Transform Head => Input.VR.Anchor.ToWorld( _head );

	/// <summary>
	/// Information about the left hand input.
	/// </summary>
	public VRController LeftHand { get; internal set; }

	/// <summary>
	/// Information about the right hand input.
	/// </summary>
	public VRController RightHand { get; internal set; }

	internal List<TrackedObject> _objectList;

	/// <summary>
	/// A list of available trackers.
	/// </summary>
	public IReadOnlyList<TrackedObject> TrackedObjects => _objectList;

	internal VRInput( TrackedDevice leftHand, TrackedDevice rightHand )
	{
		LeftHand = new( leftHand );
		RightHand = new( rightHand );
	}

	internal void Update()
	{
		//
		// Update controllers
		//
		LeftHand.Update();
		RightHand.Update();

		//
		// Update HMD
		//
		_head = VRSystem.GetHeadTransform();

		_objectList = new() { LeftHand, RightHand };
	}
}
