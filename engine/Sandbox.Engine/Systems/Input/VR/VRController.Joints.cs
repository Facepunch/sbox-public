using Facepunch.XR;

namespace Sandbox.VR;

partial record VRController
{
	class JointDataContainer
	{
		public InputPoseHandState Pose;

		private readonly VRHandJointData[] _jointData = new VRHandJointData[Enum.GetValues<VRHandJoint>().Length];

		internal Transform GetBoneTransform( VRHandJoint joint )
		{
			var index = (int)joint;
			return Pose[index].pose.GetTransform();
		}

		public VRHandJointData[] GetJoints()
		{
			UpdateJointData();
			return _jointData;
		}

		private void UpdateJointData()
		{
			for ( int i = 0; i < _jointData.Length; i++ )
			{
				ref var jointData = ref _jointData[i];
				var joint = (VRHandJoint)i;

				jointData.Joint = joint;
				jointData.Transform = GetBoneTransform( joint );
			}
		}
	}

	private JointDataContainer _handJoints = new();
	private JointDataContainer _conformingJoints = new();

	internal Transform GetBoneTransform( VRHandJoint joint, MotionRange motionRange )
	{
		if ( motionRange == MotionRange.Hand )
			return _handJoints.GetBoneTransform( joint );
		else if ( motionRange == MotionRange.Controller )
			return _conformingJoints.GetBoneTransform( joint );

		return Transform.Zero;
	}

	/// <summary>
	/// Returns joint data for a specific motion range.
	/// </summary>
	/// <param name="motionRange">Whether the joints returned represent a raw hand pose, or one that represents the hand wrapping around the controller.</param>
	public VRHandJointData[] GetJoints( MotionRange motionRange = MotionRange.Hand )
	{
		if ( motionRange == MotionRange.Hand )
			return _handJoints.GetJoints();
		else if ( motionRange == MotionRange.Controller )
			return _conformingJoints.GetJoints();

		return Array.Empty<VRHandJointData>();
	}

	/// <summary>
	/// Get the skeletal value (from 0 to 1) of a specified <see cref="FingerValue"/> - includes curl and splay.
	/// </summary>
	public float GetFingerValue( FingerValue value )
	{
		bool isCurl = value == FingerValue.ThumbCurl || value == FingerValue.IndexCurl || value == FingerValue.MiddleCurl || value == FingerValue.RingCurl || value == FingerValue.PinkyCurl;

		if ( isCurl )
		{
			return VRSystem.GetFingerCurl( _trackedDevice.InputSource, value );
		}

		return 0;
	}

	/// <summary>
	/// Get the skeletal value (from 0 to 1) of a specified finger curl index.
	/// </summary>
	public float GetFingerCurl( int index )
	{
		if ( index < 0 ) throw new ArgumentOutOfRangeException( "Should be 0-4", nameof( index ) );
		if ( index > 4 ) throw new ArgumentOutOfRangeException( "Should be 0-4", nameof( index ) );
		return GetFingerValue( (FingerValue)index );
	}

	/// <summary>
	/// Get the skeletal value (from 0 to 1) of a specified finger splay index.
	/// </summary>
	public float GetFingerSplay( int index )
	{
		if ( index < 0 ) throw new ArgumentOutOfRangeException( "Should be 0-3", nameof( index ) );
		if ( index > 3 ) throw new ArgumentOutOfRangeException( "Should be 0-3", nameof( index ) );
		return GetFingerValue( (FingerValue.ThumbIndexSplay + index) );
	}

	private readonly List<VRHandJointData> _jointList = new();

	[Obsolete( "Please use GetJoints()" )]
	public List<VRHandJointData> GetJointData()
	{
		_jointList.Clear();
		_jointList.AddRange( GetJoints( MotionRange.Hand ) );
		return _jointList;
	}
}
