using Sandbox.Resources;

namespace SceneTests.Components;

/// <summary>
/// Where a dragged-in image gets its identity from.
///
/// Dropping an image builds an <see cref="ImageFileGenerator"/> and generates a texture from it.
/// That texture is looked up later through a process-wide cache keyed by
/// <see cref="ResourceGenerator.GetHash"/>, which is a CRC64 of the generator serialized to JSON.
/// If two different images hash the same, both resolve to one texture - which is what "the sprites
/// swap to a single image" would look like.
/// </summary>
[TestClass]
public class TextureGeneratorIdentityTest
{
	static ImageFileGenerator ForFile( string path ) => new() { FilePath = path };

	/// <summary>
	/// The property that has to hold: two different files must never share an identity.
	/// </summary>
	[TestMethod]
	public void DifferentFilesMustHashDifferently()
	{
		var a = ForFile( "textures/one.png" );
		var b = ForFile( "textures/two.png" );

		Assert.AreNotEqual( a.GetHash(), b.GetHash(),
			"two different image files resolved to the same generated-texture identity" );
	}

	/// <summary>
	/// And the same file must hash stably, or the cache never hits and every load regenerates.
	/// </summary>
	[TestMethod]
	public void SameFileHashesStably()
	{
		Assert.AreEqual( ForFile( "textures/one.png" ).GetHash(), ForFile( "textures/one.png" ).GetHash() );
	}
}
