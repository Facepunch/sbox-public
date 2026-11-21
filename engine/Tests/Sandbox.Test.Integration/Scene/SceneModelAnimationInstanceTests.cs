using System;

namespace SceneTests;

#pragma warning disable CS0618

/// <summary>
/// A scene model managed code makes animates on its own <c>ModelAnimationInstance</c>; its native twin keeps draw state only.
/// </summary>
[TestClass]
public class SceneModelAnimationInstanceTests
{
	static Model Human => Model.Load( "models/citizen_human/human.vmdl" );

	static readonly Transform Placement = new( new Vector3( 120, -40, 8 ), Rotation.From( 0, 35, 0 ), 1.25f );

	/// <summary>
	/// Play a sequence instead of the animgraph.
	/// </summary>
	static void PlaySequence( SceneModel model )
	{
		model.UseAnimGraph = false;
		model.CurrentSequence.Name = "Jump_Standing";
		model.CurrentSequence.Looping = true;
	}

	static void AssertSameBox( BBox a, BBox b, string what )
	{
		Assert.IsTrue( a.Mins.Distance( b.Mins ) < 0.01f && a.Maxs.Distance( b.Maxs ) < 0.01f, $"{what}: {a} vs {b}" );
	}

	/// <summary>
	/// Every scene model managed code makes animates on an instance of its own.
	/// </summary>
	[TestMethod]
	public void InstanceIsTheDefault()
	{
		var world = new SceneWorld();
		try
		{
			var model = new SceneModel( world, Human, Placement );
			Assert.IsTrue( model.animNative.HasInstance );
			model.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// The twin of a scene model on an instance never animates and is never given the pose, even where scene objects write
	/// their native twins: it keeps the bind pose through updates and a model change, and the bounds are the pose's, kept in
	/// managed code.
	/// </summary>
	[TestMethod]
	public void TwinStaysInBindPose()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = true;
		try
		{
			var model = new SceneModel( world, Model.Load( "models/dev/box.vmdl" ), Placement );
			model.Model = Human;
			PlaySequence( model );

			var hand = Human.Bones.GetBone( "hand_R" ).Index;
			var bind = model.animNative.Twin.GetWorldSpaceRenderBoneTransform( hand );
			var start = model.GetBoneWorldTransform( hand );
			for ( int frame = 0; frame < 10; frame++ ) model.Update( 1.0f / 30.0f );

			Assert.IsTrue( model.GetBoneWorldTransform( hand ).Position.Distance( start.Position ) > 0.1f, "the instance animates" );
			Assert.IsTrue( model.animNative.Twin.GetWorldSpaceRenderBoneTransform( hand ).Position.Distance( bind.Position ) < 1e-4f, "the twin doesn't" );

			var pose = model.AnimatedWorldBounds;
			var quantised = new BBox( new Vector3( MathF.Floor( pose.Mins.x ), MathF.Floor( pose.Mins.y ), MathF.Floor( pose.Mins.z ) ),
				new Vector3( MathF.Ceiling( pose.Maxs.x ), MathF.Ceiling( pose.Maxs.y ), MathF.Ceiling( pose.Maxs.z ) ) );
			AssertSameBox( quantised, model.Bounds, "kept bounds" );

			model.Delete();
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}
}
