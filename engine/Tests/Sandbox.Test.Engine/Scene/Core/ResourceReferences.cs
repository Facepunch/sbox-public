using System.Text;
using Sandbox.Resources;

namespace SceneTests.Core;

[TestClass]
public class ResourceReferences
{
	static byte[] Compiled( params string[] references )
	{
		var writer = new ResourceWriter();
		writer.SetDataBlock( Encoding.UTF8.GetBytes( "{}" ) );
		foreach ( var reference in references ) writer.AddExternalReference( reference );
		return writer.ToArray();
	}

	/// <summary>
	/// A menu scene's set is everything its compiled references reach, through GameResources, and nothing else
	/// </summary>
	[TestMethod]
	public void FindReferencedFollowsCompiledReferences()
	{
		// Arrange
		var fs = new MemoryFileSystem();
		fs.CreateDirectory( "/scenes" );
		fs.CreateDirectory( "/prefabs" );
		fs.WriteAllBytes( "/scenes/menu.scene_c", Compiled( "prefabs/sign.prefab", "models/box.vmdl" ) );
		fs.WriteAllBytes( "/prefabs/sign.prefab_c", Compiled( "materials/sign.vmat", "scenes/menu.scene" ) );
		fs.WriteAllBytes( "/prefabs/unused.prefab_c", Compiled( "models/unused.vmdl" ) );

		// Act
		var found = ResourceLoader.FindReferenced( fs, ["/Scenes/Menu.scene"] );

		// Assert
		CollectionAssert.AreEquivalent(
			new[] { "scenes/menu.scene", "prefabs/sign.prefab", "models/box.vmdl", "materials/sign.vmat" },
			found.ToArray() );
	}

	/// <summary>
	/// A root that isn't there, or a compiled file without references, is just itself
	/// </summary>
	[TestMethod]
	public void FindReferencedKeepsRootsWithoutReferences()
	{
		// Arrange
		var fs = new MemoryFileSystem();
		fs.CreateDirectory( "/prefabs" );
		fs.WriteAllBytes( "/prefabs/plain.prefab_c", Compiled() );

		// Act
		var found = ResourceLoader.FindReferenced( fs, ["prefabs/plain.prefab", "prefabs/missing.prefab"] );

		// Assert
		CollectionAssert.AreEquivalent( new[] { "prefabs/plain.prefab", "prefabs/missing.prefab" }, found.ToArray() );
	}
}
