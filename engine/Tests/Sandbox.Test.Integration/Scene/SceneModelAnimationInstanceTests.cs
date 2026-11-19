using System;

namespace SceneTests;

#pragma warning disable CS0618

/// <summary>
/// A scene model animated by its own <c>ModelAnimationInstance</c> (<see cref="SceneModel.UsesAnimationInstance"/>) poses,
/// bounds, merges, morphs, attaches and fires events exactly as one animated by its native twin, given the same calls.
/// </summary>
[TestClass]
public class SceneModelAnimationInstanceTests
{
	static Model Human => Model.Load( "models/citizen_human/human.vmdl" );

	static readonly Transform Placement = new( new Vector3( 120, -40, 8 ), Rotation.From( 0, 35, 0 ), 1.25f );

	[TestCleanup]
	public void Cleanup() => SceneModel.AnimationInstanceOverride = null;

	/// <summary>
	/// Play a sequence instead of the animgraph: the human's graph picks idle face expressions at random, so two of them
	/// don't pose alike.
	/// </summary>
	static void PlaySequence( SceneModel model )
	{
		model.UseAnimGraph = false;
		model.CurrentSequence.Name = "Jump_Standing";
		model.CurrentSequence.Looping = true;
	}

	static SceneModel Make( SceneWorld world, Model model, Transform transform, bool instance )
	{
		SceneModel.AnimationInstanceOverride = instance;
		try
		{
			var sceneModel = new SceneModel( world, model, transform );
			Assert.AreEqual( instance, sceneModel.animNative.HasInstance );
			return sceneModel;
		}
		finally
		{
			SceneModel.AnimationInstanceOverride = null;
		}
	}

	static void AssertSamePose( SceneModel twin, SceneModel instance, string step )
	{
		var count = twin.Model.BoneCount;
		Assert.IsTrue( count > 0 );

		var a = new Transform[count];
		var b = new Transform[count];
		twin.GetBoneWorldTransforms( a );
		instance.GetBoneWorldTransforms( b );
		for ( int i = 0; i < count; i++ )
		{
			Assert.IsTrue( a[i].Position.Distance( b[i].Position ) < 1e-3f, $"{step}: bone {i} position {a[i].Position} vs {b[i].Position}" );
			Assert.IsTrue( a[i].Rotation.Distance( b[i].Rotation ) < 0.01f, $"{step}: bone {i} rotation" );
		}

		var ma = new Matrix3x4[count];
		var mb = new Matrix3x4[count];
		twin.GetBoneWorldMatrices( ma );
		instance.GetBoneWorldMatrices( mb );
		for ( int i = 0; i < count; i++ )
			Assert.IsTrue( (ma[i].Row0 - mb[i].Row0).Length + (ma[i].Row1 - mb[i].Row1).Length + (ma[i].Row2 - mb[i].Row2).Length < 1e-3f, $"{step}: bone {i} matrix" );

		AssertSameBox( twin.Bounds, instance.Bounds, $"{step}: scene object bounds" );
		AssertSameBox( twin.animNative.m_worldBounds, instance.animNative.m_worldBounds, $"{step}: world bounds" );
		AssertSameBox( twin.animNative.m_localBounds, instance.animNative.m_localBounds, $"{step}: local bounds" );

		foreach ( var attachment in twin.Model.Attachments.All )
		{
			var ta = twin.GetAttachment( attachment.Name );
			var tb = instance.GetAttachment( attachment.Name );
			Assert.AreEqual( ta.HasValue, tb.HasValue, $"{step}: attachment {attachment.Name}" );
			if ( ta.HasValue ) Assert.IsTrue( ta.Value.Position.Distance( tb.Value.Position ) < 1e-3f, $"{step}: attachment {attachment.Name}" );
		}

		var fa = new float[256];
		var fb = new float[256];
		Assert.AreEqual( twin.GetFlexControllerWeights( fa ), instance.GetFlexControllerWeights( fb ), $"{step}: flex count" );
		for ( int i = 0; i < fa.Length; i++ )
			Assert.AreEqual( fa[i], fb[i], 1e-5f, $"{step}: flex {i}" );

		Assert.IsTrue( twin.RootMotion.Position.Distance( instance.RootMotion.Position ) < 1e-3f, $"{step}: root motion" );
	}

	static void AssertSameBox( BBox a, BBox b, string what )
	{
		Assert.IsTrue( a.Mins.Distance( b.Mins ) < 0.01f && a.Maxs.Distance( b.Maxs ) < 0.01f, $"{what}: {a} vs {b}" );
	}

	/// <summary>
	/// A playing sequence on a moving, scaled object, physics bones, procedural bones and the bind pose.
	/// </summary>
	[TestMethod]
	public void PoseMatchesTwin()
	{
		var world = new SceneWorld();
		try
		{
			var twin = Make( world, Human, Placement, false );
			var instance = Make( world, Human, Placement, true );
			AssertSamePose( twin, instance, "created" );

			foreach ( var model in new[] { twin, instance } )
				PlaySequence( model );

			for ( int frame = 0; frame < 30; frame++ )
			{
				var tx = Placement.WithPosition( Placement.Position + new Vector3( frame * 3, 0, 0 ) );
				foreach ( var model in new[] { twin, instance } )
				{
					model.Transform = tx;
					model.Update( 1.0f / 30.0f );
				}

				AssertSamePose( twin, instance, $"frame {frame}" );
			}

			// Ragdoll: physics bones, then procedural parent-space bones between the update and the bone update
			var hand = Human.Bones.GetBone( "hand_R" ).Index;
			foreach ( var model in new[] { twin, instance } )
			{
				model.SetBoneOverride( hand, new Transform( new Vector3( 10, 20, 30 ), Rotation.From( 10, 20, 30 ) ) );
				model.Update( 1.0f / 30.0f, () => model.SetParentSpaceBone( 0, new Transform( new Vector3( 0, 0, 5 ) ) ) );
			}

			Assert.AreEqual( twin.HasBoneOverrides(), instance.HasBoneOverrides() );
			AssertSamePose( twin, instance, "physics bones" );

			foreach ( var model in new[] { twin, instance } )
			{
				model.ClearBoneOverrides();
				model.UpdateToBindPose();
			}

			AssertSamePose( twin, instance, "bind pose" );

			twin.Delete();
			instance.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A clothing-style bone merge onto an animated parent, and morph overrides fading in.
	/// </summary>
	[TestMethod]
	public void MergeAndMorphsMatchTwin()
	{
		var world = new SceneWorld();
		try
		{
			var parents = new[] { Make( world, Human, Placement, false ), Make( world, Human, Placement, true ) };
			var children = new[] { Make( world, Human, Transform.Zero, false ), Make( world, Human, Transform.Zero, true ) };

			var morph = Human.Morphs.Count > 0 ? Human.Morphs.GetName( 0 ) : null;
			foreach ( var model in parents ) PlaySequence( model );

			for ( int frame = 0; frame < 10; frame++ )
			{
				for ( int i = 0; i < 2; i++ )
				{
					if ( frame == 2 && morph is not null ) parents[i].Morphs.Set( morph, 0.75f );

					parents[i].Update( 1.0f / 30.0f );
					children[i].Transform = parents[i].Transform;
					children[i].MergeBones( parents[i] );
				}

				AssertSamePose( parents[0], parents[1], $"parent frame {frame}" );
				AssertSamePose( children[0], children[1], $"child frame {frame}" );
			}

			foreach ( var model in parents ) model.Delete();
			foreach ( var model in children ) model.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Sequences without the animgraph: the same cycle, the same footstep events, the same pose.
	/// </summary>
	[TestMethod]
	public void SequencesAndEventsMatchTwin()
	{
		var world = new SceneWorld();
		try
		{
			var models = new[] { Make( world, Human, Placement, false ), Make( world, Human, Placement, true ) };
			var footsteps = new int[2];

			for ( int i = 0; i < 2; i++ )
			{
				var index = i;
				PlaySequence( models[i] );
				models[i].CurrentSequence.Looping = false;
				models[i].OnFootstepEvent = _ => footsteps[index]++;
			}

			Assert.AreEqual( models[0].CurrentSequence.Name, models[1].CurrentSequence.Name );
			Assert.AreEqual( models[0].CurrentSequence.Duration, models[1].CurrentSequence.Duration, 1e-5f );

			for ( int frame = 0; frame < 60; frame++ )
			{
				foreach ( var model in models )
				{
					model.Update( 1.0f / 30.0f );
					model.RunPendingEvents();
					model.DispatchTagEvents();
				}

				Assert.AreEqual( models[0].CurrentSequence.Time, models[1].CurrentSequence.Time, 1e-5f, $"frame {frame}" );
				AssertSamePose( models[0], models[1], $"frame {frame}" );
			}

			Assert.IsTrue( footsteps[0] > 0, "the sequence has footsteps" );
			Assert.AreEqual( footsteps[0], footsteps[1] );

			foreach ( var model in models ) model.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A new model starts the instance again as native's model change starts the twin again.
	/// </summary>
	[TestMethod]
	public void ModelChangeMatchesTwin()
	{
		var world = new SceneWorld();
		try
		{
			var models = new[] { Make( world, Model.Load( "models/dev/box.vmdl" ), Placement, false ), Make( world, Model.Load( "models/dev/box.vmdl" ), Placement, true ) };
			AssertSameBox( models[0].Bounds, models[1].Bounds, "boneless" );

			foreach ( var model in models ) model.Model = Human;
			AssertSamePose( models[0], models[1], "changed" );

			foreach ( var model in models ) PlaySequence( model );

			foreach ( var model in models ) model.Update( 1.0f / 30.0f );
			AssertSamePose( models[0], models[1], "updated" );

			foreach ( var model in models ) model.Delete();
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Scene models animate on an instance unless <c>-noaniminstance</c> turns it off.
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
	/// The twin of a scene model on an instance never animates: with nothing reading it, it keeps the bind pose through updates
	/// and a model change, and the bounds are the pose's, kept in managed code.
	/// </summary>
	[TestMethod]
	public void TwinStaysInBindPose()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = false;
		try
		{
			var model = Make( world, Model.Load( "models/dev/box.vmdl" ), Placement, true );
			model.Model = Human;
			PlaySequence( model );

			var hand = Human.Bones.GetBone( "hand_R" ).Index;
			var bind = model.animNative.Twin.GetWorldSpaceRenderBoneTransform( hand );
			var start = model.GetBoneWorldTransform( hand );
			for ( int frame = 0; frame < 10; frame++ ) model.Update( 1.0f / 30.0f );

			Assert.IsTrue( model.GetBoneWorldTransform( hand ).Position.Distance( start.Position ) > 0.1f, "the instance animates" );
			Assert.IsTrue( model.animNative.Twin.GetWorldSpaceRenderBoneTransform( hand ).Position.Distance( bind.Position ) < 1e-4f, "the twin doesn't" );
			Assert.IsFalse( model.animNative.Twin.HasPhysicsBones() );

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


/// <summary>
/// <see cref="Components.SkinnedRendererTest"/> with scene models animated by their native twin (<c>-noaniminstance</c>).
/// </summary>
[TestClass]
public class SkinnedRendererTwinAnimationTest : Components.SkinnedRendererTest
{
	[TestInitialize]
	public void UseTwins() => SceneModel.AnimationInstanceOverride = false;

	[TestCleanup]
	public void Cleanup() => SceneModel.AnimationInstanceOverride = null;
}

/// <summary>
/// <see cref="Components.ModelPhysicsTest"/> (ragdolls, physics bones) with scene models animated by their native twin.
/// </summary>
[TestClass]
public class ModelPhysicsTwinAnimationTest : Components.ModelPhysicsTest
{
	[TestInitialize]
	public void UseTwins() => SceneModel.AnimationInstanceOverride = false;

	[TestCleanup]
	public void Cleanup() => SceneModel.AnimationInstanceOverride = null;
}

/// <summary>
/// <see cref="Components.DresserTests"/> (clothing, bone merge) with scene models animated by their native twin.
/// </summary>
[TestClass]
public class DresserTwinAnimationTests : Components.DresserTests
{
	[TestInitialize]
	public void UseTwins() => SceneModel.AnimationInstanceOverride = false;

	[TestCleanup]
	public void Cleanup() => SceneModel.AnimationInstanceOverride = null;
}

/// <summary>
/// <see cref="ResourceTests.ModelBuilderTests"/> (bounds of a posed runtime model among them) with scene models animated by
/// their native twin.
/// </summary>
[TestClass]
public class ModelBuilderTwinAnimationTests : ResourceTests.ModelBuilderTests
{
	[TestInitialize]
	public void UseTwins() => SceneModel.AnimationInstanceOverride = false;

	[TestCleanup]
	public void Cleanup() => SceneModel.AnimationInstanceOverride = null;
}
