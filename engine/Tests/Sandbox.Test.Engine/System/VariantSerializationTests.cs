using System;

namespace SystemTests;

[TestClass]
public class VariantSerializationTest
{
	/// <summary>
	/// Helper method to serialize, deserialize, and assert a Variant round-trip.
	/// </summary>
	private void AssertRoundTrip<T>( T expectedValue )
	{
		// 1. Setup
		var original = new Variant { Value = expectedValue };

		// 2. Serialize
		string json = Json.Serialize( original );

		// 3. Deserialize
		var deserialized = Json.Deserialize<Variant>( json );

		// 4. Assert
		Assert.AreEqual( typeof( T ), deserialized.Type, $"Type mismatch. Expected {typeof( T )}, got {deserialized.Type}" );
		Assert.AreEqual( expectedValue, deserialized.Value, "Values do not match after deserialization." );
	}

	[TestMethod]
	public void Float()
	{
		AssertRoundTrip<float>( 123.456f );
	}

	[TestMethod]
	public void Double()
	{
		AssertRoundTrip<double>( 987.654321 );
	}

	[TestMethod]
	public void Int()
	{
		AssertRoundTrip<int>( 42 );
	}

	[TestMethod]
	public void Bool()
	{
		AssertRoundTrip<bool>( true );
		AssertRoundTrip<bool>( false );
	}

	[TestMethod]
	public void Vector3()
	{
		AssertRoundTrip<Vector3>( new Vector3( 10f, 20.5f, -30f ) );
	}

	[TestMethod]
	public void ColorValue()
	{
		AssertRoundTrip<Color>( Color.Red );
	}

	[TestMethod]
	public void String_FastPath()
	{
		string expected = "Hello s&box!";
		Variant original = "Hello s&box!";

		string json = Json.Serialize( original );

		// Verify the fast-path writer worked (should just be a raw JSON string, no object wrapper)
		Assert.AreEqual( $"\"{expected}\"", json );

		var deserialized = Json.Deserialize<Variant>( json );
		Assert.AreEqual( typeof( string ), deserialized.Type );
		Assert.AreEqual( expected, deserialized.Value );
	}

	[TestMethod]
	public void Null()
	{
		var original = new Variant { Value = null };

		string json = Json.Serialize( original );

		// Assuming your writer outputs "null" when t == null
		Assert.AreEqual( "null", json );

		var deserialized = Json.Deserialize<Variant>( json );
		Assert.IsNull( deserialized.Value );
		Assert.IsNull( deserialized.Type );
	}

	[TestMethod]
	public void Vector2()
	{
		AssertRoundTrip<Vector2>( new Vector2( 5f, -10f ) );
	}

	[TestMethod]
	public void Vector4()
	{
		AssertRoundTrip<Vector4>( new Vector4( 1f, 2f, 3f, 4f ) );
	}

	[TestMethod]
	public void Default()
	{
		var v = default( Variant );

		Assert.IsNull( v.Type );
		Assert.IsNull( v.Value );

		string json = Json.Serialize( v );
		Assert.AreEqual( "null", json );

		var deserialized = Json.Deserialize<Variant>( json );
		Assert.IsNull( deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void ImplicitOperator_Int()
	{
		Variant v = 42;

		Assert.AreEqual( typeof( int ), v.Type );
		Assert.AreEqual( 42, v.Value );
	}

	[TestMethod]
	public void ImplicitOperator_Float()
	{
		Variant v = 3.14f;

		Assert.AreEqual( typeof( float ), v.Type );
		Assert.AreEqual( 3.14f, v.Value );
	}

	[TestMethod]
	public void ImplicitOperator_Bool()
	{
		Variant v = true;

		Assert.AreEqual( typeof( bool ), v.Type );
		Assert.AreEqual( true, v.Value );
	}

	[TestMethod]
	public void ImplicitOperator_String()
	{
		Variant v = "test";

		Assert.AreEqual( typeof( string ), v.Type );
		Assert.AreEqual( "test", v.Value );
	}

	[TestMethod]
	public void ImplicitOperator_Vector3()
	{
		Variant v = new Vector3( 1f, 2f, 3f );

		Assert.AreEqual( typeof( Vector3 ), v.Type );
		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), v.Value );
	}

	[TestMethod]
	public void ImplicitOperator_Color()
	{
		Variant v = Color.Blue;

		Assert.AreEqual( typeof( Color ), v.Type );
		Assert.AreEqual( Color.Blue, v.Value );
	}

	[TestMethod]
	public void Get()
	{
		Variant v = 42;
		Assert.AreEqual( 42, v.Get<int>() );

		v = "hello";
		Assert.AreEqual( "hello", v.Get<string>() );

		v = new Vector3( 1f, 2f, 3f );
		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), v.Get<Vector3>() );
	}

	[TestMethod]
	public void Get_InvalidCast()
	{
		Variant v = 42;
		Assert.ThrowsException<InvalidCastException>( () => v.Get<string>() );
	}

	[TestMethod]
	public void ToStringConversion()
	{
		Variant v = 42;
		Assert.AreEqual( "42", v.ToString() );

		v = "hello";
		Assert.AreEqual( "hello", v.ToString() );

		v = true;
		Assert.AreEqual( "True", v.ToString() );

		var nullVariant = new Variant();
		Assert.IsNull( nullVariant.ToString() );
	}

	[TestMethod]
	public void Equality_SameValue()
	{
		Variant a = 42;
		Variant b = 42;

		Assert.IsTrue( a.Equals( b ) );
		Assert.IsTrue( a == b );
		Assert.IsFalse( a != b );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
	}

	[TestMethod]
	public void Equality_DifferentValue()
	{
		Variant a = 42;
		Variant b = 99;

		Assert.IsFalse( a.Equals( b ) );
		Assert.IsFalse( a == b );
		Assert.IsTrue( a != b );
	}

	[TestMethod]
	public void Equality_DifferentType()
	{
		Variant a = 42;
		Variant b = "42";

		Assert.IsFalse( a == b );
	}

	[TestMethod]
	public void Equality_BothNull()
	{
		var a = new Variant();
		var b = new Variant();

		Assert.IsTrue( a == b );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
	}

	[TestMethod]
	public void Reassign_ChangesType()
	{
		var v = new Variant( 42 );
		Assert.AreEqual( typeof( int ), v.Type );

		v.Value = "hello";
		Assert.AreEqual( typeof( string ), v.Type );
		Assert.AreEqual( "hello", v.Value );
	}

	[TestMethod]
	public void String_SpecialCharacters()
	{
		AssertRoundTrip<string>( "Hello \"world\"" );
		AssertRoundTrip<string>( "line1\nline2" );
		AssertRoundTrip<string>( "tab\there" );
		AssertRoundTrip<string>( "unicode: \u00e9\u00e0\u00fc\u2603" );
		AssertRoundTrip<string>( "" );
	}

	[TestMethod]
	public void EmptyObject_Json()
	{
		var deserialized = Json.Deserialize<Variant>( "{}" );
		Assert.IsNull( deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void ExtraProperties_Json()
	{
		// Extra properties should be skipped without error
		var deserialized = Json.Deserialize<Variant>( "{\"t\":\"System.Int32\",\"v\":42,\"extra\":\"ignored\"}" );
		Assert.AreEqual( typeof( int ), deserialized.Type );
		Assert.AreEqual( 42, deserialized.Value );
	}

	[TestMethod]
	public void MalformedJson_MissingType()
	{
		// "v" without "t" - value should be deferred then dropped since type is unknown
		var deserialized = Json.Deserialize<Variant>( "{\"v\":42}" );
		Assert.IsNull( deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void MalformedJson_MissingValue()
	{
		// "t" without "v"
		var deserialized = Json.Deserialize<Variant>( "{\"t\":\"System.Int32\"}" );
		Assert.AreEqual( typeof( int ), deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void Json_ValueBeforeType()
	{
		// "v" comes before "t" - exercises the deferred deserialization path
		var deserialized = Json.Deserialize<Variant>( "{\"v\":42,\"t\":\"System.Int32\"}" );
		Assert.AreEqual( typeof( int ), deserialized.Type );
		Assert.AreEqual( 42, deserialized.Value );
	}
}
