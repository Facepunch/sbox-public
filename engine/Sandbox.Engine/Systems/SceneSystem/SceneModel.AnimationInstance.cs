using NativeEngine;
using Sandbox.Utility;

namespace Sandbox;

public sealed partial class SceneModel
{
	/// <summary>
	/// The bounds of the pose, worked out from it as native's <c>UpdateBoundsFromRenderBones</c> does, while an instance
	/// animates this model.
	/// </summary>
	internal BBox AnimatedWorldBounds { get; private set; }

	/// <inheritdoc cref="AnimatedWorldBounds"/>
	internal BBox AnimatedLocalBounds { get; set; }

	PoseBox[] _boneBoxes = [];
	int _boneBoxesVersion = -1;

	/// <summary>
	/// A render-bone box ready to transform: its centre and half size worked out once, as <see cref="TransformBox"/> did
	/// every frame.
	/// </summary>
	internal struct PoseBox
	{
		public int Bone;
		public Vector3 Mins, Maxs, Center, Extents;

		public PoseBox( in AnimationBoneBox box )
		{
			Bone = box.Bone;
			Mins = box.Mins;
			Maxs = box.Maxs;
			Center = (box.Mins + box.Maxs) * 0.5f;
			Extents = box.Maxs - Center;
		}
	}

	/// <summary>
	/// The model's render-bone boxes as <see cref="UpdatePoseBounds"/> walks them. Native lists a box per mesh and bone, so a
	/// model with many meshes (body groups, LODs) repeats the same box many times: the repeats are dropped, and the rest
	/// put in bone order, so each bone's transform is worked out once. Bounds are a minimum and maximum over the boxes,
	/// which neither repeats nor order change. A model without bones keeps its boxes as they are: its view bounds box
	/// starts the bounds again, so there order matters.
	/// </summary>
	internal static PoseBox[] PoseBoxes( ReadOnlySpan<AnimationBoneBox> boxes )
	{
		foreach ( ref readonly var box in boxes )
		{
			if ( box.Bone < -1 )
			{
				var kept = new PoseBox[boxes.Length];
				for ( int i = 0; i < boxes.Length; i++ ) kept[i] = new PoseBox( boxes[i] );
				return kept;
			}
		}

		var seen = new HashSet<(int, Vector3, Vector3)>( boxes.Length );
		var distinct = new List<PoseBox>( boxes.Length );
		foreach ( ref readonly var box in boxes )
		{
			if ( seen.Add( (box.Bone, box.Mins, box.Maxs) ) )
				distinct.Add( new PoseBox( box ) );
		}

		var result = distinct.ToArray();
		Array.Sort( result, static ( a, b ) => a.Bone.CompareTo( b.Bone ) );
		return result;
	}

	/// <summary>
	/// Animate on an instance of its own: what managed code makes has no native object.
	/// </summary>
	void CreateAnimationInstance( Model model, in Transform transform )
	{
		var instance = ModelAnimationInstance.Create();
		instance.SetModel( model.native, transform );
		animNative = new SceneModelAnimation( default, instance, this );

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
	/// The instance's pose changed: its bounds follow it.
	/// </summary>
	unsafe void PoseChanged()
	{
		if ( !animNative.HasInstance ) return;

		UpdatePoseBounds( animNative.Pose );
	}

	/// <summary>
	/// Native's <c>CSceneAnimatableObject::UpdateBoundsFromRenderBones</c> over the instance's pose: each of the model's
	/// render-bone boxes through its bone, a boneless mesh's box through the model's transform, the view bounds for a model
	/// without bones; a quarter bigger with deformation volumes. The world box is kept for the renderer.
	/// </summary>
	unsafe void UpdatePoseBounds( AnimationPose* pose )
	{
		if ( Model is not { } model || model.native.IsNull ) return;

		if ( _boneBoxesVersion != pose->Version )
		{
			_boneBoxesVersion = pose->Version;
			var count = ModelAnimationInstance.GetRenderBoneBoxes( model.native, IntPtr.Zero, 0 );
			var boxes = count == 0 ? [] : new AnimationBoneBox[count];
			fixed ( AnimationBoneBox* p = boxes )
				ModelAnimationInstance.GetRenderBoneBoxes( model.native, (IntPtr)p, count );
			_boneBoxes = PoseBoxes( boxes );
		}

		var rootToWorld = Matrix3x4.From( Transform );
		var worldToLocal = InvertTR( rootToWorld );

		var worldMins = new Vector3( float.MaxValue );
		var worldMaxs = new Vector3( float.MinValue );
		var localMins = worldMins;
		var localMaxs = worldMaxs;

		// The boxes come in bone order: a bone's transform into the model's space is worked out once for all its boxes
		var localBone = default( Matrix3x4 );
		var localBoneIndex = int.MinValue;

		foreach ( ref readonly var box in _boneBoxes.AsSpan() )
		{
			if ( box.Bone >= 0 )
			{
				if ( box.Bone >= pose->BoneCount ) continue;

				ref readonly var bone = ref pose->Bones[box.Bone];
				if ( box.Bone != localBoneIndex )
				{
					localBone = Matrix3x4.Concat( worldToLocal, bone );
					localBoneIndex = box.Bone;
				}

				TransformBox( bone, box.Center, box.Extents, ref worldMins, ref worldMaxs );
				TransformBox( localBone, box.Center, box.Extents, ref localMins, ref localMaxs );
			}
			else if ( box.Bone == -1 )
			{
				TransformBox( rootToWorld, box.Center, box.Extents, ref worldMins, ref worldMaxs );
				localMins = localMins.ComponentMin( box.Mins );
				localMaxs = localMaxs.ComponentMax( box.Maxs );
			}
			else
			{
				// A model with no bones at all: its view bounds, as they are
				worldMins = new Vector3( float.MaxValue );
				worldMaxs = new Vector3( float.MinValue );
				TransformBox( rootToWorld, box.Center, box.Extents, ref worldMins, ref worldMaxs );
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

		// Kept here for the renderer and Bounds
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
	/// Native's <c>TransformAABB</c> of a box by its centre and half size (<see cref="PoseBox"/>), the result added to
	/// <paramref name="mins"/>/<paramref name="maxs"/>.
	/// </summary>
	static void TransformBox( in Matrix3x4 m, Vector3 center, Vector3 extents, ref Vector3 mins, ref Vector3 maxs )
	{
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
