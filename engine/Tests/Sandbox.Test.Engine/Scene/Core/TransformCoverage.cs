namespace SceneTests.Core;

/// <summary>
/// Component that moves its object to a target position from inside a fixed update,
/// so tests can exercise the fixed-update transform path (where interpolation would
/// normally kick in).
/// </summary>
public class FixedStepMover : Component, Sandbox.Internal.IFixedUpdateSubscriber
{
	public Vector3 Target;
	public int Moves;

	protected override void OnFixedUpdate()
	{
		if ( Moves > 0 )
			return;

		Moves++;
		WorldPosition = Target;
	}
}

/// <summary>
/// Pins GameTransform behavior not covered by the transform math tests: the
/// OnTransformChanged callback and its propagation, the Absolute flag, the
/// default-transform coercion, interpolation state and the proxy escape hatch.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TransformCoverageTest : SceneTest
{
	/// <summary>
	/// OnTransformChanged fires when the transform actually changes and is skipped
	/// when the transform is set to its current value.
	/// </summary>
	[TestMethod]
	public void TransformChangedFiresOnRealChanges()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var changes = 0;
		go.Transform.OnTransformChanged += () => changes++;

		go.WorldPosition = new Vector3( 10, 0, 0 );
		Assert.AreEqual( 1, changes );

		// Setting the exact same position must not re-fire the callback
		go.WorldPosition = new Vector3( 10, 0, 0 );
		Assert.AreEqual( 1, changes );

		go.WorldRotation = Rotation.FromYaw( 90 );
		Assert.AreEqual( 2, changes );

		scene.Destroy();
	}

	/// <summary>
	/// Moving a parent notifies the children's OnTransformChanged too - their world
	/// transform changed even though their local transform didn't.
	/// </summary>
	[TestMethod]
	public void TransformChangedPropagatesToChildren()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 1, 0, 0 );

		var childChanges = 0;
		child.Transform.OnTransformChanged += () => childChanges++;

		parent.WorldPosition = new Vector3( 0, 0, 50 );

		Assert.AreEqual( 1, childChanges );
		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 1, 0, 50 ) ) );

		scene.Destroy();
	}

	/// <summary>
	/// An object flagged Absolute ignores its parent's transform completely: its world
	/// transform is its local transform, and parent moves neither move it nor fire its
	/// transform-changed callback.
	/// </summary>
	[TestMethod]
	public void AbsoluteFlagIgnoresParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( 100, 0, 0 );

		var child = new GameObject( parent );
		child.Flags |= GameObjectFlags.Absolute;
		child.WorldPosition = new Vector3( 5, 0, 0 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 5, 0, 0 ) ) );
		Assert.IsTrue( child.LocalPosition.AlmostEqual( new Vector3( 5, 0, 0 ) ) );

		var childChanges = 0;
		child.Transform.OnTransformChanged += () => childChanges++;

		parent.WorldPosition = new Vector3( 200, 0, 0 );

		Assert.AreEqual( 0, childChanges );
		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 5, 0, 0 ) ) );

		scene.Destroy();
	}

	/// <summary>
	/// Assigning a default Transform (all zero, including scale) is coerced to
	/// Transform.Zero, which has identity rotation and scale one - objects can't end
	/// up invisible with a zero scale by accident.
	/// </summary>
	[TestMethod]
	public void DefaultTransformIsCoercedToZero()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.LocalPosition = new Vector3( 1, 2, 3 );

		go.LocalTransform = default;

		Assert.AreEqual( Transform.Zero, go.LocalTransform );
		Assert.IsTrue( go.LocalScale.AlmostEqual( new Vector3( 1, 1, 1 ) ) );

		scene.Destroy();
	}

	/// <summary>
	/// Outside of a fixed update a transform set is applied directly - the
	/// interpolated local transform immediately matches the target.
	/// </summary>
	[TestMethod]
	public void DirectSetUpdatesInterpolatedLocal()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.LocalPosition = new Vector3( 7, 8, 9 );

		Assert.IsTrue( go.Transform.InterpolatedLocal.Position.AlmostEqual( new Vector3( 7, 8, 9 ) ) );
		Assert.IsTrue( go.Transform.Local.Position.AlmostEqual( new Vector3( 7, 8, 9 ) ) );

		scene.Destroy();
	}

	/// <summary>
	/// A move made during a fixed update reaches its exact target once interpolation
	/// is cleared - ClearInterpolation snaps the transform to its final destination
	/// regardless of any interpolation that was in flight.
	/// </summary>
	[TestMethod]
	public void ClearInterpolationSnapsToTarget()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mover = go.Components.Create<FixedStepMover>();
		mover.Target = new Vector3( 100, 50, 25 );

		scene.GameTick();

		Assert.AreEqual( 1, mover.Moves );

		go.Transform.ClearInterpolation();

		Assert.IsTrue( go.WorldPosition.AlmostEqual( mover.Target ), $"{go.WorldPosition}" );
		Assert.IsTrue( go.Transform.InterpolatedLocal.Position.AlmostEqual( mover.Target ) );

		scene.Destroy();
	}

	/// <summary>
	/// DisableProxy with no proxy installed returns nothing to dispose - there's no
	/// proxy to restore.
	/// </summary>
	[TestMethod]
	public void DisableProxyWithoutProxyReturnsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.IsNull( go.Transform.Proxy );
		Assert.IsNull( go.Transform.DisableProxy() );

		scene.Destroy();
	}
}
