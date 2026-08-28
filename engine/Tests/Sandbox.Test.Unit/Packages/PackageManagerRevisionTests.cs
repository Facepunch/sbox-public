namespace PackageTests;

[TestClass]
public class PackageManagerRevisionTests
{
	[TestMethod]
	public void MountedRevisionIsStableByDefault()
	{
		var options = new PackageLoadOptions( "facepunch.sandbox#42", "test" );

		Assert.IsFalse( PackageManager.ShouldReplaceExistingRevision( options, 41 ) );
	}

	[TestMethod]
	[DataRow( "facepunch.sandbox#42", 41L, true )]
	[DataRow( "facepunch.sandbox#42", 42L, false )]
	[DataRow( "facepunch.sandbox", 41L, false )]
	[DataRow( "facepunch.sandbox#local", 41L, false )]
	[DataRow( "not-a-package", 41L, false )]
	public void ExplicitReplacementRequiresDifferentExactRevision( string packageIdent, long activeRevisionId, bool expected )
	{
		var options = new PackageLoadOptions( packageIdent, "test" )
		{
			ReplaceExistingRevision = true
		};

		Assert.AreEqual( expected, PackageManager.ShouldReplaceExistingRevision( options, activeRevisionId ) );
	}

	[TestMethod]
	public void ExactRevisionReplacesPackageWithMissingRevisionMetadata()
	{
		var options = new PackageLoadOptions( "facepunch.sandbox#42", "test" )
		{
			ReplaceExistingRevision = true
		};

		Assert.IsTrue( PackageManager.ShouldReplaceExistingRevision( options, null ) );
	}
}
