using System.IO;

namespace Tools;

[TestClass]
public class EditorUtilityPathTests
{
	[TestMethod]
	public void DirectChildPathDoesNotMatchNestedOrSiblingPaths()
	{
		var directory = TestPath( "props" );

		Assert.IsTrue( Editor.EditorUtility.IsDirectChildPath( directory, Path.Combine( directory, "chair.vmdl" ) ) );
		Assert.IsFalse( Editor.EditorUtility.IsDirectChildPath( directory, Path.Combine( directory, "furniture", "chair.vmdl" ) ) );
		Assert.IsFalse( Editor.EditorUtility.IsDirectChildPath( directory, Path.Combine( TestPath( "props2" ), "chair.vmdl" ) ) );
		Assert.IsFalse( Editor.EditorUtility.IsDirectChildPath( directory, "chair.vmdl" ) );
	}

	[TestMethod]
	public void PathComparisonIgnoresCaseAndSeparatorStyle()
	{
		var directory = TestPath( "Props" ).Replace( '/', '\\' ).ToUpperInvariant();
		var asset = Path.Combine( TestPath( "props" ), "chair.vmdl" ).Replace( '\\', '/' ).ToLowerInvariant();

		Assert.IsTrue( Editor.EditorUtility.IsDirectChildPath( directory, asset ) );
	}

	[TestMethod]
	public void OnlySameOrDescendantDirectoriesAreRejected()
	{
		var directory = TestPath( "props" );

		Assert.IsTrue( Editor.EditorUtility.IsSameOrDescendantDirectory( directory, directory ) );
		Assert.IsTrue( Editor.EditorUtility.IsSameOrDescendantDirectory( directory, Path.Combine( directory, "furniture" ) ) );
		Assert.IsFalse( Editor.EditorUtility.IsSameOrDescendantDirectory( directory, TestPath( "props2" ) ) );
		Assert.IsFalse( Editor.EditorUtility.IsSameOrDescendantDirectory( directory, TestPath( "props_old" ) ) );
	}

	[TestMethod]
	public void RootDirectoryKeepsItsSeparator()
	{
		var root = Path.GetPathRoot( Path.GetTempPath() );

		Assert.IsTrue( Editor.EditorUtility.IsDirectChildPath( root, Path.Combine( root, "chair.vmdl" ) ) );
		Assert.IsTrue( Editor.EditorUtility.IsSameOrDescendantDirectory( root, Path.Combine( root, "props" ) ) );
	}

	private static string TestPath( string directory )
	{
		return Path.Combine( Path.GetTempPath(), "sbox-editor-utility-tests", directory );
	}
}
