using System.Collections.Generic;
using System.Text.Json;

namespace Sandbox.Test.Json;

/// <summary>
/// A Project is built from its path, and addons.json is a list of them, so the deserializer has to
/// be able to reach that constructor. System.Text.Json matches constructor parameters against
/// property names rather than their JsonPropertyName, which is easy to break by renaming either.
/// </summary>
[TestClass]
public class ProjectJsonTest
{
	[TestMethod]
	public void DeserializeProjectList()
	{
		var json = """[ { "Path": "/somewhere/myproject/.sbproj", "Active": true } ]""";

		var projects = JsonSerializer.Deserialize<List<Project>>( json );

		Assert.AreEqual( 1, projects.Count );
		Assert.IsTrue( projects[0].Active );
		Assert.IsTrue( projects[0].ConfigFilePath.EndsWith( ".sbproj" ) );

		// The root folder is worked out from the path, at construction
		Assert.AreEqual( "myproject", projects[0].RootDirectory.Name );
	}

	[TestMethod]
	public void DeserializeProjectWithoutPath()
	{
		// A junk entry has to come back as a project we can throw away, not an exception out of
		// the deserializer, or one bad line costs the whole project list
		var projects = JsonSerializer.Deserialize<List<Project>>( """[ { "Active": true } ]""" );

		Assert.AreEqual( 1, projects.Count );
		Assert.IsNull( projects[0].ConfigFilePath );
		Assert.IsNull( projects[0].RootDirectory );
	}

	[TestMethod]
	public void ProjectListRoundTrip()
	{
		var projects = JsonSerializer.Deserialize<List<Project>>( """[ { "Path": "/somewhere/myproject/.sbproj" } ]""" );

		var round = JsonSerializer.Deserialize<List<Project>>( JsonSerializer.Serialize( projects ) );

		Assert.AreEqual( projects[0].ConfigFilePath, round[0].ConfigFilePath );
	}

	[TestMethod]
	public void ProjectTakesTheFolderOrTheFile()
	{
		// Callers hand us the folder as often as the .sbproj in it
		var fromFolder = new Project( "/somewhere/myproject" );
		var fromFile = new Project( "/somewhere/myproject/.sbproj" );

		Assert.AreEqual( fromFile.ConfigFilePath, fromFolder.ConfigFilePath );
		Assert.AreEqual( fromFile.RootDirectory.FullName, fromFolder.RootDirectory.FullName );
	}
}
