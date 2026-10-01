using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sandbox.Resources;

namespace SceneTests.Prefab;

/// <summary>
/// A prefab's source json is released once its cache scene is built, when it can be read back from the
/// compiled file - and only then. Json that only exists in memory must never be dropped.
/// </summary>
[TestClass]
public class PrefabSourceReleaseTest
{
	static string PrefabJson( bool enabled ) => $$"""
		{
			"RootObject": {
				"__guid": "5b4b7a3e-1a7f-4d33-9a0e-3c2f6d8e9b10",
				"Name": "release_test",
				"Enabled": {{(enabled ? "true" : "false")}},
				"Components": [],
				"Children": []
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

	[TestMethod]
	public void CachedPrefabReadsItsJsonBackFromDisk()
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
			File.WriteAllBytes( compiledPath, Compile( PrefabJson( enabled: true ) ) );

			var prefab = new PrefabFile();
			prefab.RegisterWeakResourceId( path );
			Assert.IsTrue( prefab.TryLoadFromData( File.ReadAllBytes( compiledPath ) ) );

			Assert.IsNotNull( prefab.GetScene() );
			Assert.IsTrue( prefab.HasRootObject, "A released prefab still has data" );

			// Change the file: if the json had been kept in memory, the read below would still say enabled
			File.WriteAllBytes( compiledPath, Compile( PrefabJson( enabled: false ) ) );

			var root = prefab.RootObject;
			Assert.IsNotNull( root );
			Assert.IsFalse( root["Enabled"].GetValue<bool>(), "Read back from the compiled file" );
			Assert.AreEqual( "5b4b7a3e-1a7f-4d33-9a0e-3c2f6d8e9b10", root["__guid"].GetValue<string>() );
			Assert.AreSame( root, prefab.RootObject, "Read back once, then kept" );
		}
		finally
		{
			FileSystem.Mounted.UnMount( mount );
			Directory.Delete( folder, true );
		}
	}

	[TestMethod]
	public void InMemoryPrefabKeepsItsJson()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var root = JsonNode.Parse( PrefabJson( enabled: true ) )["RootObject"].DeepClone().AsObject();

		var prefab = new PrefabFile { RootObject = root };
		prefab.RegisterWeakResourceId( "in_memory_release_test.prefab" );

		Assert.IsNotNull( prefab.GetScene() );
		Assert.AreSame( root, prefab.RootObject, "Json that isn't on disk is never released" );
	}
}
