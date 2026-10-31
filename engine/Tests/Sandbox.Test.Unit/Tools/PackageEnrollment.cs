namespace ToolsTests;

[TestClass]
public class PackageEnrollmentTests
{
	[TestMethod]
	public void RuntimePackagesAreEnrolledForEditorInspection()
	{
		var remotePackage = new Package();

		Assert.IsTrue( ToolsDll.ShouldEnrollPackage( remotePackage, "game" ) );
		Assert.IsTrue( ToolsDll.ShouldEnrollPackage( remotePackage, "tools" ) );
		Assert.IsTrue( ToolsDll.ShouldEnrollPackage( remotePackage, "hammer" ) );
		Assert.IsFalse( ToolsDll.ShouldEnrollPackage( remotePackage, "menu" ) );
	}
}
