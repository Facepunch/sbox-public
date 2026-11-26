using NativeEngine;
using System;

namespace TextureTests;

/// <summary>
/// A texture made at run time with a name is described by what it was made with, as an anonymous one is, without asking
/// Source 2's device; one Source 2 finds again by its name keeps the description it was made with.
/// </summary>
[TestClass]
public class NamedTextureDescriptionTests
{
	[TestMethod]
	public void NamedTextureIsDescribedByWhatMadeIt()
	{
		var undescribed = Texture.Undescribed;
		var texture = Texture.Create( 48, 24, ImageFormat.RGBA8888 ).WithMips( 3 ).WithName( "named_description_test" ).WithAnonymous( false ).Finish();

		Assert.AreEqual( 48, texture.Width );
		Assert.AreEqual( 24, texture.Height );
		Assert.AreEqual( 3, texture.Mips );
		Assert.AreEqual( ImageFormat.RGBA8888, texture.ImageFormat );
		Assert.AreEqual( undescribed, Texture.Undescribed, "It had a description" );
	}

	[TestMethod]
	public void FoundAgainByNameKeepsItsDescription()
	{
		var first = Texture.Create( 16, 16 ).WithName( "named_found_again_test" ).WithAnonymous( false ).Finish();
		var again = Texture.Create( 64, 64 ).WithName( "named_found_again_test" ).WithAnonymous( false ).Finish();

		// Source 2 found the first rather than making a second
		Assert.AreEqual( first.native.GetBindingPtr(), again.native.GetBindingPtr() );
		Assert.AreEqual( 16, again.Width );
		GC.KeepAlive( first );
	}
}
