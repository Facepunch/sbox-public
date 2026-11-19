namespace TextureTests;

[TestClass]
public class TextureMipBindingTests
{
	[TestMethod]
	public void BoundMipIsClampedToTextureMips()
	{
		using var texture = Texture.Create( 8, 8 ).WithMips( 3 ).Finish();
		if ( texture is null || texture.Mips < 1 )
			Assert.Inconclusive( "Requires a render device that creates textures." );

		Assert.AreEqual( texture.Mips - 1, RenderAttributes.ClampMip( texture, 1000 ) );
		Assert.AreEqual( 0, RenderAttributes.ClampMip( texture, -5 ) );
	}
}
