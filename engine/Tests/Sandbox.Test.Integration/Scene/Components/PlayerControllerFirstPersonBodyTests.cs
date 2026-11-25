using System;
using System.Collections.Generic;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	const string FirstPersonHumanModel = "models/citizen_human/citizen_human_male.vmdl";
	const string FirstPersonShoesModel = "models/citizen_clothes/shoes/football_shoes/models/football_shoes_m_human.vmdl";

	/// <summary>
	/// A first person player whose body stays visible (the first person body): a walking human with shoes bone-merged onto
	/// it, on a floor, with a main camera.
	/// </summary>
	static (PlayerController Controller, SkinnedModelRenderer Body, SkinnedModelRenderer Shoes) CreateFirstPersonBodyPlayer( Scene scene )
	{
		scene.CreateObject().Components.Create<CameraComponent>();
		scene.UpdateMainCamera();
		CreateFloor( scene );

		var pc = CreatePlayer( scene, Vector3.Zero );
		pc.ThirdPerson = false;
		pc.HideBodyInFirstPerson = false;

		var body = new GameObject( pc.GameObject, true, "Body" ).Components.Create<SkinnedModelRenderer>();
		body.Model = Model.Load( FirstPersonHumanModel );
		body.UseAnimGraph = true;
		pc.Renderer = body;

		var shoes = new GameObject( body.GameObject, true, "Shoes" ).Components.Create<SkinnedModelRenderer>();
		shoes.Model = Model.Load( FirstPersonShoesModel );
		shoes.BoneMergeTarget = body;

		return (pc, body, shoes);
	}

	/// <summary>
	/// One frame as the game runs it: update, bones, then pre-render before the frame finishes (a headless tick has no
	/// pre-render of its own). <paramref name="afterPreRender"/> runs straight after it, still in the frame.
	/// </summary>
	static void FirstPersonFrame( Scene scene, Action afterPreRender = null )
	{
		using var hook = scene.AddHook( GameObjectSystem.Stage.FinishUpdate, int.MinValue, () =>
		{
			scene.PreRender();
			afterPreRender?.Invoke();
		}, nameof( PlayerControllerTest ), "PreRender" );
		scene.GameTick( 1.0 / 60 );
	}

	/// <summary>
	/// The first person copy of a renderer: what the viewer sees in its place.
	/// </summary>
	static SceneModel FirstPersonCopyOf( Scene scene, ModelRenderer renderer )
	{
		foreach ( var so in scene.SceneWorld.SceneObjects )
		{
			if ( so.Component == renderer && so != renderer.SceneObject && so.Tags.Has( "firstperson" ) )
				return so as SceneModel;
		}
		return null;
	}

	static Transform[] BonesOf( SceneModel model )
	{
		var bones = new Transform[model.Model.BoneCount];
		model.GetBoneWorldTransforms( bones );
		return bones;
	}

	static bool IsCollapsed( Transform bone ) => bone.Scale.Length < 1e-4f;

	static void AssertSameBone( Transform expected, Transform actual, string what )
	{
		Assert.IsTrue( expected.Position.Distance( actual.Position ) < 0.01f, $"{what}: position {actual.Position}, expected {expected.Position}" );
		Assert.IsTrue( expected.Rotation.Distance( actual.Rotation ) < 0.5f, $"{what}: rotation {actual.Rotation}, expected {expected.Rotation}" );
	}

	/// <summary>
	/// The first person body shows the pose of its own graph instance, given the body's parameters and b_firstperson, with
	/// the upper body collapsed; after its first frame that instance is evaluated with the animation pass. Clothing merged
	/// onto the body takes the same pose. Checked frame by frame while the player stands, walks and crouches.
	/// </summary>
	[TestMethod]
	public void FirstPersonBodyShowsItsGraphPoseOnBodyAndClothing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, body, shoes) = CreateFirstPersonBodyPlayer( scene );
		var model = body.Model;
		var neck = model.Bones.GetBone( "neck_0" );
		var head = model.Bones.GetBone( "head" );
		var ankle = model.Bones.GetBone( "ankle_R" );
		var shoeAnkle = shoes.Model.Bones.GetBone( "ankle_R" );
		Assert.IsNotNull( neck );
		Assert.IsNotNull( ankle );
		Assert.IsNotNull( shoeAnkle );

		var posed = 0;
		for ( int frame = 0; frame < 40; frame++ )
		{
			pc.WishVelocity = frame > 5 && frame < 30 ? Vector3.Forward * 150 : Vector3.Zero;
			if ( frame == 32 ) pc.UpdateDucking( true );

			Transform[] bones = null, own = null, shoeBones = null;
			FirstPersonFrame( scene, () =>
			{
				bones = FirstPersonCopyOf( scene, body ) is { } copy ? BonesOf( copy ) : null;
				shoeBones = FirstPersonCopyOf( scene, shoes ) is { } shoesCopy ? BonesOf( shoesCopy ) : null;
				own = BonesOf( body.SceneModel );
			} );

			Assert.IsNotNull( bones, "The body has a first person copy" );
			Assert.IsNotNull( shoeBones, "Merged clothing has a first person copy" );
			Assert.IsTrue( IsCollapsed( bones[neck.Index] ) && IsCollapsed( bones[head.Index] ), $"frame {frame}: the head is collapsed" );
			Assert.IsFalse( IsCollapsed( bones[ankle.Index] ), $"frame {frame}: the legs are shown" );
			AssertSameBone( bones[ankle.Index], shoeBones[shoeAnkle.Index], $"frame {frame}, the shoe follows the first person body" );

			// b_firstperson reshapes the pose: somewhere it isn't the body's own
			for ( int i = 0; i < bones.Length; i++ )
			{
				if ( IsCollapsed( bones[i] ) || bones[i].Position.Distance( own[i].Position ) < 0.1f ) continue;
				posed++;
				break;
			}
		}

		Assert.IsTrue( posed > 30, $"The first person graph posed the body ({posed} of 40 frames)" );
		Assert.IsTrue( pc.WorldPosition.Distance( Vector3.Zero ) > 20, "The player walked" );
	}

	/// <summary>
	/// Without the first person animation (seated, or turned off) the first person body keeps the body's own pose.
	/// </summary>
	[TestMethod]
	public void FirstPersonBodyWithoutFirstPersonAnimationKeepsTheBodyPose()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, body, _) = CreateFirstPersonBodyPlayer( scene );
		pc.UseFirstPersonAnimation = false;
		for ( int frame = 0; frame < 10; frame++ )
		{
			pc.WishVelocity = Vector3.Forward * 150;
			Transform[] bones = null, expected = null;
			FirstPersonFrame( scene, () =>
			{
				bones = BonesOf( FirstPersonCopyOf( scene, body ) );
				expected = BonesOf( body.SceneModel );
			} );
			for ( int i = 0; i < bones.Length; i++ )
			{
				if ( IsCollapsed( bones[i] ) ) continue;
				AssertSameBone( expected[i], bones[i], $"frame {frame}, bone {body.Model.Bones.AllBones[i].Name}" );
			}
		}
	}

	/// <summary>
	/// Disabling the controller takes the first person body away and stops its evaluation; enabling it brings it back.
	/// </summary>
	[TestMethod]
	public void FirstPersonBodyStopsWhenDisabledAndReturns()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, body, shoes) = CreateFirstPersonBodyPlayer( scene );
		for ( int frame = 0; frame < 3; frame++ )
			FirstPersonFrame( scene );
		Assert.IsNotNull( FirstPersonCopyOf( scene, body ) );

		pc.Enabled = false;
		Assert.IsNull( FirstPersonCopyOf( scene, body ), "Disabled, the copies are gone" );
		Assert.IsNull( FirstPersonCopyOf( scene, shoes ) );
		for ( int frame = 0; frame < 3; frame++ )
			FirstPersonFrame( scene );
		Assert.IsNull( FirstPersonCopyOf( scene, body ) );

		pc.Enabled = true;
		for ( int frame = 0; frame < 3; frame++ )
			FirstPersonFrame( scene );
		Assert.IsNotNull( FirstPersonCopyOf( scene, body ), "Enabled again, the first person body is back" );
		Assert.IsNotNull( FirstPersonCopyOf( scene, shoes ) );

		pc.ThirdPerson = true;
		FirstPersonFrame( scene );
		Assert.IsNull( FirstPersonCopyOf( scene, body ), "Third person has no first person body" );
	}

	/// <summary>
	/// Once steady, the first person body's pre-render allocates nothing: no searches into new lists, and tags only rewritten
	/// when they change.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void FirstPersonBodyPreRenderAllocatesNothing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (pc, _, _) = CreateFirstPersonBodyPlayer( scene );
		for ( int frame = 0; frame < 3; frame++ )
			FirstPersonFrame( scene );

		Allocations.AssertSettles( "The first person body's pre-render", () =>
		{
			using var time = Time.PushScope( scene.TimeNow, scene.TimeDelta );
			pc.OnPreRenderInternal();
		}, beforePass: () => scene.GameTick( 1.0 / 60 ) );
	}
}
