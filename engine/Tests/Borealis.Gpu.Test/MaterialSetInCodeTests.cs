using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Materials a game copied and changed in code, as Borealis.Gpu draws them (<c>BackendDevice.LoadMaterial</c> with values and
/// textures, <c>SetOnMaterial</c>): the compiled file's own values and textures, with what code set over them.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MaterialSetInCodeTests : GpuTest
{
	/// <summary>
	/// A texture and a tint set in code replace the file's, the file's other textures stay, and a later set lands on the same
	/// material, moving its version on.
	/// </summary>
	[TestMethod]
	public void SetValuesAndTexturesGoOverTheFile()
	{
		using var backend = new BackendDevice( Device );
		const string path = "materials/default/white.vmat";
		if ( backend.Content.Find( path ) is null ) Assert.Inconclusive( $"{path} isn't built" );

		using var screen = backend.CreateTexture( new TextureDesc { Width = 4, Height = 4, Depth = 1, Format = TextureFormat.RGBA8888, Samples = 1, Mips = 1, RenderTarget = true }, "Screen" );
		using var second = backend.CreateTexture( new TextureDesc { Width = 4, Height = 4, Depth = 1, Format = TextureFormat.RGBA8888, Samples = 1, Mips = 1, RenderTarget = true }, "Second screen" );

		// The file as its .shader draws it, what code-set copies start from
		var plain = (BackendMaterial)backend.LoadMaterial( path, null, null, null );
		var set = (BackendMaterial)backend.LoadMaterial( path, new Dictionary<string, float[]> { ["g_vColorTint"] = [1, 0, 0, 1] }, new Dictionary<string, GpuTexture> { ["g_tColor"] = screen } );

		Assert.AreSame( screen, set.Textures["g_tColor"], "the texture set in code" );
		foreach ( var (name, texture) in plain.Textures )
		{
			if ( name != "g_tColor" ) Assert.AreSame( texture, set.Textures[name], $"{name} stays the file's" );
		}

		var version = set.Version;
		backend.SetOnMaterial( set, null, new Dictionary<string, GpuTexture> { ["g_tColor"] = second } );
		Assert.AreSame( second, set.Textures["g_tColor"], "a later set lands on the same material" );
		Assert.AreNotEqual( version, set.Version, "and moves its version on" );
		Assert.AreNotSame( second, plain.Textures.GetValueOrDefault( "g_tColor" ), "the file's own material is untouched" );
	}
}
