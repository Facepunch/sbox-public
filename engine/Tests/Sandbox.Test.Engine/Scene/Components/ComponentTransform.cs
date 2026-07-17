namespace SceneTests.Components;

/// <summary>
/// Tests for the transform convenience accessors on <see cref="Component"/>
/// (Component.LocalTransform.cs / Component.WorldTransform.cs). These are pure
/// pass-throughs to the owning GameObject's transform, so writing through the
/// component must be visible on the GameObject and vice versa.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentTransformTest : SceneTest
{
	/// <summary>
	/// The component's local transform accessors read and write the owning
	/// GameObject's local transform. Setting a single channel (position, rotation
	/// or scale) must leave the other channels untouched.
	/// </summary>
	[TestMethod]
	public void LocalAccessorsReadAndWriteGameObjectTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<TransformProbeComponent>();

		comp.LocalTransform = Transform.Zero;
		Assert.AreEqual( Transform.Zero, go.LocalTransform );
		Assert.AreEqual( Transform.Zero, comp.LocalTransform );

		comp.LocalPosition = new Vector3( 10, 20, 30 );
		Assert.AreEqual( new Vector3( 10, 20, 30 ), comp.LocalPosition );
		Assert.AreEqual( new Vector3( 10, 20, 30 ), go.LocalPosition );

		comp.LocalRotation = Rotation.FromYaw( 90 );
		Assert.AreEqual( Rotation.FromYaw( 90 ), go.LocalRotation );

		comp.LocalScale = new Vector3( 2, 2, 2 );
		Assert.AreEqual( new Vector3( 2, 2, 2 ), go.LocalScale );

		// setting scale must not have disturbed position or rotation
		Assert.AreEqual( new Vector3( 10, 20, 30 ), comp.LocalPosition );
		Assert.AreEqual( Rotation.FromYaw( 90 ), comp.LocalRotation );

		// and writes on the GameObject are visible through the component
		go.LocalPosition = new Vector3( 1, 2, 3 );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), comp.LocalPosition );
	}

	/// <summary>
	/// WorldPosition on a component reflects the parent chain, and writing it
	/// computes the correct local position relative to the parent.
	/// </summary>
	[TestMethod]
	public void WorldPositionAccountsForParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent" );
		parent.LocalPosition = new Vector3( 100, 0, 0 );

		var child = new GameObject( parent, name: "Child" );
		var comp = child.Components.Create<TransformProbeComponent>();

		child.LocalPosition = new Vector3( 10, 0, 0 );
		Assert.AreEqual( new Vector3( 110, 0, 0 ), comp.WorldPosition );

		comp.WorldPosition = new Vector3( 25, 0, 0 );
		Assert.AreEqual( new Vector3( 25, 0, 0 ), child.WorldPosition );
		Assert.AreEqual( new Vector3( -75, 0, 0 ), child.LocalPosition );
	}

	/// <summary>
	/// WorldRotation composes the parent rotation, and writing it solves for the
	/// correct local rotation under a rotated parent.
	/// </summary>
	[TestMethod]
	public void WorldRotationAccountsForParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent" );
		parent.LocalRotation = Rotation.FromYaw( 90 );

		var child = new GameObject( parent, name: "Child" );
		var comp = child.Components.Create<TransformProbeComponent>();

		Assert.IsTrue( comp.WorldRotation.Distance( Rotation.FromYaw( 90 ) ) < 0.1f );

		comp.WorldRotation = Rotation.FromYaw( 120 );

		Assert.IsTrue( comp.WorldRotation.Distance( Rotation.FromYaw( 120 ) ) < 0.1f );
		Assert.IsTrue( child.LocalRotation.Distance( Rotation.FromYaw( 30 ) ) < 0.1f );
	}

	/// <summary>
	/// WorldScale multiplies up the hierarchy, and writing it divides out the
	/// parent's scale to produce the right local scale.
	/// </summary>
	[TestMethod]
	public void WorldScaleAccountsForParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent" );
		parent.LocalScale = new Vector3( 2, 2, 2 );

		var child = new GameObject( parent, name: "Child" );
		var comp = child.Components.Create<TransformProbeComponent>();

		child.LocalScale = new Vector3( 3, 3, 3 );
		Assert.AreEqual( new Vector3( 6, 6, 6 ), comp.WorldScale );

		comp.WorldScale = new Vector3( 2, 2, 2 );
		Assert.AreEqual( new Vector3( 1, 1, 1 ), child.LocalScale );
	}

	/// <summary>
	/// Writing the whole WorldTransform through a component on a child object
	/// updates the GameObject's world transform, recomputing its local transform
	/// against the parent.
	/// </summary>
	[TestMethod]
	public void WorldTransformWritesThroughToGameObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent" );
		parent.LocalPosition = new Vector3( 100, 0, 0 );

		var child = new GameObject( parent, name: "Child" );
		var comp = child.Components.Create<TransformProbeComponent>();

		comp.WorldTransform = Transform.Zero.WithPosition( new Vector3( 10, 0, 0 ) );

		Assert.AreEqual( new Vector3( 10, 0, 0 ), child.WorldPosition );
		Assert.AreEqual( new Vector3( -90, 0, 0 ), child.LocalPosition );
		Assert.AreEqual( new Vector3( 10, 0, 0 ), comp.WorldTransform.Position );
	}
}

/// <summary>
/// Plain component with no behaviour, used as a handle to exercise the
/// transform accessors on <see cref="Component"/>.
/// </summary>
public class TransformProbeComponent : Component
{
}
