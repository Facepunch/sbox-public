using System;

namespace SceneTests.Components;

#pragma warning disable CS0618

/// <summary>
/// An animated model's bounds are worked out from its pose and every render-bone box native lists for the model, one per
/// mesh and bone. The renderer keeps each distinct box once, so they must come out exactly as walking every box would.
/// </summary>
[TestClass]
public class PoseBoundsTests
{
	static Model HumanModel => Model.Load( "models/citizen_human/human.vmdl" );

	/// <summary>
	/// The bounds of the pose over every box native lists, in its order, as native's <c>UpdateBoundsFromRenderBones</c>.
	/// </summary>
	static unsafe (BBox World, BBox Local) EveryBox( SceneModel model )
	{
		var count = NativeEngine.ModelAnimationInstance.GetRenderBoneBoxes( model.Model.native, IntPtr.Zero, 0 );
		var boxes = new AnimationBoneBox[count];
		fixed ( AnimationBoneBox* p = boxes )
			NativeEngine.ModelAnimationInstance.GetRenderBoneBoxes( model.Model.native, (IntPtr)p, count );

		var pose = model.animNative.Pose;
		var rootToWorld = Matrix3x4.From( model.Transform );
		var worldToLocal = InvertTR( rootToWorld );

		var worldMins = new Vector3( float.MaxValue );
		var worldMaxs = new Vector3( float.MinValue );
		var localMins = worldMins;
		var localMaxs = worldMaxs;

		foreach ( var box in boxes )
		{
			if ( box.Bone >= 0 )
			{
				if ( box.Bone >= pose->BoneCount ) continue;

				var bone = pose->Bones[box.Bone];
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
				worldMins = new Vector3( float.MaxValue );
				worldMaxs = new Vector3( float.MinValue );
				TransformBox( rootToWorld, box.Mins, box.Maxs, ref worldMins, ref worldMaxs );
				localMins = box.Mins;
				localMaxs = box.Maxs;
			}
		}

		return (new BBox( worldMins, worldMaxs ), new BBox( localMins, localMaxs ));
	}

	// Bit for bit: Vector3's == allows a little difference
	static bool Same( Vector3 a, Vector3 b ) => a.x.Equals( b.x ) && a.y.Equals( b.y ) && a.z.Equals( b.z );
	static bool Same( BBox a, BBox b ) => Same( a.Mins, b.Mins ) && Same( a.Maxs, b.Maxs );

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

	/// <summary>
	/// The human repeats most of its boxes across its meshes; with each kept once, a walking, turning, scaled body and a
	/// copy merged onto it get exactly the bounds every box gives.
	/// </summary>
	[TestMethod]
	public void BoundsMatchEveryBox()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldScale = 1.3f;
		var body = go.Components.Create<SkinnedModelRenderer>( false );
		body.Model = HumanModel;
		body.Enabled = true;
		body.Set( "move_x", 150 );
		body.Set( "b_grounded", true );

		var clothing = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		clothing.Model = HumanModel;
		clothing.BoneMergeTarget = body;
		clothing.Enabled = true;

		var count = NativeEngine.ModelAnimationInstance.GetRenderBoneBoxes( HumanModel.native, IntPtr.Zero, 0 );
		var boxes = new AnimationBoneBox[count];
		unsafe
		{
			fixed ( AnimationBoneBox* p = boxes )
				NativeEngine.ModelAnimationInstance.GetRenderBoneBoxes( HumanModel.native, (IntPtr)p, count );
		}
		Assert.IsTrue( SceneModel.PoseBoxes( boxes ).Length < count, "The human should repeat boxes across its meshes" );

		for ( int i = 0; i < 12; i++ )
		{
			go.WorldPosition = new Vector3( i * 30, i * -10, i );
			go.WorldRotation = Rotation.FromYaw( i * 31 );
			scene.GameTick( 1.0 / 30.0 );

			foreach ( var model in new[] { body.SceneModel, clothing.SceneModel } )
			{
				var (world, local) = EveryBox( model );
				Assert.IsTrue( Same( world, model.AnimatedWorldBounds ), $"World bounds, frame {i}: {world} against {model.AnimatedWorldBounds}" );
				Assert.IsTrue( Same( local, model.AnimatedLocalBounds ), $"Local bounds, frame {i}: {local} against {model.AnimatedLocalBounds}" );
			}
		}
	}
}
