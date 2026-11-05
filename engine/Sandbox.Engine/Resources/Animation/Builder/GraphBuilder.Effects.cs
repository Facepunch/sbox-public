namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	/// <summary>
	/// How two-bone / foot IK blends in and out.
	/// </summary>
	[Expose]
	public enum IKBlendMode
	{
		/// <summary>Linearly blend the effector target position before solving (cheaper).</summary>
		Effector = 0,
		/// <summary>Blend the pre-IK and post-IK poses (more expensive).</summary>
		Pose = 1,
	}

	/// <summary>
	/// How a warp samples the root motion it produces.
	/// </summary>
	[Expose]
	public enum RootMotionSamplingMode
	{
		/// <summary>Sample the warped root motion as a per-frame delta.</summary>
		Delta = 0,
		/// <summary>Match the world-space position of the warped root motion.</summary>
		WorldSpace = 1,
	}

	[Expose]
	public enum OrientationWarpAlignmentMode
	{
		MovementDirection = 0,
		CharacterForward,
	}

	public Node AimAt( Node child, Node worldTarget )
	{
		RequirePose( child );
		Require( worldTarget, NodeKind.Vector );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphAimIKNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_targetNodeIdx = (short)Resolve( worldTarget );
			return definition;
		} );
	}

	public readonly record struct LookAtBone( string Bone, float Weight );
	public sealed class LookAtSettings
	{
		public string ReferenceBone { get; init; } = "";
		public Rotation ReferenceRotation { get; init; } = Rotation.Identity;
		public IReadOnlyList<LookAtBone> Bones { get; init; } = [];
		public Node Weight { get; init; }
		public float YawLimit { get; init; } = 140;
		public float PitchLimit { get; init; } = 140;
	}

	public Node LookAt( Node child, Node pitch, Node yaw, LookAtSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		ArgumentNullException.ThrowIfNull( settings.Bones );
		RequirePose( child );
		Require( pitch, NodeKind.Float );
		Require( yaw, NodeKind.Float );
		Optional( settings.Weight, NodeKind.Float );
		if ( string.IsNullOrWhiteSpace( settings.ReferenceBone ) || settings.Bones.Count is < 1 or > 255 )
			throw new ArgumentException( "Look At requires a reference bone and between 1 and 255 chain bones.", nameof( settings ) );
		var previousWeight = 1f;
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var bone in settings.Bones )
		{
			if ( string.IsNullOrWhiteSpace( bone.Bone ) || !names.Add( bone.Bone ) || !float.IsFinite( bone.Weight ) || bone.Weight < 0 || bone.Weight > previousWeight )
				throw new ArgumentException( "Look At bones must be unique, ordered end-to-root, with descending weights in [0,1].", nameof( settings ) );
			previousWeight = bone.Weight;
		}

		if ( !float.IsFinite( settings.YawLimit ) || settings.YawLimit is < 0 or > 180 || !float.IsFinite( settings.PitchLimit ) || settings.PitchLimit is < 0 or > 180 )
			throw new ArgumentException( "Look At limits must be finite angles in [0,180].", nameof( settings ) );
		var rotation = settings.ReferenceRotation;
		var lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
		if ( !float.IsFinite( lengthSquared ) || MathF.Abs( lengthSquared - 1 ) > 0.001f )
			throw new ArgumentException( "Look At reference rotation must be normalized.", nameof( settings ) );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphLookAtChainNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_pitchNodeIdx = (short)Resolve( pitch );
			definition.m_yawNodeIdx = (short)Resolve( yaw );
			definition.m_weightNodeIdx = (short)ResolveOptional( settings.Weight );
			definition.SetReferenceBoneID( native.InternID( IdString( settings.ReferenceBone ) ) );
			definition.m_referenceRotation = new Rotation( rotation.x, rotation.y, rotation.z, rotation.w );
			var nativeBoneIDs = definition.GetBoneIDs();
			foreach ( var bone in settings.Bones )
			{
				nativeBoneIDs.Add( native.InternID( IdString( bone.Bone ) ) );
			}

			var nativeWeights = definition.GetWeights();
			foreach ( var bone in settings.Bones )
			{
				nativeWeights.Add( bone.Weight );
			}

			definition.m_yawLimit = settings.YawLimit;
			definition.m_pitchLimit = settings.PitchLimit;
			return definition;
		} );
	}

	public sealed class TwoBoneIKSettings
	{
		public Node Enabled { get; init; }
		public Node Weight { get; init; }
		public float BlendTime { get; init; } = 0.2f;
		public bool WorldSpace { get; init; } = true;
		public IKBlendMode BlendMode { get; init; } = IKBlendMode.Effector;
		public float ChainRotationWeight { get; init; }
	}

	public sealed class FootIKSettings
	{
		public Node LeftTarget { get; init; }
		public Node RightTarget { get; init; }
		public Node Enabled { get; init; }
		public float BlendTime { get; init; }
		public IKBlendMode BlendMode { get; init; } = IKBlendMode.Effector;
		public bool WorldSpace { get; init; }
	}

	/// <summary>
	/// Analytical two bone IK - rotates the effector bone's two parents (e.g.
	/// shoulder and elbow) so the effector reaches the target, and aligns the
	/// effector to the target's rotation.
	/// </summary>
	/// <param name="child">Pose to apply the IK on top of.</param>
	/// <param name="effectorBone">The end bone of the IK chain, e.g. <c>"hand_R"</c>.</param>
	/// <param name="target">Target node providing the effector target transform, e.g. a <see cref="TargetParameter"/>.</param>
	/// <param name="settings">IK blending and targeting options.</param>
	public Node TwoBoneIK( Node child, string effectorBone, Node target, TwoBoneIKSettings settings = null )
	{
		settings ??= new();
		RequirePose( child );
		Require( target, NodeKind.Target );
		Optional( settings.Enabled, NodeKind.Bool );
		Optional( settings.Weight, NodeKind.Float );
		if ( string.IsNullOrWhiteSpace( effectorBone ) )
			throw new ArgumentException( "Effector bone must not be null or blank.", nameof( effectorBone ) );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphTwoBoneIKNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_effectorTargetNodeIdx = (short)Resolve( target );
			definition.m_enableNodeIdx = (short)ResolveOptional( settings.Enabled );
			definition.m_weightNodeIdx = (short)ResolveOptional( settings.Weight );
			definition.SetEffectorBoneID( native.InternID( IdString( effectorBone ) ) );
			definition.m_blendTime = settings.BlendTime;
			definition.SetBlendMode( (int)settings.BlendMode );
			definition.m_isTargetInWorldSpace = settings.WorldSpace;
			definition.m_chainRotationWeight = settings.ChainRotationWeight;
			return definition;
		} );
	}

	/// <summary>
	/// Two-bone IK applied to the left and right foot bones with independent targets,
	/// falling back to solving only one foot when a single target is provided.
	/// </summary>
	/// <param name="child">Pose to apply the IK on top of.</param>
	/// <param name="leftFootBone">The left foot bone name.</param>
	/// <param name="rightFootBone">The right foot bone name.</param>
	/// <param name="settings">Foot targets and IK blending options.</param>
	public Node FootIK( Node child, string leftFootBone, string rightFootBone, FootIKSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		RequirePose( child );
		Optional( settings.LeftTarget, NodeKind.Target );
		Optional( settings.RightTarget, NodeKind.Target );
		Optional( settings.Enabled, NodeKind.Bool );
		if ( string.IsNullOrWhiteSpace( leftFootBone ) )
			throw new ArgumentException( "Left foot bone must not be null or blank.", nameof( leftFootBone ) );
		if ( string.IsNullOrWhiteSpace( rightFootBone ) )
			throw new ArgumentException( "Right foot bone must not be null or blank.", nameof( rightFootBone ) );
		if ( settings.LeftTarget is null && settings.RightTarget is null )
			throw new ArgumentException( "FootIK needs at least one foot target." );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphFootIKNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.SetLeftFootBoneID( native.InternID( IdString( leftFootBone ) ) );
			definition.SetRightFootBoneID( native.InternID( IdString( rightFootBone ) ) );
			definition.m_leftTargetNodeIdx = (short)ResolveOptional( settings.LeftTarget );
			definition.m_rightTargetNodeIdx = (short)ResolveOptional( settings.RightTarget );
			definition.m_enableNodeIdx = (short)ResolveOptional( settings.Enabled );
			definition.m_blendTime = settings.BlendTime;
			definition.SetBlendMode( (int)settings.BlendMode );
			definition.m_isTargetInWorldSpace = settings.WorldSpace;
			return definition;
		} );
	}

	/// <summary>
	/// Redirect an animation clip's root-motion facing toward a character-space
	/// <paramref name="target"/> - either a direction (a vector node) or an angle offset
	/// in degrees (a float node). Uses an orientation warp event on the clip.
	/// </summary>
	/// <param name="clip">The animation clip to warp; must be a clip node.</param>
	/// <param name="target">A vector direction, or a float angle offset in degrees.</param>
	/// <param name="relativeToCharacter">Whether an offset is relative to the character or the original root motion.</param>
	/// <param name="samplingMode">How the warped root motion is sampled.</param>
	/// <param name="alignmentMode">Whether to align movement direction or character forward.</param>
	public Node OrientationWarp( Node clip, Node target, bool relativeToCharacter = true, RootMotionSamplingMode samplingMode = RootMotionSamplingMode.WorldSpace, OrientationWarpAlignmentMode alignmentMode = OrientationWarpAlignmentMode.MovementDirection )
	{
		RequireClip( clip );
		if ( target is null )
			throw new ArgumentException( "OrientationWarp needs a direction (vector) or angle offset (float) target.", nameof( target ) );
		ValidateOwner( target, nameof( target ) );
		if ( target.Kind is not (NodeKind.Vector or NodeKind.Float) )
			throw new ArgumentException( $"OrientationWarp target must be a vector direction or a float angle offset, but was {target.Kind}.", nameof( target ) );
		var isAngleOffset = target.Kind == NodeKind.Float;
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphOrientationWarpNode.Add( native );
			definition.m_clipReferenceNodeIdx = (short)Resolve( clip );
			definition.m_targetValueNodeIdx = (short)Resolve( target );
			definition.m_isOffsetNode = isAngleOffset;
			definition.m_isOffsetRelativeToCharacter = relativeToCharacter;
			definition.SetAlignmentMode( (int)alignmentMode );
			definition.SetSamplingMode( (int)samplingMode );
			return definition;
		} );
	}

	/// <summary>
	/// How a <see cref="TargetWarp"/> reacts when its target moves mid-warp.
	/// </summary>
	[Expose]
	public enum TargetWarpUpdate
	{
		None,
		Recalculate,
		Offset,
		RecalculateOrOffset,
	}

	public sealed class TargetWarpSettings
	{
		public TargetWarpUpdate UpdateRule { get; init; }
		public RootMotionSamplingMode SamplingMode { get; init; } = RootMotionSamplingMode.WorldSpace;
		public bool AlignAtLastWarpEvent { get; init; }
		public string AlignmentBone { get; init; }
		public float SamplingPositionErrorThreshold { get; init; } = 0.05f;
		public float MaxTangentLength { get; init; } = 1.25f;
		public float LerpFallbackDistance { get; init; } = 0.1f;
		public float TargetUpdateDistance { get; init; } = 0.1f;
		public float TargetUpdateAngleDegrees { get; init; } = 5.0f;
	}

	/// <summary>
	/// Warp an animation clip's root motion so it reaches a world-space
	/// <paramref name="target"/>, using target-warp events authored on the clip.
	/// </summary>
	/// <param name="clip">The animation clip to warp; must be a clip node with target-warp events.</param>
	/// <param name="target">Target node giving the transform to reach.</param>
	/// <param name="settings">Warping and target-update options.</param>
	public Node TargetWarp( Node clip, Node target, TargetWarpSettings settings = null )
	{
		settings ??= new();
		RequireClip( clip );
		Require( target, NodeKind.Target );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphTargetWarpNode.Add( native );
			definition.m_clipReferenceNodeIdx = (short)Resolve( clip );
			definition.m_targetValueNodeIdx = (short)Resolve( target );
			definition.SetSamplingMode( (int)settings.SamplingMode );
			definition.SetTargetUpdateRule( (int)settings.UpdateRule );
			definition.m_allowTargetUpdate = settings.UpdateRule != TargetWarpUpdate.None;
			definition.m_alignWithTargetAtLastWarpEvent = settings.AlignAtLastWarpEvent;
			definition.SetAlignmentBoneID( native.InternID( IdString( settings.AlignmentBone ) ) );
			definition.m_samplingPositionErrorThresholdSq = settings.SamplingPositionErrorThreshold * settings.SamplingPositionErrorThreshold;
			definition.m_maxTangentLength = settings.MaxTangentLength;
			definition.m_lerpFallbackDistanceThreshold = settings.LerpFallbackDistance;
			definition.m_targetUpdateDistanceThreshold = settings.TargetUpdateDistance;
			definition.m_targetUpdateAngleThresholdRadians = settings.TargetUpdateAngleDegrees.DegreeToRadian();
			return definition;
		} );
	}

	/// <summary>
	/// Which axes and behaviours a <see cref="RootMotionOverride"/> is allowed to drive.
	/// </summary>
	[Expose]
	[Flags]
	public enum RootMotionOverrideFlags
	{
		None = 0,
		/// <summary>Allow overriding movement along the character X axis.</summary>
		MoveX = 1 << 0,
		/// <summary>Allow overriding movement along the character Y axis.</summary>
		MoveY = 1 << 1,
		/// <summary>Allow overriding movement along the character Z axis.</summary>
		MoveZ = 1 << 2,
		/// <summary>Allow pitching the character facing (3D facing).</summary>
		FacingPitch = 1 << 3,
		/// <summary>Listen for root-motion override events on the animation.</summary>
		ListenForEvents = 1 << 4,
	}

	public sealed class RootMotionOverrideSettings
	{
		public Node MovingVelocity { get; init; }
		public Node FacingDirection { get; init; }
		public Node LinearVelocityLimit { get; init; }
		public Node AngularVelocityLimit { get; init; }
		public float MaxLinearVelocity { get; init; } = -1.0f;
		public float MaxAngularVelocityDegrees { get; init; } = -1.0f;
		public RootMotionOverrideFlags Flags { get; init; } = RootMotionOverrideFlags.MoveX | RootMotionOverrideFlags.MoveY | RootMotionOverrideFlags.MoveZ;
	}

	/// <summary>
	/// Replace a pose's root motion with a desired character-space movement velocity,
	/// facing direction, or both - e.g. driving movement from gameplay values instead
	/// of the authored root motion.
	/// </summary>
	/// <param name="child">Pose whose root motion is overridden.</param>
	/// <param name="settings">Movement, facing, and velocity-limit options.</param>
	public Node RootMotionOverride( Node child, RootMotionOverrideSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		RequirePose( child );
		Optional( settings.MovingVelocity, NodeKind.Vector );
		Optional( settings.FacingDirection, NodeKind.Vector );
		Optional( settings.LinearVelocityLimit, NodeKind.Float );
		Optional( settings.AngularVelocityLimit, NodeKind.Float );
		if ( settings.MovingVelocity is null && settings.FacingDirection is null )
			throw new ArgumentException( "RootMotionOverride needs at least one of movingVelocity or facingDirection." );
		return AddNode( NodeKind.Pose, native =>
		{
			var definition = NativeEngine.GraphRootMotionOverrideNode.Add( native );
			definition.m_childNodeIdx = (short)Resolve( child );
			definition.m_desiredMovingVelocityNodeIdx = (short)ResolveOptional( settings.MovingVelocity );
			definition.m_desiredFacingDirectionNodeIdx = (short)ResolveOptional( settings.FacingDirection );
			definition.m_linearVelocityLimitNodeIdx = (short)ResolveOptional( settings.LinearVelocityLimit );
			definition.m_angularVelocityLimitNodeIdx = (short)ResolveOptional( settings.AngularVelocityLimit );
			definition.m_maxLinearVelocity = settings.MaxLinearVelocity;
			definition.m_maxAngularVelocity = settings.MaxAngularVelocityDegrees < 0.0f ? settings.MaxAngularVelocityDegrees : settings.MaxAngularVelocityDegrees.DegreeToRadian();
			definition.m_overrideFlags = (byte)((int)settings.Flags);
			return definition;
		} );
	}
}
