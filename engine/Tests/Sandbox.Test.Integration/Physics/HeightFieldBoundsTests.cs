using System;

namespace PhysicsTests;

[TestClass]
public class HeightFieldBoundsTests
{
	[TestMethod]
	public void InvalidSizesAreRejected()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var body = new PhysicsBody( scene.PhysicsWorld );

		// 65537 * 65537 wraps to 131073 in 32 bit math
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => body.AddHeightFieldShape( new ushort[131073], null, 65537, 65537, 1, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => body.AddHeightFieldShape( new ushort[4], null, -2, -2, 1, 1 ) );
	}

	[TestMethod]
	public void TerrainColliderUpdateRequiresMatchingHeightMap()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var storage = new TerrainStorage();
		storage.SetResolution( 64 );

		var terrain = scene.CreateObject().Components.Create<Terrain>( false );
		terrain.Storage = storage;
		terrain.Enabled = true;

		Assert.IsTrue( terrain.CanUpdateCollider() );

		storage.HeightMap = new ushort[4];
		Assert.IsFalse( terrain.CanUpdateCollider() );

		storage.SetResolution( 16 );
		Assert.IsFalse( terrain.CanUpdateCollider() );
	}
}
