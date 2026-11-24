using System;
using System.Collections.Generic;

namespace SceneTests.Components;

#pragma warning disable CS0618

/// <summary>
/// The animation pass spreads independent models over the job dispatcher's threads. Whatever thread runs a model, and in
/// whatever order, the poses, bone objects and attachments it ends up with are the same as one thread running them in turn.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneAnimationParallelTests
{
	static Model HumanModel => Model.Load( "models/citizen_human/human.vmdl" );

	/// <summary>
	/// A crowd of walking humans, each with a bone-merged copy (clothing), some with bone objects and a second skinned model
	/// held in a hand bone (a hierarchy child, animated after its parent), others with a skinned child on the root object.
	/// </summary>
	static List<SkinnedModelRenderer> BuildCrowd( Scene scene, int count )
	{
		var renderers = new List<SkinnedModelRenderer>();

		for ( int i = 0; i < count; i++ )
		{
			var go = scene.CreateObject();
			go.Name = $"citizen {i}";
			go.WorldPosition = new Vector3( (i % 6) * 80, (i / 6) * 80, 0 );
			go.WorldRotation = Rotation.FromYaw( i * 23 );

			var body = go.Components.Create<SkinnedModelRenderer>( false );
			body.Model = HumanModel;
			body.CreateBoneObjects = i % 3 == 0;
			body.Enabled = true;

			var speed = 30 + (i * 37) % 170;
			body.Set( "move_x", speed );
			body.Set( "wish_x", speed );
			body.Set( "move_groundspeed", speed );
			body.Set( "b_grounded", true );
			body.Set( "duck", (i % 4) * 0.25f );
			renderers.Add( body );

			var clothingGo = scene.CreateObject();
			clothingGo.SetParent( go, false );
			var clothing = clothingGo.Components.Create<SkinnedModelRenderer>( false );
			clothing.Model = HumanModel;
			clothing.BoneMergeTarget = body;
			clothing.Enabled = true;
			renderers.Add( clothing );

			// Clothing on an object of its own (a root of its own, merged onto the body), with something merged onto it
			var looseGo = scene.CreateObject();
			looseGo.Name = $"loose clothing {i}";
			var loose = looseGo.Components.Create<SkinnedModelRenderer>( false );
			loose.Model = HumanModel;
			loose.CreateBoneObjects = i % 4 == 1;
			loose.BoneMergeTarget = body;
			loose.Enabled = true;
			renderers.Add( loose );

			var layerGo = scene.CreateObject();
			layerGo.SetParent( looseGo, false );
			var layer = layerGo.Components.Create<SkinnedModelRenderer>( false );
			layer.Model = HumanModel;
			layer.BoneMergeTarget = loose;
			layer.Enabled = true;
			renderers.Add( layer );

			// Every fifth one wears something that sits in the previous citizen's hierarchy: a merge across two of them
			if ( i % 5 == 4 )
			{
				var across = renderers[renderers.Count - 9].GameObject;
				var strayGo = scene.CreateObject();
				strayGo.SetParent( across, false );
				var stray = strayGo.Components.Create<SkinnedModelRenderer>( false );
				stray.Model = HumanModel;
				stray.BoneMergeTarget = body;
				stray.Enabled = true;
				renderers.Add( stray );
			}

			var holder = body.CreateBoneObjects ? body.GetBoneObject( "hand_R" ) : go;
			Assert.IsNotNull( holder, "The human's hand bone object should exist" );

			var heldGo = scene.CreateObject();
			heldGo.SetParent( holder, false );
			heldGo.LocalPosition = new Vector3( 4, 0, 0 );
			var held = heldGo.Components.Create<SkinnedModelRenderer>( false );
			held.Model = HumanModel;
			held.CreateBoneObjects = i % 2 == 0;
			held.Enabled = true;
			held.Set( "move_x", 200 - speed );
			held.Set( "b_grounded", i % 2 == 0 );
			renderers.Add( held );
		}

		return renderers;
	}

	/// <summary>
	/// Every renderer's world bones, its scene model's transform and every object under the crowd, after some frames.
	/// </summary>
	static List<Transform> RunCrowd( int parallelism, int frames )
	{
		var previous = JobDispatcher.MaxDegreeOfParallelism;
		JobDispatcher.MaxDegreeOfParallelism = parallelism;

		var scene = new Scene();
		try
		{
			using var sceneScope = scene.Push();
			var renderers = BuildCrowd( scene, 24 );

			for ( int i = 0; i < frames; i++ )
				scene.GameTick( 1.0 / 30.0 );

			var result = new List<Transform>();
			foreach ( var renderer in renderers )
			{
				result.Add( renderer.SceneModel.Transform );
				result.AddRange( renderer.GetBoneTransforms( true ) );
			}

			foreach ( var go in scene.GetAllObjects( true ) )
				result.Add( go.WorldTransform );

			return result;
		}
		finally
		{
			scene.Destroy();
			JobDispatcher.MaxDegreeOfParallelism = previous;
		}
	}

	// Bit for bit: Vector3's and Rotation's == allow a little difference
	static bool Same( Vector3 a, Vector3 b ) => a.x.Equals( b.x ) && a.y.Equals( b.y ) && a.z.Equals( b.z );
	static bool Same( in Transform a, in Transform b ) => Same( a.Position, b.Position ) && Same( a.Scale, b.Scale )
		&& a.Rotation.x.Equals( b.Rotation.x ) && a.Rotation.y.Equals( b.Rotation.y ) && a.Rotation.z.Equals( b.Rotation.z ) && a.Rotation.w.Equals( b.Rotation.w );

	/// <summary>
	/// The same crowd animated on one thread and on all of them comes out bit for bit the same.
	/// </summary>
	[TestMethod]
	public void ParallelPosesMatchSerial()
	{
		if ( JobDispatcher.Workers.Count == 0 ) Assert.Inconclusive( "No workers to run in parallel on." );

		var serial = RunCrowd( 1, 20 );
		var parallel = RunCrowd( JobDispatcher.Workers.Count + 1, 20 );

		Assert.AreEqual( serial.Count, parallel.Count, "The same objects and bones" );

		var moved = false;
		for ( int i = 0; i < serial.Count; i++ )
		{
			var a = serial[i];
			var b = parallel[i];
			Assert.IsTrue( Same( a, b ), $"Transform {i} differs: serial {a}, parallel {b}" );
		}

		var again = RunCrowd( 1, 21 );
		for ( int i = 0; i < serial.Count && !moved; i++ )
			moved = serial[i].Position.Distance( again[i].Position ) > 0.001f;

		Assert.IsTrue( moved, "The crowd should be animating" );
	}
}
