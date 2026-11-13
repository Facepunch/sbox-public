using Sandbox.Navigation;
using System;

namespace NavigationTests;

[TestClass]
public class NavMeshPathStatusTest
{
	// NavMesh casts the status of a library path to its own
	[TestMethod]
	public void StatusValuesMatchTheNavigationLibrary()
	{
		Assert.AreEqual( (int)Nav.PathStatus.StartNotFound, (int)NavMeshPathStatus.StartNotFound );
		Assert.AreEqual( (int)Nav.PathStatus.EndNotFound, (int)NavMeshPathStatus.TargetNotFound );
		Assert.AreEqual( (int)Nav.PathStatus.NotFound, (int)NavMeshPathStatus.PathNotFound );
		Assert.AreEqual( (int)Nav.PathStatus.Partial, (int)NavMeshPathStatus.Partial );
		Assert.AreEqual( (int)Nav.PathStatus.Complete, (int)NavMeshPathStatus.Complete );
		Assert.AreEqual( Enum.GetValues<Nav.PathStatus>().Length, Enum.GetValues<NavMeshPathStatus>().Length );
	}
}
