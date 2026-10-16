using Sandbox.Internal;
using Sandbox.Network;

namespace SceneTests.GameObjects;

using static GlobalGameNamespace;

/// <summary>
/// The owner of a networked Rigidbody syncs its velocities to proxies through the network table.
/// </summary>
[TestClass]
public class RigidbodyNetworkingTest
{
	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;

		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;

		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	/// <summary>
	/// Spawns a networked body owned by the host, driven by a <see cref="VelocityDriver"/>.
	/// </summary>
	static (GameObject go, Rigidbody rb, VelocityDriver driver) CreateDrivenBody( Scene scene )
	{
		var go = scene.CreateObject();

		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );

		var driver = go.Components.Create<VelocityDriver>();

		go.NetworkSpawn();
		Assert.IsFalse( go.IsProxy );

		return (go, rb, driver);
	}

	/// <summary>
	/// The value in the network table for one of the Rigidbody's synced properties, which is what proxies get sent.
	/// </summary>
	static Vector3 NetworkedValue( GameObject go, Rigidbody rb, string propertyName )
	{
		var slot = NetworkObject.GetPropertySlot( $"{typeof( Rigidbody ).FullName}.{propertyName}".FastHash(), rb.Id );
		Assert.IsTrue( go._net.dataTable.IsRegistered( slot ), $"Rigidbody.{propertyName} isn't in the network table" );

		return (Vector3)go._net.dataTable.GetValue( slot );
	}

	/// <summary>
	/// A velocity the owner sets every fixed update, like a player controller does, gets networked.
	/// Setting it used to also overwrite the last networked value, so it never looked changed and proxies saw zero.
	/// </summary>
	[TestMethod]
	public void OwnerSetVelocityIsNetworked()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );
		clientAndHost.BecomeHost();

		var (go, rb, driver) = CreateDrivenBody( scene );
		driver.Velocity = new Vector3( 100, 0, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( rb.Velocity.AlmostEqual( driver.Velocity, 1f ), $"the body should be moving: {rb.Velocity}" );

		var networked = NetworkedValue( go, rb, "NetworkedVelocity" );
		Assert.IsTrue( networked.AlmostEqual( driver.Velocity, 1f ), $"proxies should be sent the velocity: {networked}" );
	}

	/// <summary>
	/// Same as <see cref="OwnerSetVelocityIsNetworked"/>, for angular velocity.
	/// </summary>
	[TestMethod]
	public void OwnerSetAngularVelocityIsNetworked()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );
		clientAndHost.BecomeHost();

		var (go, rb, driver) = CreateDrivenBody( scene );
		driver.AngularVelocity = new Vector3( 0, 0, 5 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( rb.AngularVelocity.AlmostEqual( driver.AngularVelocity, 0.1f ), $"the body should be spinning: {rb.AngularVelocity}" );

		var networked = NetworkedValue( go, rb, "NetworkedAngularVelocity" );
		Assert.IsTrue( networked.AlmostEqual( driver.AngularVelocity, 0.1f ), $"proxies should be sent the angular velocity: {networked}" );
	}

	/// <summary>
	/// Sets the Rigidbody's velocities every fixed update. The test assembly doesn't run the source generator that
	/// normally adds <see cref="Sandbox.Internal.IFixedUpdateSubscriber"/>, so it's implemented explicitly.
	/// </summary>
	private class VelocityDriver : Component, Sandbox.Internal.IFixedUpdateSubscriber
	{
		public Vector3 Velocity;
		public Vector3 AngularVelocity;

		protected override void OnFixedUpdate()
		{
			var rb = GetComponent<Rigidbody>();
			rb.Velocity = Velocity;
			rb.AngularVelocity = AngularVelocity;
		}
	}
}
