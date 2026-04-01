using Sandbox.Diagnostics;
using System;
using System.IO;
using System.Linq;

namespace Projects;

[TestClass]
public class ProjectTests
{
	[TestInitialize]
	public void TestInitialize()
	{
		Logging.Enabled = true;
		Project.Clear();
		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/project";
		AssetDownloadCache.Initialize( dir );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	string CreateTempProject( string root, string relativePath, string type, string ident, bool hasCode = true, bool hasEditor = false )
	{
		var projectRoot = Path.Combine( root, relativePath );
		Directory.CreateDirectory( projectRoot );

		if ( hasCode )
			Directory.CreateDirectory( Path.Combine( projectRoot, "Code" ) );

		if ( hasEditor )
			Directory.CreateDirectory( Path.Combine( projectRoot, "Editor" ) );

		var configText =
		$@"{{
		""Title"": ""{ident}"",
		""Type"": ""{type}"",
		""Org"": ""local"",
		""Ident"": ""{ident}"",
		""Schema"": 1,
		""HasAssets"": false,
		""HasCode"": {hasCode.ToString().ToLowerInvariant()},
		""CodePath"": ""Code"",
		""PackageReferences"": []
		}}";

		File.WriteAllText( Path.Combine( projectRoot, ".sbproj" ), configText );

		return Path.Combine( projectRoot, ".sbproj" );
	}

	/// <summary>
	/// Find and load a local package
	/// </summary>
	[TestMethod]
	public void AddProject()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

	}

	/// <summary>
	/// Find and load a local package
	/// </summary>
	[TestMethod]
	public async Task AddBaseAddon()
	{
		var project = Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();
	}

	[TestMethod]
	public async Task BaseSolutionDoesNotReferenceLocalLibraries()
	{
		Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

		var tempRoot = Path.Combine( Environment.CurrentDirectory, ".source2", "test_download_cache", "project", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( tempRoot );

		try
		{
			var libraryPath = CreateTempProject( tempRoot, Path.Combine( "Libraries", "samplelib" ), "library", "samplelib", hasCode: true, hasEditor: true );
			var addonPath = CreateTempProject( tempRoot, Path.Combine( "Addons", "sampleaddon" ), "addon", "sampleaddon", hasCode: true, hasEditor: true );

			Project.AddFromFile( libraryPath );
			var addon = Project.AddFromFile( addonPath );

			await Project.SyncWithPackageManager();

			Project.Current = addon;
			await Project.GenerateSolution();

			var baseProjectFile = Path.Combine( Environment.CurrentDirectory, "addons", "base", "Code", "Base Library.csproj" );
			var addonProjectFile = Path.Combine( tempRoot, "Addons", "sampleaddon", "Code", "sampleaddon.csproj" );
			var addonEditorProjectFile = Path.Combine( tempRoot, "Addons", "sampleaddon", "Editor", "sampleaddon.editor.csproj" );

			var baseProjectText = File.ReadAllText( baseProjectFile );
			var addonProjectText = File.ReadAllText( addonProjectFile );
			var addonEditorProjectText = File.ReadAllText( addonEditorProjectFile );

			Assert.IsFalse( baseProjectText.Contains( "samplelib.csproj" ), "local.base should not reference local library code projects." );
			Assert.IsFalse( baseProjectText.Contains( "samplelib.editor.csproj" ), "local.base should not reference local library editor projects." );
			Assert.IsTrue( addonProjectText.Contains( "samplelib.csproj" ), "Regular addons should still reference local library code projects." );
			Assert.IsTrue( addonEditorProjectText.Contains( "samplelib.editor.csproj" ), "Regular addon editor projects should still reference local library editor projects." );

			var baseProject = Project.FindByIdent( "local.base" );
			var baseReferences = baseProject.Package.EnumeratePackageReferences().ToArray();
			Assert.IsFalse( baseReferences.Contains( "local.samplelib" ), "local.base should not enumerate local libraries as package references." );
		}
		finally
		{
			Project.Current = null;

			if ( Directory.Exists( tempRoot ) )
			{
				Directory.Delete( tempRoot, true );
			}
		}
	}

	/*
	[TestMethod]
	public async Task OpenGameProject()
	{
		Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

		var project = Project.AddFromFile( "unittest/addons/spacewars", false );

		var ct = new CancellationToken();
		await EditorUtility.Projects.OpenProject( project.Path, null, ct ); ;

		Assert.IsNotNull( project.Path );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		var assemblies = PackageManager.MountedFileSystem.FindFile( "/.bin/", "*.dll" ).ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}
	}
	*/

	/// <summary>
	/// Initialize the menu addon
	/// </summary>
	[TestMethod]
	public async Task MenuInitialization()
	{
		Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

		var project = Project.AddFromFile( "addons/menu/.sbproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();

		var assemblies = PackageManager.MountedFileSystem.FindFile( "/.bin/", "*.dll", false ).ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}

	}
}
