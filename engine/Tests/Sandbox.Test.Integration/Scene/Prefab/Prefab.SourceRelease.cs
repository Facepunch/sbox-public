using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Sandbox.Resources;

namespace SceneTests.Prefab;

/// <summary>
/// A prefab's source json is dropped after each use when it can be read back from the compiled file -
/// and only then. Json that only exists in memory must never be dropped.
/// </summary>
[TestClass]
public class PrefabSourceReleaseTest
{
	static string PrefabJson( Guid guid, string childName ) => $$"""
		{
			"RootObject": {
				"__guid": "{{guid}}",
				"Name": "release_test",
				"Enabled": true,
				"Components": [],
				"Children": [
					{
						"__guid": "{{Guid.NewGuid()}}",
						"Name": "{{childName}}",
						"Enabled": true,
						"Components": [],
						"Children": []
					}
				]
			},
			"__version": 2
		}
		""";

	static byte[] Compile( string json )
	{
		var writer = new ResourceWriter();
		writer.SetDataBlock( Encoding.UTF8.GetBytes( json ) );
		return writer.ToArray();
	}

	static string ChildName( JsonObject root ) => root["Children"][0]["Name"].GetValue<string>();

	/// <summary>
	/// Load a compiled prefab from a temp mount the way the resource system does, run <paramref name="test"/>
	/// with a callback that rewrites the compiled file on disk.
	/// </summary>
	static void WithLoadedPrefab( Action<PrefabFile, Action<string>> test )
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var folder = Path.Combine( Path.GetTempPath(), $"prefab_release_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( folder );
		var mount = new LocalFileSystem( folder );
		FileSystem.Mounted.Mount( mount );

		try
		{
			const string path = "release_test.prefab";
			var compiledPath = Path.Combine( folder, path + "_c" );
			var guid = Guid.NewGuid();
			void WriteChild( string childName ) => File.WriteAllBytes( compiledPath, Compile( PrefabJson( guid, childName ) ) );

			WriteChild( "loaded" );

			var prefab = new PrefabFile();
			prefab.RegisterWeakResourceId( path );
			Assert.IsTrue( prefab.TryLoadFromData( File.ReadAllBytes( compiledPath ) ) );
			prefab.PostLoadInternal();

			test( prefab, WriteChild );
		}
		finally
		{
			FileSystem.Mounted.UnMount( mount );
			Directory.Delete( folder, true );
		}
	}

	[TestMethod]
	public void UnspawnedPrefabDropsItsJsonAfterLoad()
	{
		WithLoadedPrefab( ( prefab, writeChild ) =>
		{
			Assert.IsTrue( prefab.HasRootObject, "A dropped prefab still has data" );

			// If the json had been kept in memory, the read below would still see the loaded child
			writeChild( "on_disk" );

			var root = prefab.RootObject;
			Assert.AreEqual( "on_disk", ChildName( root ), "Read back from the compiled file" );
			Assert.AreSame( root, prefab.RootObject, "Read back once, then kept" );
		} );
	}

	[TestMethod]
	public void SpawnReadsTheJsonBackThenDropsIt()
	{
		WithLoadedPrefab( ( prefab, writeChild ) =>
		{
			writeChild( "spawned" );
			Assert.AreEqual( "spawned", prefab.GetScene().Children.Single().Name, "Cache built from the json on disk" );

			writeChild( "after_spawn" );
			Assert.AreEqual( "after_spawn", ChildName( prefab.RootObject ), "Dropped again once the cache was built" );
		} );
	}

	[TestMethod]
	public void InMemoryPrefabKeepsItsJson()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var root = JsonNode.Parse( PrefabJson( Guid.NewGuid(), "in_memory" ) )["RootObject"].DeepClone().AsObject();

		var prefab = new PrefabFile { RootObject = root };
		prefab.RegisterWeakResourceId( "in_memory_release_test.prefab" );

		Assert.IsNotNull( prefab.GetScene() );
		Assert.AreSame( root, prefab.RootObject, "Json that isn't on disk is never dropped" );
	}
}
