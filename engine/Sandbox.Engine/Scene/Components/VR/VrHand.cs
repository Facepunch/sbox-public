namespace Sandbox.VR;

/// <summary>
/// Updates the parameters on an <see cref="SkinnedModelRenderer"/> on this GameObject based on the skeletal data from SteamVR.
/// Useful for quick hand posing based on controller input.
/// </summary>
[Title( "VR Hand" )]
[Category( "VR" )]
[EditorHandle( "materials/gizmo/hand.png" )]
[Icon( "waving_hand" )]
public class VRHand : Component
{
	/// <summary>
	/// Which <see cref="SkinnedModelRenderer"/> to use when updating this component
	/// </summary>
	[RequireComponent]
	public SkinnedModelRenderer SkinnedModelComponent { get; set; }

	/// <summary>
	/// Represents a controller to use when fetching skeletal data (finger curl/splay values)
	/// </summary>
	public enum HandSources
	{
		/// <summary>
		/// The left controller
		/// </summary>
		Left,

		/// <summary>
		/// The right controller
		/// </summary>
		Right
	}

	/// <summary>
	/// Which hand should we use to update the parameters?
	/// </summary>
	[Property]
	public HandSources HandSource { get; set; } = HandSources.Left;

	/// <summary>
	/// What motion range should we use to update the parameters?
	/// </summary>
	[Property]
	public MotionRange MotionRange { get; set; } = MotionRange.Hand;

	private static readonly Dictionary<VRHandJoint, string> JointNameMap = new Dictionary<VRHandJoint, string>
	{
		{ VRHandJoint.Wrist, "hand_{0}" },

		{ VRHandJoint.ThumbMetacarpal, "finger_thumb_0_{0}" },
		{ VRHandJoint.ThumbProximal, "finger_thumb_1_{0}" },
		{ VRHandJoint.ThumbDistal, "finger_thumb_2_{0}" },

		{ VRHandJoint.IndexMetacarpal, "finger_index_meta_{0}" },
		{ VRHandJoint.IndexProximal, "finger_index_0_{0}" },
		{ VRHandJoint.IndexIntermediate, "finger_index_1_{0}" },
		{ VRHandJoint.IndexDistal, "finger_index_2_{0}" },

		{ VRHandJoint.MiddleMetacarpal, "finger_middle_meta_{0}" },
		{ VRHandJoint.MiddleProximal, "finger_middle_0_{0}" },
		{ VRHandJoint.MiddleIntermediate, "finger_middle_1_{0}" },
		{ VRHandJoint.MiddleDistal, "finger_middle_2_{0}" },

		{ VRHandJoint.RingMetacarpal, "finger_ring_meta_{0}" },
		{ VRHandJoint.RingProximal, "finger_ring_0_{0}" },
		{ VRHandJoint.RingIntermediate, "finger_ring_1_{0}" },
		{ VRHandJoint.RingDistal, "finger_ring_2_{0}" },

		{ VRHandJoint.LittleMetacarpal, "finger_pinky_meta_{0}" },
		{ VRHandJoint.LittleProximal, "finger_pinky_0_{0}" },
		{ VRHandJoint.LittleIntermediate, "finger_pinky_1_{0}" },
		{ VRHandJoint.LittleDistal, "finger_pinky_2_{0}" },
	};

	private static string GetBoneName( VRHandJoint joint, HandSources hand )
	{
		if ( JointNameMap.TryGetValue( joint, out string jointNameFormat ) )
		{
			string handSide = hand == HandSources.Left ? "L" : "R";
			return string.Format( jointNameFormat, handSide );
		}
		return "Unknown";
	}

	private void UpdatePose()
	{
		if ( SkinnedModelComponent?.Model is null )
			return;

		var source = (HandSource == HandSources.Left) ? Input.VR.LeftHand : Input.VR.RightHand;
		var offset = (HandSource == HandSources.Left) ? new Transform( Vector3.Zero, Rotation.From( 0, 0, 180 ) ) : new Transform( Vector3.Zero, Rotation.From( 0, 0, 0 ) );

		foreach ( var entry in source.GetJoints( MotionRange ) )
		{
			var boneName = GetBoneName( entry.Joint, HandSource );
			if ( string.IsNullOrEmpty( boneName ) )
				continue;

			var bone = SkinnedModelComponent.Model.Bones.AllBones.FirstOrDefault( x => x.Name.Equals( boneName, StringComparison.OrdinalIgnoreCase ) );

			if ( bone is null )
				continue;

			// Joint transforms are in anchor-local space, convert to world space first
			var worldTransform = Input.VR.Anchor.ToWorld( entry.Transform );
			var tx = GameObject.WorldTransform.ToLocal( worldTransform );
			tx = tx.WithPosition( tx.Position + offset.Position );
			tx = tx.WithRotation( tx.Rotation * offset.Rotation );

			SkinnedModelComponent.SceneModel?.SetBoneOverride( bone.Index, tx );
		}
	}

	protected override void OnUpdate()
	{
		if ( !Enabled || Scene.IsEditor || !Game.IsRunningInVR )
			return;

		if ( IsProxy )
			return;

		UpdatePose();
	}

	protected override void OnPreRender()
	{
		if ( !Enabled || Scene.IsEditor || !Game.IsRunningInVR )
			return;

		if ( IsProxy )
			return;

		UpdatePose();
	}
}
