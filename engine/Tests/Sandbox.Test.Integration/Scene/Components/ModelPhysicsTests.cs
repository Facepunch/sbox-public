using System;

namespace SceneTests.Components;

[TestClass]
public class ModelPhysicsTest
{
	private static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	[TestMethod]
	public void ComponentCreation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();

		Assert.IsNotNull( modelPhysics, "ModelPhysics component should be created" );
		Assert.IsTrue( modelPhysics.IsValid(), "ModelPhysics component should be valid" );
		Assert.AreEqual( 0, modelPhysics.Bodies.Count, "Bodies collection should be empty initially" );
		Assert.AreEqual( 0, modelPhysics.Joints.Count, "Joints collection should be empty initially" );
		Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount, "Shouldn't have built anything" );
	}

	[TestMethod]
	public void ModelAssignment_CreatesPhysics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>( false );

		// Assign model
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		// Physics should be created when model is assigned and component is enabled
		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Bodies should be created from model physics" );
		Assert.IsTrue( modelPhysics.Joints.Count > 0, "Joints should be created from model physics" );
		Assert.AreEqual( 1, modelPhysics.PhysicsRebuildCount, "Should have built physics only once" );
	}

	[TestMethod]
	public void RendererIntegration_AutoAssignsModel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();

		// Create renderer first with a model
		var renderer = go.Components.Create<SkinnedModelRenderer>();
		renderer.Model = CitizenModel;

		// Create ModelPhysics - should auto-assign model from renderer
		var modelPhysics = go.Components.Create<ModelPhysics>();

		Assert.AreEqual( CitizenModel, modelPhysics.Model, "Model should be auto-assigned from renderer" );
		Assert.AreEqual( renderer, modelPhysics.Renderer, "Renderer should be auto-assigned" );
	}

	[TestMethod]
	public void RigidbodyFlags_AppliedToAllBodies()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;

		// Set flags before enabling
		var testFlags = RigidbodyFlags.DisableCollisionSounds;
		modelPhysics.RigidbodyFlags = testFlags;
		modelPhysics.Enabled = true;

		// All bodies should have the flags
		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Should have bodies to check the flags on" );
		foreach ( var body in modelPhysics.Bodies )
		{
			Assert.AreEqual( testFlags, body.Component.RigidbodyFlags,
				"All bodies should have the specified RigidbodyFlags" );
		}
	}

	[TestMethod]
	public void Locking_AppliedToAllBodies()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;

		// Set locking before enabling
		var testLocking = new PhysicsLock { Roll = true };
		modelPhysics.Locking = testLocking;
		modelPhysics.Enabled = true;

		// All bodies should have the locking
		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Should have bodies to check the locking on" );
		foreach ( var body in modelPhysics.Bodies )
		{
			Assert.AreEqual( testLocking, body.Component.Locking,
				"All bodies should have the specified locking" );
		}
	}

	[TestMethod]
	public void MotionEnabled_TogglesDynamicPhysics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		// Default should be motion enabled
		Assert.IsTrue( modelPhysics.MotionEnabled, "Motion should be enabled by default" );
		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Should have bodies to toggle motion on" );

		// Disable motion
		modelPhysics.MotionEnabled = false;
		foreach ( var body in modelPhysics.Bodies )
		{
			Assert.IsFalse( body.Component.MotionEnabled,
				"All bodies should have motion disabled" );
		}

		// Re-enable motion
		modelPhysics.MotionEnabled = true;
		foreach ( var body in modelPhysics.Bodies )
		{
			Assert.IsTrue( body.Component.MotionEnabled,
				"All bodies should have motion enabled" );
		}
	}

	[TestMethod]
	public void StartAsleep_PutsBodiesAsleep()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>( false );
		modelPhysics.Model = CitizenModel;
		modelPhysics.StartAsleep = true;
		modelPhysics.Enabled = true;

		// StartAsleep propagates to every Rigidbody at physics creation, so the
		// bodies are asleep without ever ticking the scene
		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Should have bodies to check sleep state on" );
		foreach ( var body in modelPhysics.Bodies )
		{
			Assert.IsTrue( body.Component.Sleeping,
				"All bodies should be asleep when StartAsleep is true" );
		}
	}

	/// <summary>
	/// Disabling the component disables - not destroys - the bodies, joints and
	/// colliders it created, so they can be re-enabled without a physics rebuild.
	/// </summary>
	[TestMethod]
	public void DisablingComponent_DisablesPhysics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;

		// Enable to create physics
		modelPhysics.Enabled = true;
		var initialBodyCount = modelPhysics.Bodies.Count;
		var initialJointCount = modelPhysics.Joints.Count;
		var initialColliderCount = 0;

		foreach ( var collider in go.GetComponentsInChildren<Collider>( true ) )
		{
			if ( !collider.IsValid() ) continue;

			// Collider should be on a physics bone
			Assert.IsTrue( collider.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) );

			initialColliderCount++;
		}

		Assert.IsTrue( initialBodyCount > 0, "Should have bodies when enabled" );
		Assert.IsTrue( initialJointCount > 0, "Should have joints when enabled" );
		Assert.IsTrue( initialColliderCount > 0, "Should have colliders when enabled" );

		// Disable should disable physics components
		modelPhysics.Enabled = false;

		Assert.IsTrue( modelPhysics.Bodies.All( b => !b.Component.Enabled ), "All bodies should be disabled when model physics is disabled" );
		Assert.IsTrue( modelPhysics.Joints.All( j => !j.Component.Enabled ), "All joints should be disabled when model physics is disabled" );
		Assert.IsTrue( go.GetComponentsInChildren<Collider>( true ).All( c => !c.Enabled ), "All colliders should be disabled when model physics is disabled" );
	}

	/// <summary>
	/// Every frame update the root physics body drives the component's transform
	/// (PositionRendererBonesFromPhysics copies the root body's world transform onto
	/// the GameObject). With IgnoreRoot set the transform is left alone.
	/// </summary>
	[TestMethod]
	public void IgnoreRoot_PreventsDrivingTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;
		modelPhysics.IgnoreRoot = true;
		modelPhysics.Enabled = true;

		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Should have bodies to drive the transform" );

		// Teleport the whole ragdoll away from the origin, keeping its pose intact
		// (the bone objects are Absolute, so they don't follow the root GameObject)
		foreach ( var body in modelPhysics.Bodies )
		{
			body.Component.GameObject.WorldPosition += new Vector3( 500, 0, 0 );
		}

		scene.GameTick();

		Assert.IsTrue( go.WorldPosition.Distance( Vector3.Zero ) < 0.001f,
			$"With IgnoreRoot the root body must not drive the transform: {go.WorldPosition}" );

		// Without IgnoreRoot the next update snaps the GameObject to the root body
		modelPhysics.IgnoreRoot = false;
		scene.GameTick();

		Assert.IsTrue( go.WorldPosition.x > 400f,
			$"Without IgnoreRoot the root body should drive the transform: {go.WorldPosition}" );
	}

	/// <summary>
	/// CopyBonesFrom applies the source renderer's world-space bone transforms to the
	/// target's physics bodies: a target ragdoll at the origin snaps to a source
	/// renderer posed far away.
	/// </summary>
	[TestMethod]
	public void CopyBonesFrom_TransfersBoneData()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Create source with renderer, far from the origin
		var sourceGo = scene.CreateObject();
		sourceGo.WorldPosition = new Vector3( 500, 0, 0 );
		var sourceRenderer = sourceGo.Components.Create<SkinnedModelRenderer>();
		sourceRenderer.Model = CitizenModel;

		// Create target with ModelPhysics at the origin
		var targetGo = scene.CreateObject();
		var targetPhysics = targetGo.Components.Create<ModelPhysics>();
		targetPhysics.Model = CitizenModel;
		targetPhysics.Enabled = true;

		// Let the source renderer settle its bone transforms
		scene.GameTick();

		Assert.IsTrue( targetPhysics.Bodies.Count > 0, "Should have bodies to copy bones onto" );
		Assert.IsTrue( targetPhysics.Bodies.All( b => b.Component.GameObject.WorldPosition.x < 100f ),
			"Bodies should start near the origin before the copy" );

		// Copy bones
		targetPhysics.CopyBonesFrom( sourceRenderer, false );

		// Every body should have snapped to the source renderer's bone transforms
		foreach ( var body in targetPhysics.Bodies )
		{
			Assert.IsTrue( body.Component.GameObject.WorldPosition.x > 400f,
				$"Body '{body.Component.GameObject.Name}' should have moved to the source pose: {body.Component.GameObject.WorldPosition}" );
		}
	}

	/// <summary>
	/// UpdateJointScale rescales the joint attachment points from the stored local
	/// frames every fixed update: scaling the body objects measurably moves every
	/// joint anchor to its frame position times the new scale.
	/// </summary>
	[TestMethod]
	public void BodyScaling_UpdatesJointPositions()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		Assert.IsTrue( modelPhysics.Joints.Count > 0, "Should have joints to test scaling" );
		Assert.IsTrue( modelPhysics.Joints.Any( j => j.LocalFrame1.Position.Length > 0.5f ),
			"At least one joint should have an off-origin anchor, or the scale assert below is meaningless" );

		// Scale the bodies themselves - the bone objects are Absolute, so they
		// don't inherit a scale set on the root GameObject
		foreach ( var body in modelPhysics.Bodies )
		{
			body.Component.GameObject.WorldScale = 2.0f;
		}

		// The next fixed update applies the body scale to the joint points
		scene.GameTick();

		foreach ( var joint in modelPhysics.Joints )
		{
			Assert.IsTrue( joint.Component.Point1.LocalPosition.Distance( joint.LocalFrame1.Position * 2.0f ) < 0.01f,
				$"Point1 should scale with the body: {joint.Component.Point1.LocalPosition} vs frame {joint.LocalFrame1.Position}" );
			Assert.IsTrue( joint.Component.Point2.LocalPosition.Distance( joint.LocalFrame2.Position * 2.0f ) < 0.01f,
				$"Point2 should scale with the body: {joint.Component.Point2.LocalPosition} vs frame {joint.LocalFrame2.Position}" );
		}
	}

	[TestMethod]
	public void MultipleEnableDisable_HandlesCorrectly()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>( false );
		modelPhysics.Model = CitizenModel;

		// Enable/Disable multiple times
		for ( int i = 0; i < 3; i++ )
		{
			modelPhysics.Enabled = true;

			Assert.IsTrue( modelPhysics.Bodies.Count > 0, $"Iteration {i}: Should have bodies when enabled" );
			Assert.IsTrue( modelPhysics.Bodies.All( b => b.Component.Enabled ), $"Iteration {i}: All bodies should be enabled when model physics is enabled" );

			modelPhysics.Enabled = false;

			Assert.IsTrue( modelPhysics.Bodies.All( b => !b.Component.Enabled ), $"Iteration {i}: All bodies should be disabled when model physics is disabled" );
		}
	}

	[TestMethod]
	public void NullModel_HandlesGracefully()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();

		// Enable with null model
		modelPhysics.Model = null;
		modelPhysics.Enabled = true;

		// Should handle gracefully without creating bodies
		Assert.AreEqual( 0, modelPhysics.Bodies.Count, "Should have no bodies with null model" );
		Assert.AreEqual( 0, modelPhysics.Joints.Count, "Should have no joints with null model" );
	}

	[TestMethod]
	public void RigidbodyAccess_ValidAfterCreation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		// All body components should be valid and accessible
		Assert.IsTrue( modelPhysics.Bodies.Count > 0, "Should have bodies to check" );
		foreach ( var body in modelPhysics.Bodies )
		{
			Assert.IsTrue( body.Component.IsValid(), "Body component should be valid" );
			Assert.IsTrue( body.Component.GameObject.IsValid(), "Body GameObject should be valid" );
			Assert.IsTrue( body.Bone >= 0, "Body should have valid bone index" );
		}
	}

	[TestMethod]
	public void JointConfiguration_PreservesSettings()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		// Joints should maintain their configuration
		Assert.IsTrue( modelPhysics.Joints.Count > 0, "Should have joints to check" );
		foreach ( var joint in modelPhysics.Joints )
		{
			Assert.IsTrue( joint.Component.IsValid(), "Joint component should be valid" );
			Assert.IsTrue( joint.Body1.Component.IsValid(), "Joint Body1 should be valid" );
			Assert.IsTrue( joint.Body2.Component.IsValid(), "Joint Body2 should be valid" );
			Assert.AreEqual( Sandbox.Joint.AttachmentMode.LocalFrames, joint.Component.Attachment,
				"Joints should use LocalFrames attachment mode" );
		}
	}

	[TestMethod]
	public void CreatesHeadBoneWithRigidbody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();

		// Assign the citizen model which should have a head bone
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		// Find the head child GameObject
		GameObject headObject = go.GetComponentsInChildren<Rigidbody>().Where( x => x.GameObject.Name == "head" ).Single().GameObject;

		// Verify head GameObject exists
		Assert.IsNotNull( headObject, "A child GameObject named 'head' should be created" );
		Assert.IsTrue( headObject.IsValid(), "Head GameObject should be valid" );

		// Verify head has a Rigidbody component
		var headRigidbody = headObject.GetComponent<Rigidbody>();
		Assert.IsNotNull( headRigidbody, "Head GameObject should have a Rigidbody component" );
		Assert.IsTrue( headRigidbody.IsValid(), "Head Rigidbody should be valid" );

		// Additional verification - check it's in the Bodies list
		var headBody = modelPhysics.Bodies.FirstOrDefault( b => b.Component.GameObject == headObject );
		Assert.AreEqual( headRigidbody, headBody.Component, "Rigidbody in Bodies should match head's Rigidbody" );

		// Verify the GameObject has the physics bone flags set
		Assert.IsTrue( headObject.Flags.HasFlag( GameObjectFlags.Absolute ), "Head GameObject should have Absolute flag" );
		Assert.IsTrue( headObject.Flags.HasFlag( GameObjectFlags.PhysicsBone ), "Head GameObject should have PhysicsBone flag" );
	}

	[TestMethod]
	public void CreatesNeck0BoneWithoutRigidbody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();
		var modelPhysics = go.Components.Create<ModelPhysics>();

		// Assign the citizen model which should have a neck_0 bone
		modelPhysics.Model = CitizenModel;
		modelPhysics.Enabled = true;

		// Find the neck_0 child GameObject
		GameObject neckObject = go.GetAllObjects( true ).Where( x => x.Name == "neck_0" ).FirstOrDefault();

		// Verify neck_0 GameObject exists (bone objects are created for all bones)
		Assert.IsNotNull( neckObject, "A child GameObject named 'neck_0' should be created" );
		Assert.IsTrue( neckObject.IsValid(), "Neck_0 GameObject should be valid" );

		// Verify neck_0 does NOT have a Rigidbody component
		var neckRigidbody = neckObject.GetComponent<Rigidbody>();
		Assert.IsNull( neckRigidbody, "Neck_0 GameObject should NOT have a Rigidbody component" );

		// Verify it's not in the Bodies list
		var neckBody = modelPhysics.Bodies.FirstOrDefault( b => b.Component.GameObject == neckObject );
		Assert.IsNull( neckBody.Component, "Neck_0 should NOT be in the Bodies collection" );

		// Verify the GameObject doesn't have the physics bone flags
		// (these are only set when physics components are added)
		Assert.IsFalse( neckObject.Flags.HasFlag( GameObjectFlags.PhysicsBone ), "Neck_0 GameObject should NOT have PhysicsBone flag since it has no physics" );
	}

	[TestMethod]
	public void ClonedObjectKeepsFlags_Clone()
	{
		GameObject sourceGo = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var modelPhysics = go.Components.Create<ModelPhysics>();
			modelPhysics.Model = CitizenModel;

			sourceGo = go;
		}

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var go = sourceGo.Clone( new CloneConfig { StartEnabled = true } );
			var modelPhysics = go.GetComponent<ModelPhysics>();

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );
			Assert.AreNotEqual( 0, modelPhysics.Bodies.Count );
			Assert.AreNotEqual( 0, modelPhysics.Joints.Count );
			Assert.AreNotEqual( 0, go.GetComponentsInChildren<Rigidbody>().Count() );

			foreach ( var part in go.GetComponentsInChildren<Rigidbody>() )
			{
				Assert.IsTrue( part.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) );
				Assert.IsTrue( part.GameObject.Flags.Contains( GameObjectFlags.Bone ) );
				Console.WriteLine( part.GameObject.Flags );
			}

		}
		scene.Destroy();

		sourceScene.Destroy();
	}

	[TestMethod]
	public void ClonedObjectKeepsFlags_Json()
	{
		string json = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var modelPhysics = go.Components.Create<ModelPhysics>();
			modelPhysics.Model = CitizenModel;

			json = go.Serialize().ToJsonString();
		}

		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var go = new GameObject( false );
			go.Deserialize( jsonObject );
			go.Enabled = true;

			var modelPhysics = go.GetComponent<ModelPhysics>();

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );
			Assert.AreNotEqual( 0, modelPhysics.Bodies.Count );
			Assert.AreNotEqual( 0, modelPhysics.Joints.Count );
			Assert.AreNotEqual( 0, go.GetComponentsInChildren<Rigidbody>().Count() );

			foreach ( var part in go.GetComponentsInChildren<Rigidbody>() )
			{
				Assert.IsTrue( part.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) );
				Console.WriteLine( part.GameObject.Flags );
			}

		}
		scene.Destroy();

		sourceScene.Destroy();
	}

	[TestMethod]
	public void ClonedObjectKeepsPositions_Clone()
	{
		GameObject sourceGo = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var modelPhysics = go.Components.Create<ModelPhysics>();
			modelPhysics.Model = CitizenModel;

			sourceGo = go;
		}

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var targetPos = new Vector3( 10000, 0, 0 );

			// spawn it a bit away
			var go = sourceGo.Clone( new CloneConfig { StartEnabled = true, Transform = new Transform( targetPos ) } );
			var modelPhysics = go.GetComponent<ModelPhysics>();

			System.Console.WriteLine( go.WorldTransform );

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );

			Assert.IsTrue( go.IsValid(), "Cloned GameObject should be valid" );
			Assert.IsTrue( go.WorldPosition.Distance( targetPos ) < 1.0f, "Cloned GameObject should be near the target position" );

			Assert.IsTrue( go.GetAllObjects( true ).Count() > 50, "Should have more than 50 child objects" );

			var furthestObject = go.GetAllObjects( true ).Max( x => x.WorldPosition.Distance( targetPos ) );
			Assert.IsTrue( furthestObject < 200.0f, $"Furthest object was {furthestObject:n0} away!" );
		}

		scene.Destroy();

		sourceScene.Destroy();
	}

	[TestMethod]
	public void ClonedObjectKeepsPositions_Json()
	{
		string json = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var modelPhysics = go.Components.Create<ModelPhysics>();
			modelPhysics.Model = CitizenModel;

			json = go.Serialize().ToJsonString();
		}

		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var targetPos = new Vector3( 10000, 0, 0 );

			var go = new GameObject( false );
			go.Deserialize( jsonObject, new GameObject.DeserializeOptions { TransformOverride = new Transform( targetPos ) } );
			go.Enabled = true;

			// spawn it a bit away
			var modelPhysics = go.GetComponent<ModelPhysics>();

			System.Console.WriteLine( go.WorldTransform );

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );

			Assert.IsTrue( go.IsValid(), "Cloned GameObject should be valid" );
			Assert.IsTrue( go.WorldPosition.Distance( targetPos ) < 1.0f, "Cloned GameObject should be near the target position" );

			Assert.IsTrue( go.GetAllObjects( true ).Count() > 50, "Should have more than 50 child objects" );

			var furthestObject = go.GetAllObjects( true ).Max( x => x.WorldPosition.Distance( targetPos ) );
			Assert.IsTrue( furthestObject < 200.0f, $"Furthest object was {furthestObject:n0} away!" );
		}

		scene.Destroy();

		sourceScene.Destroy();
	}

	[TestMethod]
	public void ClonedObjectKeepsPositions_Json_Update()
	{
		string json = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var modelRender = go.Components.Create<SkinnedModelRenderer>();
			modelRender.Model = CitizenModel;

			var modelPhysics = go.Components.Create<ModelPhysics>();
			modelPhysics.Model = CitizenModel;

			for ( int i = 0; i < 100; i++ )
			{
				sourceScene.GameTick( 0.1f );
			}

			json = go.Serialize().ToJsonString();
		}

		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var targetPos = new Vector3( 10000, 0, 0 );

			var go = new GameObject( false );
			go.Deserialize( jsonObject, new GameObject.DeserializeOptions { TransformOverride = new Transform( targetPos ) } );
			go.Enabled = true;

			scene.GameTick( 0.1f );

			// spawn it a bit away
			var modelPhysics = go.GetComponent<ModelPhysics>();

			System.Console.WriteLine( go.WorldTransform );

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );

			Assert.IsTrue( go.IsValid(), "Cloned GameObject should be valid" );
			Assert.IsTrue( go.WorldPosition.Distance( targetPos ) < 1.0f, "Cloned GameObject should be near the target position" );

			Assert.IsTrue( go.GetAllObjects( true ).Count() > 50, "Should have more than 50 child objects" );

			var furthestObject = go.GetAllObjects( true ).Max( x => x.WorldPosition.Distance( targetPos ) );
			Assert.IsTrue( furthestObject < 200.0f, $"Furthest object was {furthestObject:n0} away!" );
		}

		scene.Destroy();

		sourceScene.Destroy();
	}

	[TestMethod]
	public void ClonedObjectKeepsPositions_Json_Batch()
	{
		string json = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var modelPhysics = go.Components.Create<ModelPhysics>();
			modelPhysics.Model = CitizenModel;

			json = go.Serialize().ToJsonString();
		}

		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var targetPos = new Vector3( 10000, 0, 0 );

			GameObject go = default;

			using ( scene.BatchGroup() )
			{
				go = new GameObject( false );
				go.Deserialize( jsonObject, new GameObject.DeserializeOptions { TransformOverride = new Transform( targetPos ) } );
				go.Enabled = true;
			}

			// spawn it a bit away
			var modelPhysics = go.GetComponent<ModelPhysics>();

			System.Console.WriteLine( go.WorldTransform );

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );

			Assert.IsTrue( go.IsValid(), "Cloned GameObject should be valid" );
			Assert.IsTrue( go.WorldPosition.Distance( targetPos ) < 1.0f, "Cloned GameObject should be near the target position" );

			Assert.IsTrue( go.GetAllObjects( true ).Count() > 50, "Should have more than 50 child objects" );

			var furthestObject = go.GetAllObjects( true ).Max( x => x.WorldPosition.Distance( targetPos ) );
			Assert.IsTrue( furthestObject < 200.0f, $"Furthest object was {furthestObject:n0} away!" );
		}

		scene.Destroy();

		sourceScene.Destroy();
	}

	[TestMethod]
	public void ClonedObjectKeepsPositions_Json_Batch_Prop()
	{
		string json = default;

		var sourceScene = new Scene();
		using ( var sceneScope = sourceScene.Push() )
		{
			var go = sourceScene.CreateObject();
			var prop = go.Components.Create<Prop>();
			prop.Model = CitizenModel;

			json = go.Serialize().ToJsonString();
		}

		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var scene = new Scene();
		using ( var sceneScope = scene.Push() )
		{
			var targetPos = new Vector3( 10000, 0, 0 );

			GameObject go = default;

			using ( scene.BatchGroup() )
			{
				go = new GameObject( false );
				go.Deserialize( jsonObject, new GameObject.DeserializeOptions { TransformOverride = new Transform( targetPos ) } );
				go.Enabled = true;
			}

			// spawn it a bit away
			var modelPhysics = go.GetComponent<ModelPhysics>();

			System.Console.WriteLine( go.WorldTransform );

			Assert.IsNotNull( modelPhysics, "Cloned GameObject should have ModelPhysics component" );
			Assert.AreEqual( 0, modelPhysics.PhysicsRebuildCount );

			Assert.IsTrue( go.IsValid(), "Cloned GameObject should be valid" );
			Assert.IsTrue( go.WorldPosition.Distance( targetPos ) < 1.0f, "Cloned GameObject should be near the target position" );

			Assert.IsTrue( go.GetAllObjects( true ).Count() > 50, "Should have more than 50 child objects" );

			var furthestObject = go.GetAllObjects( true ).Max( x => x.WorldPosition.Distance( targetPos ) );
			Assert.IsTrue( furthestObject < 200.0f, $"Furthest object was {furthestObject:n0} away!" );
		}

		scene.Destroy();

		sourceScene.Destroy();
	}
}
