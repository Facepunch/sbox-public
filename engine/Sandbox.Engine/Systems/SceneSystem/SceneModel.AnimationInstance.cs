using NativeEngine;
using Sandbox.Utility;

namespace Sandbox;

public sealed partial class SceneModel
{
	/// <summary>
	/// Whether scene models made from now on animate on a <see cref="ModelAnimationInstance"/> of their own instead of their
	/// native twin: on unless <c>-noaniminstance</c> is on the command line. Their twin keeps draw state and draws their pose
	/// in Source 2's views.
	/// </summary>
	internal static bool UsesAnimationInstance => AnimationInstanceOverride ?? (_animationInstanceSwitch ??= !CommandLine.HasSwitch( "-noaniminstance" ));

	/// <summary>Tests set <see cref="UsesAnimationInstance"/> either way; null follows the command line.</summary>
	internal static bool? AnimationInstanceOverride { get; set; }

	static bool? _animationInstanceSwitch;

	/// <summary>
	/// The bounds of the pose, worked out from it as native's <c>UpdateBoundsFromRenderBones</c> does, while an instance
	/// animates this model.
	/// </summary>
	internal BBox AnimatedWorldBounds { get; private set; }

	/// <inheritdoc cref="AnimatedWorldBounds"/>
	internal BBox AnimatedLocalBounds { get; private set; }

	AnimationBoneBox[] _boneBoxes = [];
	int _boneBoxesVersion = -1;

	/// <summary>
	/// Animate on an instance of its own from here: the twin stops animating and keeps its bind pose until it's given one.
	/// </summary>
	void CreateAnimationInstance( Model model, in Transform transform )
	{
		var instance = ModelAnimationInstance.Create();
		animNative.Twin.SetExternalAnimation();
		instance.SetModel( model.native, transform );
		animNative = new SceneModelAnimation( animNative.Twin, instance, this );

		// The twin had the callback; its controller is the instance's now
		RegisterAnimationControllerCallback();
	}

	void DestroyAnimationInstance()
	{
		if ( !animNative.HasInstance ) return;

		animNative.Instance.Destroy();
	}

	/// <summary>
	/// The instance starts again on a new model as native's <c>ChangeModel</c> starts an animated object again: bind pose, its
	/// graphs, and a first update.
	/// </summary>
	void AnimationInstanceModelChanged()
	{
		if ( !animNative.HasInstance || Model is not { } model ) return;

		var instance = animNative.Instance;
		instance.SetModel( model.native, Transform );
		instance.InitAnimation();
		instance.Update( 0.1f );
		instance.CalculateWorldSpaceBones();
		instance.FinishUpdate();
		PoseChanged();
	}

	/// <summary>
	/// The instance's pose changed: its bounds follow it, and Source 2's views draw it through the twin while anything reads it.
	/// </summary>
	unsafe void PoseChanged()
	{
		if ( !animNative.HasInstance ) return;

		var pose = animNative.Pose;
		UpdatePoseBounds( pose );

		if ( !WritesNative ) return;

		var twin = animNative.Twin;
		twin.SetWorldSpaceRenderBoneMatrices( pose->BoneCount, (IntPtr)pose->Bones );
		twin.SetFlexControllerWeights( (IntPtr)pose->FlexWeights, pose->FlexWeightCount );
	}

	/// <summary>
	/// Native's <c>CSceneAnimatableObject::UpdateBoundsFromRenderBones</c> over the instance's pose: each of the model's
	/// render-bone boxes through its bone, a boneless mesh's box through the model's transform, the view bounds for a model
	/// without bones; a quarter bigger with deformation volumes. The twin gets the world box, which the renderer reads.
	/// </summary>
	unsafe void UpdatePoseBounds( AnimationPose* pose )
	{
		if ( Model is not { } model || model.native.IsNull ) return;

		if ( _boneBoxesVersion != pose->Version )
		{
			_boneBoxesVersion = pose->Version;
			var count = ModelAnimationInstance.GetRenderBoneBoxes( model.native, IntPtr.Zero, 0 );
			_boneBoxes = count == 0 ? [] : new AnimationBoneBox[count];
			fixed ( AnimationBoneBox* p = _boneBoxes )
				ModelAnimationInstance.GetRenderBoneBoxes( model.native, (IntPtr)p, count );
		}

		var rootToWorld = Matrix3x4.From( Transform );
		var worldToLocal = InvertTR( rootToWorld );

		var worldMins = new Vector3( float.MaxValue );
		var worldMaxs = new Vector3( float.MinValue );
		var localMins = worldMins;
		var localMaxs = worldMaxs;

		foreach ( ref readonly var box in _boneBoxes.AsSpan() )
		{
			if ( box.Bone >= 0 )
			{
				if ( box.Bone >= pose->BoneCount ) continue;

				ref readonly var bone = ref pose->Bones[box.Bone];
				TransformBox( bone, box.Mins, box.Maxs, ref worldMins, ref worldMaxs );
				TransformBox( Matrix3x4.Concat( worldToLocal, bone ), box.Mins, box.Maxs, ref localMins, ref localMaxs );
			}
			else if ( box.Bone == -1 )
			{
				TransformBox( rootToWorld, box.Mins, box.Maxs, ref worldMins, ref worldMaxs );
				localMins = localMins.ComponentMin( box.Mins );
				localMaxs = localMaxs.ComponentMax( box.Maxs );
			}
			else
			{
				// A model with no bones at all: its view bounds, as they are
				worldMins = new Vector3( float.MaxValue );
				worldMaxs = new Vector3( float.MinValue );
				TransformBox( rootToWorld, box.Mins, box.Maxs, ref worldMins, ref worldMaxs );
				localMins = box.Mins;
				localMaxs = box.Maxs;
			}
		}

		var world = new BBox( worldMins, worldMaxs );
		var local = new BBox( localMins, localMaxs );

		if ( _volumeSnapshot.Count > 0 )
		{
			world = Grow( world );
			local = Grow( local );
		}

		AnimatedWorldBounds = world;
		AnimatedLocalBounds = local;

		// Kept here for the renderer and Bounds; the twin gets them too, for render-mesh traces and Source 2's views
		SetPoseBounds( world );
	}

	/// <summary>
	/// <c>AABB_t::EnsureMinSize( size * 1.25 )</c>: an eighth of the size more on each side.
	/// </summary>
	static BBox Grow( in BBox box )
	{
		var half = (box.Maxs - box.Mins) * 0.125f;
		return new BBox( box.Mins - half, box.Maxs + half );
	}

	/// <summary>
	/// Native's <c>TransformAABB</c>, the result added to <paramref name="mins"/>/<paramref name="maxs"/>.
	/// </summary>
	static void TransformBox( in Matrix3x4 m, Vector3 boxMins, Vector3 boxMaxs, ref Vector3 mins, ref Vector3 maxs )
	{
		var center = (boxMins + boxMaxs) * 0.5f;
		var extents = boxMaxs - center;

		var worldCenter = new Vector3(
			m.Row0.x * center.x + m.Row0.y * center.y + m.Row0.z * center.z + m.Row0.w,
			m.Row1.x * center.x + m.Row1.y * center.y + m.Row1.z * center.z + m.Row1.w,
			m.Row2.x * center.x + m.Row2.y * center.y + m.Row2.z * center.z + m.Row2.w );

		var worldExtents = new Vector3(
			MathF.Abs( m.Row0.x * extents.x ) + MathF.Abs( m.Row0.y * extents.y ) + MathF.Abs( m.Row0.z * extents.z ),
			MathF.Abs( m.Row1.x * extents.x ) + MathF.Abs( m.Row1.y * extents.y ) + MathF.Abs( m.Row1.z * extents.z ),
			MathF.Abs( m.Row2.x * extents.x ) + MathF.Abs( m.Row2.y * extents.y ) + MathF.Abs( m.Row2.z * extents.z ) );

		mins = mins.ComponentMin( worldCenter - worldExtents );
		maxs = maxs.ComponentMax( worldCenter + worldExtents );
	}

	/// <summary>
	/// Native's <c>MatrixInvert</c>: the transpose with the translation taken back through it, divided by the squared scale
	/// when that isn't one (it assumes a uniform scale).
	/// </summary>
	static Matrix3x4 InvertTR( in Matrix3x4 m )
	{
		var t = new Vector3( m.Row0.w, m.Row1.w, m.Row2.w );
		var r0 = new Vector3( m.Row0.x, m.Row1.x, m.Row2.x );
		var r1 = new Vector3( m.Row0.y, m.Row1.y, m.Row2.y );
		var r2 = new Vector3( m.Row0.z, m.Row1.z, m.Row2.z );

		var result = new Matrix3x4
		{
			Row0 = new Vector4( r0.x, r0.y, r0.z, -Vector3.Dot( r0, t ) ),
			Row1 = new Vector4( r1.x, r1.y, r1.z, -Vector3.Dot( r1, t ) ),
			Row2 = new Vector4( r2.x, r2.y, r2.z, -Vector3.Dot( r2, t ) ),
		};

		var scaleSqr = m.Row0.x * m.Row0.x + m.Row1.x * m.Row1.x + m.Row2.x * m.Row2.x;
		if ( MathF.Abs( scaleSqr - 1.0f ) < 1e-3f ) return result;

		var inverse = scaleSqr != 0.0f ? 1.0f / scaleSqr : 1.0f;
		result.Row0 *= inverse;
		result.Row1 *= inverse;
		result.Row2 *= inverse;
		return result;
	}
}
