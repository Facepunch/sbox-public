using System.Linq;

using Sandbox.Mounting;
using ResourceLoader = Sandbox.Mounting.ResourceLoader;

namespace MountingTests;

/// <summary>
/// Tests for <see cref="SceneLoader{T}"/> - mounts that build a <see cref="SceneFile"/> in code.
/// </summary>
[TestClass]
public class MountedSceneTest
{
	Configuration Config => new();

	async Task<(MountHost Host, ResourceLoader Loader)> MountSceneSource()
	{
		var host = new MountHost( Config );
		host.RegisterTypes( GetType().Assembly );

		await host.Mount( "testgame" );

		var source = host.GetSource( "testgame" );
		var loader = source.Resources.FirstOrDefault( x => x.Type == ResourceType.Scene );

		Assert.IsNotNull( loader, "testgame didn't expose a scene resource" );

		return (host, loader);
	}

	[TestMethod]
	public async Task SceneLoaderProducesRegisteredSceneFile()
	{
		var (host, loader) = await MountSceneSource();
		using ( host )
		{
			var scene = await loader.GetOrCreate() as SceneFile;

			Assert.IsNotNull( scene, "SceneLoader didn't produce a SceneFile" );
			Assert.IsTrue( scene.GameObjects is { Length: > 0 }, "SceneFile has no GameObjects" );

			// The loader registers it in the resource library under its mount path
			Assert.AreSame( scene, SceneFile.Load( loader.Path ), "Built scene wasn't registered/loadable by path" );
		}
	}

	[TestMethod]
	public async Task SceneLoaderCachesResult()
	{
		var (host, loader) = await MountSceneSource();
		using ( host )
		{
			var a = await loader.GetOrCreate() as SceneFile;
			var b = await loader.GetOrCreate() as SceneFile;

			Assert.IsNotNull( a );
			Assert.AreSame( a, b, "Loading the same resource twice should return the same SceneFile" );
		}
	}

	[TestMethod]
	public async Task SceneLoaderHasDeterministicId()
	{
		var (host, loader) = await MountSceneSource();
		using ( host )
		{
			var scene = await loader.GetOrCreate() as SceneFile;

			Assert.IsNotNull( scene );
			Assert.AreEqual( loader.Path.ToGuid(), scene.Id, "Scene id should be derived deterministically from its path" );
		}
	}

	[TestMethod]
	public async Task BuiltSceneCanBeLoadedIntoAScene()
	{
		var (host, loader) = await MountSceneSource();
		using ( host )
		{
			var sceneFile = await loader.GetOrCreate() as SceneFile;
			Assert.IsNotNull( sceneFile );

			var scene = new Scene();
			using var sceneScope = scene.Push();

			var options = new SceneLoadOptions();
			options.SetScene( sceneFile );
			scene.Load( options );

			var root = scene.Children.FirstOrDefault( x => x.Name == "MountedRoot" );
			Assert.IsNotNull( root, "Built scene didn't contain the root object" );

			var renderer = root.Components.Get<ModelRenderer>();
			Assert.IsNotNull( renderer, "Root object lost its ModelRenderer" );
			Assert.AreEqual( "models/dev/box.vmdl", renderer.Model?.ResourcePath, "ModelRenderer lost its model" );

			var child = root.Children.FirstOrDefault( x => x.Name == "MountedChild" );
			Assert.IsNotNull( child, "Built scene didn't preserve the child object" );
		}
	}

	[TestMethod]
	public async Task DisposingMountUnregistersTheSceneFile()
	{
		var (host, loader) = await MountSceneSource();

		var scene = await loader.GetOrCreate() as SceneFile;
		Assert.IsNotNull( scene );

		var path = loader.Path;
		Assert.IsNotNull( SceneFile.Load( path ), "Scene should be loadable while mounted" );

		host.Dispose();

		Assert.IsNull( SceneFile.Load( path ), "Scene should be unregistered after the mount is disposed" );
	}
}
