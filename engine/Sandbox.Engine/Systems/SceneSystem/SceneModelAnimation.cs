using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A scene model's animation: the <see cref="ModelAnimationInstance"/> every scene model managed code makes animates on. A
/// scene model native code made (a tool's preview) has none: native code animates its twin, a
/// <see cref="CSceneAnimatableObject"/>, and managed code only reads the twin's pose, bounds and attachments. Draw state (body
/// groups, deformation volumes) is always the twin's.
/// </summary>
internal readonly unsafe struct SceneModelAnimation
{
	/// <summary>
	/// The scene model's native twin: it draws in Source 2's views and keeps draw state.
	/// </summary>
	internal readonly CSceneAnimatableObject Twin;

	/// <summary>
	/// What animates the scene model, if not its twin.
	/// </summary>
	internal readonly ModelAnimationInstance Instance;

	readonly SceneModel owner;

	internal SceneModelAnimation( CSceneAnimatableObject twin, ModelAnimationInstance instance, SceneModel owner )
	{
		Twin = twin;
		Instance = instance;
		this.owner = owner;
	}

	internal bool IsNull => Twin.IsNull;
	internal bool IsValid => !IsNull;
	internal bool HasInstance => !Instance.IsNull;

	/// <summary>
	/// The pose the instance publishes, read in place. Null without an instance.
	/// </summary>
	internal AnimationPose* Pose => HasInstance ? (AnimationPose*)Instance.GetPose() : null;

	internal float m_flDeltaTime => HasInstance ? Instance.GetDeltaTime() : 0;
	internal BBox m_worldBounds => HasInstance ? owner.AnimatedWorldBounds : Twin.m_worldBounds;
	internal BBox m_localBounds => HasInstance ? owner.AnimatedLocalBounds : Twin.m_localBounds;

	internal void InitAnimation() { if ( HasInstance ) Instance.InitAnimation(); }
	internal void Update( float dt ) { if ( HasInstance ) Instance.Update( dt ); }
	internal void SetBindPose() { if ( HasInstance ) Instance.SetBindPose(); }
	internal void CalculateWorldSpaceBones() { if ( HasInstance ) Instance.CalculateWorldSpaceBones(); }
	internal void FinishUpdate() { if ( HasInstance ) Instance.FinishUpdate(); }

	/// <summary>
	/// Merge from another scene model's bones. Both animate the same way (the switch is per process); an instance can't merge
	/// from a twin.
	/// </summary>
	internal void MergeFrom( SceneModel parent )
	{
		if ( !HasInstance ) return;

		var source = parent.animNative.Instance;
		if ( !source.IsNull ) Instance.MergeFrom( source );
	}

	internal void SetWorldSpaceRenderBoneTransform( int bone, Transform tx ) { if ( HasInstance ) Instance.SetWorldSpaceRenderBoneTransform( bone, tx ); }
	internal Transform GetWorldSpaceRenderBoneTransform( int bone ) => HasInstance ? Instance.GetWorldSpaceRenderBoneTransform( bone ) : Twin.GetWorldSpaceRenderBoneTransform( bone );
	internal Transform GetWorldSpaceRenderBoneTransform( string bone ) => HasInstance ? Instance.GetWorldSpaceRenderBoneTransform( bone ) : Twin.GetWorldSpaceRenderBoneTransform( bone );
	internal Transform GetWorldSpaceRenderBonePreviousTransform( int bone ) => HasInstance ? Instance.GetWorldSpaceRenderBonePreviousTransform( bone ) : Twin.GetWorldSpaceRenderBonePreviousTransform( bone );
	internal Transform GetWorldSpaceRenderBonePreviousTransform( string bone ) => HasInstance ? Instance.GetWorldSpaceRenderBonePreviousTransform( bone ) : Twin.GetWorldSpaceRenderBonePreviousTransform( bone );
	internal Transform GetLocalSpaceRenderBoneTransform( int bone ) => HasInstance ? Instance.GetLocalSpaceRenderBoneTransform( bone ) : Twin.GetLocalSpaceRenderBoneTransform( bone );
	internal Transform GetLocalSpaceRenderBoneTransform( string bone ) => HasInstance ? Instance.GetLocalSpaceRenderBoneTransform( bone ) : Twin.GetLocalSpaceRenderBoneTransform( bone );
	internal Transform GetWorldSpaceAnimationTransform( int bone ) => HasInstance ? Instance.GetWorldSpaceAnimationTransform( bone ) : Twin.GetWorldSpaceAnimationTransform( bone );
	internal void GetWorldSpaceRenderBoneTransforms( int count, IntPtr dest ) { if ( HasInstance ) Instance.GetWorldSpaceRenderBoneTransforms( count, dest ); else Twin.GetWorldSpaceRenderBoneTransforms( count, dest ); }
	internal void GetWorldSpaceRenderBonePreviousTransforms( int count, IntPtr dest ) { if ( HasInstance ) Instance.GetWorldSpaceRenderBonePreviousTransforms( count, dest ); else Twin.GetWorldSpaceRenderBonePreviousTransforms( count, dest ); }
	internal int GetClosestBoneIndex( Vector3 point ) => HasInstance ? Instance.GetClosestBoneIndex( point ) : Twin.GetClosestBoneIndex( point );

	/// <summary>
	/// The world-space render bones as matrices, into <paramref name="count"/> at <paramref name="dest"/>; identity past the last.
	/// </summary>
	internal void GetWorldSpaceRenderBoneMatrices( int count, IntPtr dest )
	{
		if ( !HasInstance )
		{
			Twin.GetWorldSpaceRenderBoneMatrices( count, dest );
			return;
		}

		var pose = Pose;
		var bones = new Span<Matrix3x4>( (void*)dest, count );
		var copy = Math.Min( count, pose->BoneCount );
		new ReadOnlySpan<Matrix3x4>( pose->Bones, copy ).CopyTo( bones );
		bones[copy..].Fill( Matrix3x4.Identity );
	}

	/// <summary>
	/// The flex controllers' weights, up to <paramref name="max"/> into <paramref name="dest"/>; returns how many there are.
	/// </summary>
	internal int GetFlexControllerWeights( IntPtr dest, int max )
	{
		if ( !HasInstance ) return Twin.GetFlexControllerWeights( dest, max );

		var pose = Pose;
		var copy = Math.Min( max, pose->FlexWeightCount );
		if ( dest != IntPtr.Zero && copy > 0 )
			new ReadOnlySpan<float>( pose->FlexWeights, copy ).CopyTo( new Span<float>( (void*)dest, copy ) );

		return pose->FlexWeightCount;
	}

	internal Transform GetParentSpaceBone( int index ) => HasInstance ? Instance.GetParentSpaceBone( index ) : Twin.GetParentSpaceBone( index );
	internal void GetParentSpaceBones( int count, IntPtr dest ) { if ( HasInstance ) Instance.GetParentSpaceBones( count, dest ); else Twin.GetParentSpaceBones( count, dest ); }
	internal void SetParentSpaceBone( int index, Transform tx ) { if ( HasInstance ) Instance.SetParentSpaceBone( index, tx ); }

	internal void ResetGraphParameters() { if ( HasInstance ) Instance.ResetGraphParameters(); }
	internal void SetAnimGraphChangedCallback( IntPtr callback ) { if ( HasInstance ) Instance.SetAnimGraphChangedCallback( callback ); }
	internal void SetAnimGraph( string name ) { if ( HasInstance ) Instance.SetAnimGraph( name ); }
	internal void SetAnimGraph( HAnimationGraph graph ) { if ( HasInstance ) Instance.SetAnimGraph( graph ); }
	internal HAnimationGraph GetAnimGraph() => HasInstance ? Instance.GetAnimGraph() : default;
	internal void SetAnimGraph2( HAnimGraph2 graph ) { if ( HasInstance ) Instance.SetAnimGraph2( graph ); }
	internal void SetAnimGraph2ControllerCreatedCallback( IntPtr callback ) { if ( HasInstance ) Instance.SetAnimGraph2ControllerCreatedCallback( callback ); }
	internal AnimationGraph2Controller GetAnimGraph2Controller() => HasInstance ? Instance.GetAnimGraph2Controller() : default;
	internal float GetAnimGraph2UpdateTimeMs() => HasInstance ? Instance.GetAnimGraph2UpdateTimeMs() : 0;
	internal void SetLowDetailAnimation( bool low ) { if ( HasInstance ) Instance.SetLowDetailAnimation( low ); }
	internal bool IsLowDetailAnimation() => HasInstance ? Instance.IsLowDetailAnimation() : false;

	internal void SBox_SetFlexOverride( StringToken name, float weight, float fade ) { if ( HasInstance ) Instance.SBox_SetFlexOverride( name, weight, fade ); }
	internal void SBox_SetFlexOverride( int flex, float weight, float fade ) { if ( HasInstance ) Instance.SBox_SetFlexOverride( flex, weight, fade ); }
	internal float SBox_GetFlexOverride( int flex ) => HasInstance ? Instance.SBox_GetFlexOverride( flex ) : 0;
	internal float SBox_GetFlexOverride( StringToken name ) => HasInstance ? Instance.SBox_GetFlexOverride( name ) : 0;
	internal void SBox_ClearFlexOverride( float fade ) { if ( HasInstance ) Instance.SBox_ClearFlexOverride( fade ); }
	internal void SBox_ClearFlexOverride( StringToken name, float fade ) { if ( HasInstance ) Instance.SBox_ClearFlexOverride( name, fade ); }
	internal void SBox_ClearFlexOverride( int flex, float fade ) { if ( HasInstance ) Instance.SBox_ClearFlexOverride( flex, fade ); }

	internal void DirectPlayback_PlaySequence( string name ) { if ( HasInstance ) Instance.DirectPlayback_PlaySequence( name ); }
	internal void DirectPlayback_PlaySequence( string name, Vector3 target, float heading, float interpTime ) { if ( HasInstance ) Instance.DirectPlayback_PlaySequence( name, target, heading, interpTime ); }
	internal void DirectPlayback_CancelSequence() { if ( HasInstance ) Instance.DirectPlayback_CancelSequence(); }
	internal float DirectPlayback_GetSequenceCycle() => HasInstance ? Instance.DirectPlayback_GetSequenceCycle() : 0;
	internal string DirectPlayback_GetSequence() => HasInstance ? Instance.DirectPlayback_GetSequence() : null;
	internal void DirectPlayback_SetSequenceStartTime( float time ) { if ( HasInstance ) Instance.DirectPlayback_SetSequenceStartTime( time ); }
	internal float DirectPlayback_GetSequenceDuration() => HasInstance ? Instance.DirectPlayback_GetSequenceDuration() : 0;

	internal bool SBox_GetAttachment( StringToken name, bool worldspace, out Transform tx ) => HasInstance ? Instance.SBox_GetAttachment( name, worldspace, out tx ) : Twin.SBox_GetAttachment( name, worldspace, out tx );

	internal void SetShouldUseAnimGraph( bool enabled ) { if ( HasInstance ) Instance.SetShouldUseAnimGraph( enabled ); }
	internal bool GetShouldUseAnimGraph() => HasInstance ? Instance.GetShouldUseAnimGraph() : false;

	internal string GetSequence() => HasInstance ? Instance.GetSequence() : null;
	internal void SetSequence( string name ) { if ( HasInstance ) Instance.SetSequence( name ); }
	internal float GetSequenceDuration() => HasInstance ? Instance.GetSequenceDuration() : 0;
	internal float GetSequenceCycle() => HasInstance ? Instance.GetSequenceCycle() : 0;
	internal void SetSequenceCycle( float cycle ) { if ( HasInstance ) Instance.SetSequenceCycle( cycle ); }
	internal void SetSequenceLooping( bool looping ) { if ( HasInstance ) Instance.SetSequenceLooping( looping ); }
	internal bool IsSequenceFinished() => HasInstance ? Instance.IsSequenceFinished() : false;
	internal void SetSequenceBlending( bool blending ) { if ( HasInstance ) Instance.SetSequenceBlending( blending ); }

	internal float GetPlaybackRate() => HasInstance ? Instance.GetPlaybackRate() : 1;
	internal void SetPlaybackRate( float rate ) { if ( HasInstance ) Instance.SetPlaybackRate( rate ); }

	internal int PendingAnimationEvents() => HasInstance ? Instance.PendingAnimationEvents() : 0;
	internal void RunAnimationEvents( IntPtr callback ) { if ( HasInstance ) Instance.RunAnimationEvents( callback ); }
	internal void DispatchTagEvents( IntPtr callback ) { if ( HasInstance ) Instance.DispatchTagEvents( callback ); }

	internal void ClearPhysicsBones() { if ( HasInstance ) Instance.ClearPhysicsBones(); }
	internal void SetPhysicsBone( ushort bone, Transform tx ) { if ( HasInstance ) Instance.SetPhysicsBone( bone, tx ); }
	internal bool HasPhysicsBones() => HasInstance ? Instance.HasPhysicsBones() : false;

	internal Transform GetRootMotion() => HasInstance ? Instance.GetRootMotion() : Transform.Zero;

	internal IAnimParameterInstance GetAnimParameter( int index ) => HasInstance ? Instance.GetAnimParameter( index ) : default;

	// Draw state: the twin's whichever animates
	internal void SetBodyGroup( string name, int value ) => Twin.SetBodyGroup( name, value );
	internal void SetDeformationVolumes( int count, IntPtr volumes ) => Twin.SetDeformationVolumes( count, volumes );
	internal void SetDeformationAnchors( int count, IntPtr anchors ) => Twin.SetDeformationAnchors( count, anchors );
}

/// <summary>
/// Native's <c>AnimationPose_t</c>: an animation instance's pose, read in place.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct AnimationPose
{
	public Matrix3x4* Bones;
	public Matrix3x4* PreviousBones;
	public float* FlexWeights;
	public int BoneCount;
	public int FlexWeightCount;
	public int Version;
	int padding;
}

/// <summary>
/// Native's <c>AnimationBoneBox_t</c>: a render-bone box in a model bone's space, a boneless mesh's box in the model's space
/// (bone -1), or the model's view bounds (bone -2) for a model without bones.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct AnimationBoneBox
{
	public int Bone;
	public Vector3 Mins;
	public Vector3 Maxs;
}
