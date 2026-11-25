using Borealis.Backend;

namespace BorealisGpuTests;

/// <summary>
/// The scene attributes of materials whose <c>.shader</c> is gone (a surface alone): what the scene reads for an object's
/// layers (<c>overlay</c>, <c>bWantsFBCopyTexture</c>, <c>NoZPrepass</c>) comes from the surface's module for the
/// material's features, as it came from the <c>.shader</c>'s static combos.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SurfaceAttributeTests : GpuTest
{
	/// <summary>
	/// A static_overlay material is an overlay, so a map's decals draw in the static overlay layer and not the translucent
	/// one; default (refractive) glass wants the frame buffer copy; standard's materials are neither.
	/// </summary>
	[TestMethod]
	public void SurfacesAloneAnswerTheirModulesAttributes()
	{
		using var backend = new BackendDevice( Device );
		if ( backend.CreateRawMaterial( "raw overlay", "shaders/static_overlay.shader" ) is not ConvertedMaterial overlay ) { Assert.Inconclusive( "static_overlay isn't drawn as a surface here" ); return; }
		if ( backend.CreateRawMaterial( "raw glass", "shaders/glass.shader" ) is not ConvertedMaterial glass ) { Assert.Inconclusive( "glass isn't drawn as a surface here" ); return; }
		if ( backend.CreateRawMaterial( "raw complex", "shaders/complex.shader" ) is not ConvertedMaterial standard ) { Assert.Inconclusive( "standard isn't drawn as a surface here" ); return; }

		Assert.IsTrue( overlay.Attribute( "overlay", false ), "static_overlay declares overlay" );
		Assert.IsFalse( overlay.Attribute( "NoZPrepass", false ), "an opaque overlay (blend mode 0) isn't NoZPrepass" );
		Assert.IsTrue( glass.Attribute( "bWantsFBCopyTexture", false ), "refractive glass (quality 0) reads the frame buffer copy" );
		Assert.IsFalse( glass.Attribute( "overlay", false ) );
		Assert.IsFalse( standard.Attribute( "overlay", false ) );
		Assert.IsFalse( standard.Attribute( "bWantsFBCopyTexture", false ) );
		Assert.IsTrue( standard.Attribute( "overlay", true ), "an attribute the module doesn't declare is the fallback" );
	}
}
