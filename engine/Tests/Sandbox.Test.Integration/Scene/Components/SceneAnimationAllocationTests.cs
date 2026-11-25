namespace SceneTests.Components;

#pragma warning disable CS0618

/// <summary>
/// The animation pass runs every frame for every skinned model, so once warm it allocates nothing.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneAnimationAllocationTests
{
	/// <summary>
	/// A frame of the animation pass over walking humans, with bone objects, bone-merged clothing and a model held in a
	/// hand: graphs, merges, bone objects and each pose's hand-off. On one thread, so what's counted is the pass's own.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void AnimationPassDoesNotAllocateWhenWarm()
	{
		var previous = JobDispatcher.MaxDegreeOfParallelism;
		JobDispatcher.MaxDegreeOfParallelism = 1;

		var scene = new Scene();
		try
		{
			using var sceneScope = scene.Push();
			var human = Model.Load( "models/citizen_human/human.vmdl" );

			for ( int i = 0; i < 4; i++ )
			{
				var go = scene.CreateObject();
				go.WorldPosition = new Vector3( i * 80, 0, 0 );

				var body = go.Components.Create<SkinnedModelRenderer>( false );
				body.Model = human;
				body.CreateBoneObjects = i % 2 == 0;
				body.Enabled = true;
				body.Set( "move_x", 50 + i * 40 );
				body.Set( "b_grounded", true );

				var clothing = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
				clothing.Model = human;
				clothing.BoneMergeTarget = body;
				clothing.Enabled = true;

				var heldGo = scene.CreateObject();
				heldGo.SetParent( body.CreateBoneObjects ? body.GetBoneObject( "hand_R" ) : go, false );
				var held = heldGo.Components.Create<SkinnedModelRenderer>( false );
				held.Model = human;
				held.Enabled = true;
			}

			for ( int i = 0; i < 10; i++ )
				scene.GameTick( 1.0 / 30.0 );

			var animation = scene.GetSystem<SceneAnimationSystem>();
			using var time = Time.PushScope( 1, 1.0 / 30.0 );
			Allocations.AssertSettles( "the animation pass", animation.UpdateAnimation );
		}
		finally
		{
			scene.Destroy();
			JobDispatcher.MaxDegreeOfParallelism = previous;
		}
	}
}
