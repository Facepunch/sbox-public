namespace ResourceTests;

[TestClass]
public class ModelPhysicsBoundsTests
{
	[TestMethod]
	public void PhysicsBoundsFollowTheResourceAfterReload()
	{
		var model = Model.Builder.AddCollisionBox( new Vector3( 8 ) ).Create();
		var replacement = Model.Builder.AddCollisionBox( new Vector3( 64 ) ).Create();
		var originalHandle = model.native;
		var before = model.PhysicsBounds;
		Assert.AreNotEqual( before, replacement.PhysicsBounds );

		try
		{
			// Simulate the resource's native data being replaced, then run its real reload invalidation.
			model.native = replacement.native;
			model.OnReloaded();
			Assert.AreEqual( replacement.PhysicsBounds, model.PhysicsBounds );
		}
		finally
		{
			model.native = originalHandle;
			model.OnReloaded();
		}

		Assert.AreEqual( before, model.PhysicsBounds );
	}
}
