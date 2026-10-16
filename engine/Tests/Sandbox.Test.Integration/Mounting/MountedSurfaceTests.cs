using Sandbox.Engine;
using Sandbox.Mounting;

namespace MountingTests;

[TestClass]
public class MountedSurfaceTest
{
	const string Path = "mount://testgame/surfaces/test.surface";

	[TestInitialize]
	public async Task MountSource()
	{
		Sandbox.Mounting.Directory.AddAssembly( GetType().Assembly );
		Assert.IsNotNull( await Sandbox.Mounting.Directory.Mount( "testgame" ) );
		Assert.IsNull( ResourceLibrary.Get<Surface>( Path ) );
	}

	[TestCleanup]
	public void UnmountSource()
	{
		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );
	}

	[TestMethod]
	public void SurfaceConstructionAndPhysicsNameAreNotPublic()
	{
		Assert.IsNull( typeof( Surface ).GetProperty( "PhysicsName" ) );
		Assert.IsNull( typeof( Surface ).GetMethod( "Create", [typeof( string ), typeof( Surface )] ) );
	}

	[TestMethod]
	public async Task OrdinaryLoaderRegistersAndCachesSurface()
	{
		using var context = new MountedResourceTestContext();
		var loader = Sandbox.Mounting.Directory.Get( "testgame" ).GetByPath( Path );
		Assert.AreEqual( "surfaces/test.surface", loader.RelativePath );
		Assert.AreEqual( ResourceType.GameResource, loader.Type );

		var surface = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( surface );
		Assert.AreEqual( 1.25f, surface.Friction );
		Assert.AreEqual( 0.4f, surface.Elasticity );
		Assert.AreEqual( 700.0f, surface.Density );
		Assert.AreEqual( Path, surface.PhysicsName );
		Assert.AreSame( surface, ResourceLibrary.Get<Surface>( Path ) );
		Assert.AreSame( surface, Surface.FindByName( Path ) );
		Assert.AreSame( surface, Surface.FindByIndex( surface.Index ) );
		Assert.AreSame( surface, await ResourceLibrary.LoadAsync<Surface>( Path ) );
	}

	[TestMethod]
	public async Task SurfaceSoundsAreSharedMountedResources()
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( surface );
		var sound = await ResourceLibrary.LoadAsync<SoundEvent>( "mount://testgame/sounds/surface.sound" );
		Assert.IsNotNull( sound );
		Assert.AreSame( sound, GameResource.Load<SoundEvent>( sound.ResourcePath ) );
		Assert.AreSame( sound, surface.SoundCollection.FootLeft );
		Assert.AreSame( sound, surface.SoundCollection.FootRight );
		Assert.IsNull( sound.EmbeddedResource );
		Assert.AreEqual( "\"mount://testgame/sounds/surface.sound\"", Json.Serialize( sound ) );
	}

	[TestMethod]
	public void UnmountReleasesRegisteredSurfaceAndSound()
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( surface );
		var sound = surface.SoundCollection.FootLeft;
		Assert.IsNotNull( sound );

		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );

		Assert.IsFalse( surface.IsValid );
		Assert.IsFalse( sound.IsValid );
		Assert.IsNull( ResourceLibrary.Get<Surface>( Path ) );
		Assert.IsNull( ResourceLibrary.Get<SoundEvent>( sound.ResourcePath ) );
		Assert.AreEqual( 0, Surface.All.Count );
	}

	[TestMethod]
	public async Task MountedSurfaceLoadsLazilyThroughResourceReferences()
	{
		using var context = new MountedResourceTestContext();
		var surface = Json.Deserialize<Surface>( $"\"{Path}\"" );
		Assert.IsNotNull( surface );
		Assert.AreSame( surface, GameResource.Load<Surface>( Path ) );
		Assert.AreSame( surface, await ResourceLibrary.LoadAsync<Surface>( Path ) );
	}

	[TestMethod]
	public async Task MountedSurfaceLoadsLazilyThroughAsyncLibrary()
	{
		using var context = new MountedResourceTestContext();
		var surface = await ResourceLibrary.LoadAsync<Surface>( Path );
		Assert.IsNotNull( surface );
		Assert.AreSame( surface, ResourceLibrary.Get<Surface>( Path ) );
	}

	[TestMethod]
	public void MountedSurfaceLoadsLazilyThroughTypedLoader()
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( surface );
		Assert.AreSame( surface, ResourceLibrary.Get<Surface>( Path ) );
	}

	[TestMethod]
	public async Task RefreshReplacesRegisteredSurface()
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( surface );

		await Sandbox.Mounting.Directory.Get( "testgame" ).RefreshInternal();

		Assert.IsFalse( surface.IsValid );
		Assert.IsNull( ResourceLibrary.Get<Surface>( Path ) );
		var replacement = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( replacement );
		Assert.AreNotSame( surface, replacement );
		Assert.AreSame( replacement, Surface.FindByIndex( replacement.Index ) );
	}

	[TestMethod]
	public void SurfacePhysicsNamesCanDistinguishMatchingResourceNames()
	{
		using var context = new MountedResourceTestContext();
		var a = GameResource.Load<Surface>( "mount://testgame/surfaces/collision_a/shared.surface" );
		var b = GameResource.Load<Surface>( "mount://testgame/surfaces/collision_b/shared.surface" );
		Assert.IsNotNull( a );
		Assert.IsNotNull( b );
		Assert.AreEqual( a.ResourceName, b.ResourceName );
		Assert.AreNotEqual( a.Index, b.Index );
		Assert.AreSame( a, Surface.FindByName( a.PhysicsName ) );
		Assert.AreSame( b, Surface.FindByName( b.PhysicsName ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MountedSurfaceBindsToPhysicsShapesAndBodies( bool assignBody )
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var collider = scene.CreateObject().AddComponent<BoxCollider>();
			collider.Scale = new Vector3( 16 );
			if ( assignBody )
				collider.Shapes[0].Body.Surface = surface;
			else
				collider.Surface = surface;

			Assert.AreEqual( Path, collider.Shapes[0].SurfaceMaterial );
			var trace = scene.Trace.Ray( new Vector3( 0, 0, 32 ), new Vector3( 0, 0, -32 ) ).Run();
			Assert.IsTrue( trace.Hit );
			Assert.AreSame( surface, trace.Surface );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void ModelColliderPreservesMountedPhysicsSurface()
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		var builder = Model.Builder;
		builder.AddBody( 10, surface ).AddBox( new Vector3( 8 ) );
		var model = builder.Create();
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var collider = scene.CreateObject().AddComponent<ModelCollider>( false );
			collider.Model = model;
			collider.Enabled = true;

			Assert.AreEqual( Path, collider.Shapes[0].SurfaceMaterial );
			var trace = scene.Trace.Ray( new Vector3( 0, 0, 32 ), new Vector3( 0, 0, -32 ) ).Run();
			Assert.IsTrue( trace.Hit );
			Assert.AreSame( surface, trace.Surface );
			var sound = GameResource.Load<SoundEvent>( "mount://testgame/sounds/surface.sound" );
			Assert.IsNotNull( sound );
			Assert.AreSame( sound, trace.Surface.SoundCollection.ImpactHard );
			Assert.AreSame( sound, trace.Surface.SoundCollection.ScrapeRough );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void OrdinarySurfacesRetainResourceNameBindings()
	{
		using var context = new MountedResourceTestContext();
		var surface = Game.Resources.LoadGameResource<Surface>( "surfaces/default.surface", EngineFileSystem.CoreContent );
		Assert.IsNotNull( surface );
		Assert.AreEqual( surface.ResourceName, surface.PhysicsName );
		Assert.AreSame( surface, Surface.FindByName( surface.ResourceName ) );
	}

	[TestMethod]
	public void ContextResetReleasesSurfaceAndSoundReferences()
	{
		using var context = new MountedResourceTestContext();
		var surface = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( surface );
		var sound = surface.SoundCollection.FootLeft;
		Assert.IsNotNull( sound );

		GlobalContext.Current.Reset();

		Assert.IsFalse( surface.IsValid );
		Assert.IsFalse( sound.IsValid );
		Assert.IsNull( ResourceLibrary.Get<Surface>( Path ) );
		Assert.IsNull( ResourceLibrary.Get<SoundEvent>( sound.ResourcePath ) );
		Assert.AreEqual( 0, Surface.All.Count );

		var replacement = GameResource.Load<Surface>( Path );
		Assert.IsNotNull( replacement );
		Assert.AreNotSame( surface, replacement );
		Assert.AreNotSame( sound, replacement.SoundCollection.FootLeft );
		Assert.AreSame( replacement, Surface.FindByIndex( replacement.Index ) );
	}
}
