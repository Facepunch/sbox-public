using Sandbox.Diagnostics;
using Sandbox.Engine;
using System;
using System.IO;

namespace ResourceTests;

[TestClass]
public class GuidTests
{
	string _assetsPath;
	string _testPath;

	[TestInitialize]
	public void TestInitialize()
	{
		Logging.Enabled = true;
		Project.Clear();

		var project = Project.AddFromFile( "unittest/addons/resources/.sbproj" );
		_assetsPath = project.GetAssetsPath();

		// create a unique subfolder for this run so we don't have to worry about leftover shit
		var testName = $"rc_test_{System.Guid.NewGuid():N}";
		_testPath = System.IO.Path.Combine( _assetsPath, testName );
		System.IO.Directory.CreateDirectory( _testPath );

		NativeEngine.FullFileSystem.AddProjectPath( "unittest", _assetsPath.ToLowerInvariant() );

		// make sure the guid mappings are rebuilt
		NativeEngine.g_pResourceSystem.InvalidateDatabase();

		GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.Mount( new LocalFileSystem( _assetsPath ) );

		ResourceLoader.LoadAllGameResource( FileSystem.Mounted, reloadExisting: true );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();

		System.IO.Directory.Delete( _testPath, recursive: true );
	}

	/// <summary>
	/// Can fetch by GUID only (no path)
	/// </summary>
	[TestMethod]
	public void GetByGUIDOnly()
	{
		ResourceId id = Guid.Parse( "f8646022-0110-4d96-87a4-540659497d24" );

		var resource = Game.Resources.Get( typeof( Resource ), id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.ResourcePath, "test.scene" );
	}

	/// <summary>
	/// Using a combo of a valid GUID but an invalid path should still find the resource by GUID
	/// </summary>
	[TestMethod]
	public void FindByValidGuid()
	{
		ResourceId id = new()
		{
			Guid = Guid.Parse( "f8646022-0110-4d96-87a4-540659497d24" ),
			Path = "missing.scene"
		};

		var resource = Game.Resources.Get( typeof( Resource ), id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.Guid, Guid.Parse( "f8646022-0110-4d96-87a4-540659497d24" ) );
	}

	/// <summary>
	/// Using a combo of a valid GUID but an invalid path should still find the resource by GUID
	/// </summary>
	[TestMethod]
	public void GetByPathOnly()
	{
		ResourceId id = "test.scene";

		var resource = Game.Resources.Get( typeof( Resource ), id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.Guid, Guid.Parse( "f8646022-0110-4d96-87a4-540659497d24" ) );
	}

	/// <summary>
	/// Using a combo of an invalid Guid but a valid path should still find the resource by path
	/// </summary>
	[TestMethod]
	public void FindByValidPath()
	{
		ResourceId id = new()
		{
			Guid = Guid.NewGuid(),
			Path = "test.scene"
		};

		var resource = Game.Resources.Get( typeof( Resource ), id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.Guid, Guid.Parse( "f8646022-0110-4d96-87a4-540659497d24" ) );
	}

	/// <summary>
	/// Can fetch by GUID only (no path)
	/// </summary>
	[TestMethod]
	public void Native_FindByGuidOnly()
	{
		ResourceId id = Guid.Parse( "8288cd80-e793-44ba-a9d6-10503d715b7f" );

		var resource = Model.Load( id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.ResourcePath, "model.vmdl" );
	}

	/// <summary>
	/// Using a combo of a valid GUID but an invalid path should still find the resource by GUID
	/// </summary>
	[TestMethod]
	public void Native_FindByValidGuid()
	{
		ResourceId id = new()
		{
			Guid = Guid.Parse( "8288cd80-e793-44ba-a9d6-10503d715b7f" ),
			Path = "missing.vmdl"
		};

		var resource = Model.Load( id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.ResourcePath, "model.vmdl" );
	}

	/// <summary>
	/// Can fetch by path only (no GUID)
	/// </summary>
	[TestMethod]
	public void Native_FindByPathOnly()
	{
		ResourceId id = "model.vmdl";

		var resource = Model.Load( id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.Guid, Guid.Parse( "8288cd80-e793-44ba-a9d6-10503d715b7f" ) );
	}

	/// <summary>
	/// Using a combo of an invalid Guid but a valid path should still find the resource by path
	/// </summary>
	[TestMethod]
	public void Native_FindByValidPath()
	{
		ResourceId id = new()
		{
			Guid = Guid.NewGuid(),
			Path = "model.vmdl"
		};

		var resource = Model.Load( id );
		Assert.IsNotNull( resource, "Couldn't find resource" );
		Assert.AreEqual( resource.Guid, Guid.Parse( "8288cd80-e793-44ba-a9d6-10503d715b7f" ) );
	}


	void WriteVmdl( string relativePath, Guid guid )
	{
		WriteMeta( relativePath, guid );

		var src = File.ReadAllBytes( Path.Combine( _assetsPath, "model.vmdl_c" ) );
		File.WriteAllBytes( Path.Combine( _assetsPath, relativePath + "_c" ), src );
	}

	void WriteMeta( string relativePath, Guid guid )
	{
		File.WriteAllText( Path.Combine( _assetsPath, relativePath + ".meta" ), $"{{\"guid\": \"{guid}\"}}" );
	}

	void DeleteMeta( string relativePath )
	{
		File.Delete( Path.Combine( _assetsPath, relativePath + ".meta" ) );
	}

	/// <summary>
	/// After a resource is renamed, its old path must resolve to whatever's there now,
	/// not the resource that used to live there.
	/// </summary>
	[TestMethod]
	public void PathReuseAfterRename()
	{
		var folder = Path.GetFileName( _testPath );
		var originalPath = $"{folder}/original.vmdl";
		var renamedPath = $"{folder}/moved.vmdl";

		// 1. create a resource at the original path
		var originalGuid = Guid.NewGuid();
		WriteVmdl( originalPath, originalGuid );

		NativeEngine.g_pResourceSystem.InvalidateDatabase();

		var original = Model.Load( originalPath );
		Assert.IsNotNull( original, "Couldn't load the original resource" );
		Assert.IsFalse( original.IsError, "Original resource shouldn't error" );
		Assert.AreEqual( originalGuid, original.Guid );

		// 2. simulate a rename (same guid, new path)
		DeleteMeta( originalPath );
		WriteVmdl( renamedPath, originalGuid );

		NativeEngine.g_pResourceSystem.InvalidateDatabase();

		var moved = Model.Load( renamedPath );
		Assert.AreSame( original, moved, "Renamed resource should maintain the same Model instance" );
		Assert.AreEqual( renamedPath, moved.ResourcePath, "ResourcePath was not updated after rename" );

		// 3. introduce a new model (with new guid) at the old path
		var newGuid = Guid.NewGuid();
		WriteMeta( originalPath, newGuid );

		NativeEngine.g_pResourceSystem.InvalidateDatabase();

		var newModel = Model.Load( originalPath );
		Assert.IsNotNull( newModel, "Couldn't load the new resource" );
		Assert.AreNotSame( original, newModel, "Path resolved to old model, which isn't at this path anymore" );
		Assert.AreEqual( newGuid, newModel.Guid, "Incorrect GUID for the new model" );
	}

	void WriteScene( string relativePath, Guid guid )
	{
		WriteMeta( relativePath, guid );

		var src = File.ReadAllBytes( Path.Combine( _assetsPath, "test.scene_c" ) );
		File.WriteAllBytes( Path.Combine( _assetsPath, relativePath + "_c" ), src );
	}

	/// <summary>
	/// A path-only promise must adopt any GUID once it becomes known.
	/// </summary>
	[TestMethod]
	public void PathOnlyPromiseAdoptsGuidOnLoad()
	{
		var folder = Path.GetFileName( _testPath );
		var relativePath = $"{folder}/promise_test.scene";

		// legacy/path-only reference, resolved before this resource (or its .meta) exists
		var promise = GameResource.GetPromise( typeof( SceneFile ), relativePath );
		Assert.IsNotNull( promise );
		Assert.AreEqual( Guid.Empty, promise.Guid, "Promise shouldn't have a guid yet" );

		// now the resource actually gets created on disk with a real guid, and loaded for real
		var guid = Guid.NewGuid();
		WriteScene( relativePath, guid );

		NativeEngine.g_pResourceSystem.InvalidateDatabase();
		ResourceLoader.LoadAllGameResource( FileSystem.Mounted, reloadExisting: true );

		Assert.AreEqual( guid, promise.Guid, "Promise should have adopted the guid once the resource was loaded" );
		Assert.AreSame( promise, Game.Resources.Get( typeof( SceneFile ), guid ), "Resource should now be indexed by guid too" );
	}

	/// <summary>
	/// A GUID-only promise must adopt the path on load. It also shouldn't do anything weird because it's not got a path.
	/// </summary>
	[TestMethod]
	public void GuidOnlyPromiseResolvesPathOnLoad()
	{
		var folder = Path.GetFileName( _testPath );
		var currentPath = $"{folder}/guid_only.scene";

		var guidA = Guid.NewGuid();
		var guidB = Guid.NewGuid();

		// two separate guid-only references, no path attached to either
		var promiseA = GameResource.GetPromise( typeof( SceneFile ), new ResourceId { Guid = guidA } );
		var promiseB = GameResource.GetPromise( typeof( SceneFile ), new ResourceId { Guid = guidB } );

		Assert.AreNotSame( promiseA, promiseB );
		Assert.AreSame( promiseA, Game.Resources.Get( typeof( SceneFile ), guidA ) );
		Assert.AreSame( promiseB, Game.Resources.Get( typeof( SceneFile ), guidB ) );
		Assert.IsNull( Game.Resources.GetByIdLong<SceneFile>( 0 ), "GUID-only promise must not be indexed under a 'no path' key" );

		// now the real resource for guidA gets loaded
		WriteScene( currentPath, guidA );
		NativeEngine.g_pResourceSystem.InvalidateDatabase();
		ResourceLoader.LoadAllGameResource( FileSystem.Mounted, reloadExisting: true );

		Assert.AreSame( promiseA, ResourceLibrary.Get<SceneFile>( currentPath ), "GUID-only promise should adopt the real path once loaded" );
		Assert.AreEqual( currentPath, promiseA.ResourcePath );

		// promiseB is unrelated and untouched
		Assert.AreSame( promiseB, Game.Resources.Get( typeof( SceneFile ), guidB ) );
	}

	/// <summary>
	/// A GUID reference may carry a stale path (eg. a scene saved before the referenced resource was last renamed).
	/// Make sure that gets corrected on load.
	/// </summary>
	[TestMethod]
	public void StalePathPromiseCorrectedOnLoad()
	{
		var folder = Path.GetFileName( _testPath );
		var stalePath = $"{folder}/gp_stale.scene";
		var currentPath = $"{folder}/gp_current.scene";

		var guid = Guid.NewGuid();
		WriteScene( currentPath, guid );

		NativeEngine.g_pResourceSystem.InvalidateDatabase();

		// reference by guid, but with a stale path that was never actually where this
		// resource lived (eg a saved reference from before a rename)
		ResourceId id = new() { Guid = guid, Path = stalePath };
		var promise = GameResource.GetPromise( typeof( SceneFile ), id );
		Assert.AreEqual( stalePath, promise.ResourcePath, "Sanity: promise should start out at the stale path" );

		ResourceLoader.LoadAllGameResource( FileSystem.Mounted, reloadExisting: true );

		Assert.AreSame( promise, ResourceLibrary.Get<SceneFile>( currentPath ), "Should resolve to the resource's current path, not the stale one supplied" );
		Assert.AreEqual( currentPath, promise.ResourcePath, "Promise's path should have been corrected once the real resource loaded" );
	}

}
