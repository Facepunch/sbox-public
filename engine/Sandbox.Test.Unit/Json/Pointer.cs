using System;
using System.Text.Json.Nodes;

namespace JsonTest;

[TestClass]
public class JsonPointerTests
{
	[TestMethod]
	public void Parse_EmptyPath_ReturnsRoot()
	{
		var pointer = new Json.Pointer( "" );
		Assert.AreEqual( "/", pointer.ToString() );
		Assert.AreEqual( 0, pointer.ReferenceTokens.Length );
	}

	[TestMethod]
	[ExpectedException( typeof( ArgumentException ) )]
	public void Parse_InvalidPath_ThrowsArgumentException()
	{
		new Json.Pointer( "invalid" ); // Missing leading '/'
	}

	[TestMethod]
	public void Parse_ValidPath_ReturnsCorrectPointer()
	{
		var pointer = new Json.Pointer( "/a/0/b/1" );
		Assert.AreEqual( "/a/0/b/1", pointer.ToString() );
	}

	[TestMethod]
	public void Parse_EscapedCharacters_HandlesCorrectly()
	{
		var pointer = new Json.Pointer( "/~1path~1to~1resource/~0tilde" );
		Assert.AreEqual( "/~1path~1to~1resource/~0tilde", pointer.ToString() );

		var tokens = pointer.ReferenceTokens;
		Assert.AreEqual( 2, tokens.Length );
		Assert.AreEqual( "/path/to/resource", tokens[0] );
		Assert.AreEqual( "~tilde", tokens[1] );
	}

	[TestMethod]
	public void Append_Field_CreatesNewPointer()
	{
		var pointer = new Json.Pointer( "/" );
		pointer = pointer.Append( "a" ).Append( "b" );
		Assert.AreEqual( "/a/b", pointer.ToString() );
	}

	[TestMethod]
	public void Append_Index_CreatesNewPointer()
	{
		var pointer = new Json.Pointer( "/" );
		pointer = pointer.Append( "arr" ).Append( "0" ).Append( "1" );
		Assert.AreEqual( "/arr/0/1", pointer.ToString() );
	}

	[TestMethod]
	public void GetParent_ReturnsCorrectPointer()
	{
		var pointer = new Json.Pointer( "/a/b/c" );
		var parent = pointer.GetParent();
		Assert.AreEqual( "/a/b", parent.ToString() );
	}

	[TestMethod]
	public void GetParent_OnRoot_ReturnsRoot()
	{
		var pointer = new Json.Pointer( "/" );
		var parent = pointer.GetParent();
		Assert.AreEqual( "/", parent.ToString() );
	}

	[TestMethod]
	public void Evaluate_SimpleObject_ReturnsCorrectElement()
	{
		var json = JsonNode.Parse(
			"""
			{"a":{"b":"value"}}
			""" );
		var pointer = new Json.Pointer( "/a/b" );
		var result = pointer.Evaluate( json );
		Assert.AreEqual( "value", result.ToString() );
	}

	[TestMethod]
	public void Evaluate_Array_ReturnsCorrectElement()
	{
		var json = JsonNode.Parse(
			"""
			{"arr":["first","second"]}
			""" );
		var pointer = new Json.Pointer( "/arr/1" );
		var result = pointer.Evaluate( json );
		Assert.AreEqual( "second", result.ToString() );
	}

	[TestMethod]
	[ExpectedException( typeof( InvalidOperationException ) )]
	public void Evaluate_InvalidArrayIndex_ThrowsException()
	{
		var json = JsonNode.Parse(
			"""
			{"arr":["first","second"]}
			""" );
		var pointer = new Json.Pointer( "/arr/2" );
		pointer.Evaluate( json );
	}

	[TestMethod]
	[ExpectedException( typeof( InvalidOperationException ) )]
	public void Evaluate_MissingProperty_ThrowsException()
	{
		var json = JsonNode.Parse(
			"""
			{"a":{"b":"value"}}
			""" );
		var pointer = new Json.Pointer( "/a/c" );
		pointer.Evaluate( json );
	}

	[TestMethod]
	public void Evaluate_ComplexPath_ReturnsCorrectElement()
	{
		var json = JsonNode.Parse(
			"""
			{
				"foo": {
					"bar": {
						"baz": [
							{"qux": "value"},
							{"qux": "target"}
						]
					}
				}
			}
			""" );
		var pointer = new Json.Pointer( "/foo/bar/baz/1/qux" );
		var result = pointer.Evaluate( json );
		Assert.AreEqual( "target", result.ToString() );
	}

	[TestMethod]
	public void Evaluate_NumericObjectKeys_ReturnsCorrectElement()
	{
		var json = JsonNode.Parse(
			"""
			{
				"obj": {
					"0": "zero as string key",
					"1": {
						"42": "nested numeric key",
						"arr": [
							"array element"
						]
					}
				},
				"arr": [
					"first",
					"second"
				]
			}
			""" );

		// Test numeric key in object (should not be treated as array index)
		var pointer1 = new Json.Pointer( "/obj/0" );
		var result1 = pointer1.Evaluate( json );
		Assert.AreEqual( "zero as string key", result1.ToString() );

		// Test nested numeric key
		var pointer2 = new Json.Pointer( "/obj/1/42" );
		var result2 = pointer2.Evaluate( json );
		Assert.AreEqual( "nested numeric key", result2.ToString() );

		// Test array index after numeric object keys
		var pointer3 = new Json.Pointer( "/obj/1/arr/0" );
		var result3 = pointer3.Evaluate( json );
		Assert.AreEqual( "array element", result3.ToString() );

		// Test actual array index for comparison
		var pointer4 = new Json.Pointer( "/arr/0" );
		var result4 = pointer4.Evaluate( json );
		Assert.AreEqual( "first", result4.ToString() );
	}

	[TestMethod]
	public void Evaluate_EscapedCharacters_ReturnsCorrectElement()
	{
		var json = JsonNode.Parse(
			"""
			{
				"~tilde": {
					"slash/path": "value"
				}
			}
			""" );
		var pointer = new Json.Pointer( "/~0tilde/slash~1path" );
		var result = pointer.Evaluate( json );
		Assert.AreEqual( "value", result.ToString() );
	}
}
