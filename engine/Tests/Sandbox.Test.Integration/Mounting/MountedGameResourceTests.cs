using Sandbox.Engine;

namespace MountingTests;

[TestClass]
public class MountedGameResourceTest
{
	const string Path = "mount://testgame/resources/test.custom";

	[TestInitialize]
	public async Task MountSource()
	{
		Sandbox.Mounting.Directory.AddAssembly( GetType().Assembly );
		var mount = await Sandbox.Mounting.Directory.Mount( "testgame" );
		Assert.IsNotNull( mount );
		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( Path ) );
	}

	[TestCleanup]
	public void UnmountSource()
	{
		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );
	}

	[TestMethod]
	public void CustomResourceLoadsThroughJsonReference()
	{
		using var context = new MountedResourceTestContext();
		var resource = Json.Deserialize<TestGameCustomResource>( $"\"{Path}\"" );
		Assert.IsNotNull( resource );
		Assert.AreEqual( 42, resource.Value );
		Assert.IsTrue( resource.IsInitialized );
		Assert.AreSame( resource, ResourceLibrary.Get<TestGameCustomResource>( Path ) );
		Assert.IsFalse( resource.IsPromise );
	}

	[TestMethod]
	public void GenericCategoryPreservesAssetExtension()
	{
		using var context = new MountedResourceTestContext();
		var loader = Sandbox.Mounting.Directory.Get( "testgame" ).GetByPath( Path );
		Assert.IsNotNull( loader );
		Assert.AreEqual( Sandbox.Mounting.ResourceType.GameResource, loader.Type );
		Assert.AreEqual( "resources/test.custom", loader.RelativePath );
	}

	[TestMethod]
	public void UnmountReleasesRegisteredResource()
	{
		using var context = new MountedResourceTestContext();
		var resource = GameResource.Load<TestGameCustomResource>( Path );
		Assert.IsNotNull( resource );

		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );

		Assert.IsFalse( resource.IsValid );
		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( Path ) );
	}

	[TestMethod]
	public void CustomResourceLoadsThroughTypedLoader()
	{
		using var context = new MountedResourceTestContext();
		var resource = GameResource.Load<TestGameCustomResource>( Path );
		Assert.IsNotNull( resource );
		Assert.AreEqual( 42, resource.Value );
		Assert.AreSame( resource, ResourceLibrary.Get<TestGameCustomResource>( Path ) );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	[DataRow( "   " )]
	public void TypedLoaderRejectsEmptyPaths( string path )
	{
		using var context = new MountedResourceTestContext();
		Assert.IsNull( GameResource.Load<TestGameCustomResource>( path ) );
	}

	[TestMethod]
	public async Task CustomResourceLoadsThroughAsyncLibrary()
	{
		using var context = new MountedResourceTestContext();
		var resource = await ResourceLibrary.LoadAsync<TestGameCustomResource>( Path );
		Assert.IsNotNull( resource );
		Assert.AreEqual( 42, resource.Value );
		Assert.AreSame( resource, ResourceLibrary.Get<TestGameCustomResource>( Path ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task GameResourceLoadingPreservesMountedSceneCategory( bool asynchronous )
	{
		using var context = new MountedResourceTestContext();
		const string scenePath = "mount://testgame/maps/testlevel.scene";
		var resource = asynchronous
			? await ResourceLibrary.LoadAsync<SceneFile>( scenePath )
			: GameResource.Load<SceneFile>( scenePath );

		Assert.IsNotNull( resource );
		Assert.AreSame( resource, SceneFile.Load( scenePath ) );
	}

	[TestMethod]
	public async Task MountedGameResourceLoadingRejectsWorkerThreads()
	{
		using var context = new MountedResourceTestContext();
		var exception = await Assert.ThrowsExceptionAsync<System.Exception>( async () =>
			await Task.Run( () => Sandbox.Mounting.Directory.TryLoadAsync( Path ) ) );

		Assert.AreEqual( "LoadResource must be called on the main thread!", exception.Message );
		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( Path ) );
	}

	[TestMethod]
	public async Task ConcurrentMountedLoadsShareRegistrationAndLifetime()
	{
		using var context = new MountedResourceTestContext();
		const string pendingPath = "mount://testgame/resources/pending.custom";
		var loader = Sandbox.Mounting.Directory.Get( "testgame" ).GetByPath( pendingPath ) as TestGamePendingResource;
		Assert.IsNotNull( loader );
		var first = ResourceLibrary.LoadAsync<TestGameCustomResource>( pendingPath );
		var second = ResourceLibrary.LoadAsync<TestGameCustomResource>( pendingPath );
		Assert.IsFalse( first.IsCompleted );
		Assert.IsFalse( second.IsCompleted );

		var resource = new TestGameCustomResource();
		loader.Completion.SetResult( resource );
		Assert.AreSame( resource, await first );
		Assert.AreSame( resource, await second );
		Assert.IsTrue( resource.IsInitialized );
		Assert.AreSame( resource, ResourceLibrary.Get<TestGameCustomResource>( pendingPath ) );

		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );
		Assert.IsFalse( resource.IsValid );
		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( pendingPath ) );
	}

	[TestMethod]
	public async Task MountedResourceLoadsThroughBaseResourceType()
	{
		using var context = new MountedResourceTestContext();
		var resource = await ResourceLibrary.LoadAsync<GameResource>( Path );
		Assert.IsInstanceOfType<TestGameCustomResource>( resource );
		Assert.AreSame( resource, Json.Deserialize<GameResource>( $"\"{Path}\"" ) );
	}

	[TestMethod]
	public void ContextResetReleasesResourcesAndAllowsReload()
	{
		using var context = new MountedResourceTestContext();
		var first = GameResource.Load<TestGameCustomResource>( Path );
		Assert.IsNotNull( first );
		GlobalContext.Current.Reset();

		Assert.IsFalse( first.IsValid );
		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( Path ) );

		var second = GameResource.Load<TestGameCustomResource>( Path );
		Assert.IsNotNull( second );
		Assert.AreNotSame( first, second );
		Assert.IsTrue( second.IsInitialized );
	}

	[TestMethod]
	public void MountedResourceIsCachedPerContext()
	{
		using var context = new MountedResourceTestContext();
		var first = GameResource.Load<TestGameCustomResource>( Path );
		Assert.IsNotNull( first );

		TestGameCustomResource second;
		using ( new MountedResourceTestContext() )
		{
			second = GameResource.Load<TestGameCustomResource>( Path );
			Assert.IsNotNull( second );
			Assert.AreNotSame( first, second );
			Assert.AreSame( second, GameResource.Load<TestGameCustomResource>( Path ) );
			Assert.IsTrue( second.IsInitialized );
		}

		Assert.IsFalse( second.IsValid );
		Assert.IsTrue( first.IsValid );
		Assert.AreSame( first, GameResource.Load<TestGameCustomResource>( Path ) );
	}

	[TestMethod]
	public void UnmountReleasesResourcesFromEachContext()
	{
		using var context = new MountedResourceTestContext();
		var first = GameResource.Load<TestGameCustomResource>( Path );
		Assert.IsNotNull( first );

		using ( new MountedResourceTestContext() )
		{
			var second = GameResource.Load<TestGameCustomResource>( Path );
			Assert.IsNotNull( second );

			Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );

			Assert.IsFalse( first.IsValid );
			Assert.IsFalse( second.IsValid );
			Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( Path ) );
		}

		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( Path ) );
	}

	[TestMethod]
	public async Task UnmountDuringLoadDoesNotRegisterResource()
	{
		using var context = new MountedResourceTestContext();
		const string pendingPath = "mount://testgame/resources/pending.custom";
		var loader = Sandbox.Mounting.Directory.Get( "testgame" ).GetByPath( pendingPath ) as TestGamePendingResource;
		Assert.IsNotNull( loader );
		var loading = ResourceLibrary.LoadAsync<TestGameCustomResource>( pendingPath );
		Assert.IsFalse( loading.IsCompleted );

		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );
		var resource = new TestGameCustomResource();
		loader.Completion.SetResult( resource );

		Assert.IsNull( await loading );
		Assert.IsFalse( resource.IsValid );
		Assert.IsFalse( resource.IsInitialized );
		Assert.IsNull( ResourceLibrary.Get<TestGameCustomResource>( pendingPath ) );
	}

	[TestMethod]
	public void MountedResourceDoesNotCreateMismatchedPromise()
	{
		using var context = new MountedResourceTestContext();
		Assert.IsNull( GameResource.Load<Surface>( Path ) );
		Assert.IsInstanceOfType<TestGameCustomResource>( ResourceLibrary.Get<GameResource>( Path ) );
	}

	[TestMethod]
	public async Task AsyncLibraryRejectsMismatchedResourceType()
	{
		using var context = new MountedResourceTestContext();
		Assert.IsNull( await ResourceLibrary.LoadAsync<Surface>( Path ) );
		Assert.IsInstanceOfType<TestGameCustomResource>( ResourceLibrary.Get<GameResource>( Path ) );
	}
}
