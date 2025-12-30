using Sandbox.Localization;
using System.Collections.Generic;

namespace SystemTest;

[TestClass]
public class LocalizationTest
{
	[TestMethod]
	public void ParserMultiple()
	{
		var phrase = new Phrase( "Hello {one} then {two} and the end" );
		Assert.AreEqual( "Hello ", phrase.Parts[0] );
		Assert.AreEqual( "{one}", phrase.Parts[1] );
		Assert.AreEqual( " then ", phrase.Parts[2] );
		Assert.AreEqual( "{two}", phrase.Parts[3] );
		Assert.AreEqual( " and the end", phrase.Parts[4] );
		Assert.AreEqual( phrase.Parts.Length, 5 );
	}

	[TestMethod]
	public void ParserNone()
	{
		var phrase = new Phrase( "Hello one then two" );
		Assert.AreEqual( null, phrase.Parts );
	}

	[TestMethod]
	public void ParserStarting()
	{
		var phrase = new Phrase( "{one} then" );
		Assert.AreEqual( "{one}", phrase.Parts[0] );
		Assert.AreEqual( " then", phrase.Parts[1] );
		Assert.AreEqual( phrase.Parts.Length, 2 );
	}

	[TestMethod]
	public void ParserEnding()
	{
		var phrase = new Phrase( "then {two}" );
		Assert.AreEqual( "then ", phrase.Parts[0] );
		Assert.AreEqual( "{two}", phrase.Parts[1] );
		Assert.AreEqual( phrase.Parts.Length, 2 );
	}

	[TestMethod]
	public void ParserStartingEnding()
	{
		var phrase = new Phrase( "{one} then {two}" );
		Assert.AreEqual( "{one}", phrase.Parts[0] );
		Assert.AreEqual( " then ", phrase.Parts[1] );
		Assert.AreEqual( "{two}", phrase.Parts[2] );
		Assert.AreEqual( phrase.Parts.Length, 3 );
	}

	[TestMethod]
	public void BasicTranslation()
	{
		var lang = new PhraseCollection();

		lang.Set( "hello.world", "Hello World!" );

		Assert.AreEqual( "Hello World!", lang.GetPhrase( "hello.world" ) );
		Assert.AreEqual( "hello", lang.GetPhrase( "hello" ) );
	}

	[TestMethod]
	public void BasicInterpolation()
	{
		var lang = new PhraseCollection();

		lang.Set( "hello.player", "Hello {PlayerName}!" );

		var data = new Dictionary<string, object>();
		data["PlayerName"] = "Garry";

		// With Data
		Assert.AreEqual( "Hello Garry!", lang.GetPhrase( "hello.player", data ) );

		// With No data
		Assert.AreEqual( "Hello {PlayerName}!", lang.GetPhrase( "hello.player" ) );

		// With Wrong Data
		data.Clear();
		Assert.AreEqual( "Hello {PlayerName}!", lang.GetPhrase( "hello.player", data ) );
	}
}
