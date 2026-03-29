using System;

namespace SystemTest;

[TestClass]
public class VariantSerializationTests
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
		Assert.IsNotNull( deserialized, "Deserialized Variant should not be null." );
		Assert.AreEqual( typeof( T ), deserialized.Type, $"Type mismatch. Expected {typeof( T )}, got {deserialized.Type}" );
		Assert.AreEqual( expectedValue, deserialized.Value, "Values do not match after deserialization." );
	}

	[TestMethod]
	public void Test_Variant_Float()
	{
		AssertRoundTrip<float>( 123.456f );
	}

	[TestMethod]
	public void Test_Variant_Double()
	{
		AssertRoundTrip<double>( 987.654321 );
	}

	[TestMethod]
	public void Test_Variant_Int()
	{
		AssertRoundTrip<int>( 42 );
	}

	[TestMethod]
	public void Test_Variant_Bool()
	{
		AssertRoundTrip<bool>( true );
		AssertRoundTrip<bool>( false );
	}

	[TestMethod]
	public void Test_Variant_Vector3()
	{
		AssertRoundTrip<Vector3>( new Vector3( 10f, 20.5f, -30f ) );
	}

	[TestMethod]
	public void Test_Variant_Color()
	{
		AssertRoundTrip<Color>( Color.Red );
	}

	[TestMethod]
	public void Test_Variant_String_FastPath()
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
	public void Test_Variant_Null()
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
	public void Test_Variant_Vector2()
	{
		AssertRoundTrip<Vector2>( new Vector2( 5f, -10f ) );
	}

	[TestMethod]
	public void Test_Variant_Vector4()
	{
		AssertRoundTrip<Vector4>( new Vector4( 1f, 2f, 3f, 4f ) );
	}

	[TestMethod]
	public void Test_Variant_Default()
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
	public void Test_Variant_ImplicitOperator_Int()
	{
		Variant v = 42;

		Assert.AreEqual( typeof( int ), v.Type );
		Assert.AreEqual( 42, v.Value );
	}

	[TestMethod]
	public void Test_Variant_ImplicitOperator_Float()
	{
		Variant v = 3.14f;

		Assert.AreEqual( typeof( float ), v.Type );
		Assert.AreEqual( 3.14f, v.Value );
	}

	[TestMethod]
	public void Test_Variant_ImplicitOperator_Bool()
	{
		Variant v = true;

		Assert.AreEqual( typeof( bool ), v.Type );
		Assert.AreEqual( true, v.Value );
	}

	[TestMethod]
	public void Test_Variant_ImplicitOperator_String()
	{
		Variant v = "test";

		Assert.AreEqual( typeof( string ), v.Type );
		Assert.AreEqual( "test", v.Value );
	}

	[TestMethod]
	public void Test_Variant_ImplicitOperator_Vector3()
	{
		Variant v = new Vector3( 1f, 2f, 3f );

		Assert.AreEqual( typeof( Vector3 ), v.Type );
		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), v.Value );
	}

	[TestMethod]
	public void Test_Variant_ImplicitOperator_Color()
	{
		Variant v = Color.Blue;

		Assert.AreEqual( typeof( Color ), v.Type );
		Assert.AreEqual( Color.Blue, v.Value );
	}

	[TestMethod]
	public void Test_Variant_Get()
	{
		Variant v = 42;
		Assert.AreEqual( 42, v.Get<int>() );

		v = "hello";
		Assert.AreEqual( "hello", v.Get<string>() );

		v = new Vector3( 1f, 2f, 3f );
		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), v.Get<Vector3>() );
	}

	[TestMethod]
	public void Test_Variant_Get_InvalidCast()
	{
		Variant v = 42;
		Assert.ThrowsException<InvalidCastException>( () => v.Get<string>() );
	}

	[TestMethod]
	public void Test_Variant_ToString()
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
	public void Test_Variant_Equality_SameValue()
	{
		Variant a = 42;
		Variant b = 42;

		Assert.IsTrue( a.Equals( b ) );
		Assert.IsTrue( a == b );
		Assert.IsFalse( a != b );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
	}

	[TestMethod]
	public void Test_Variant_Equality_DifferentValue()
	{
		Variant a = 42;
		Variant b = 99;

		Assert.IsFalse( a.Equals( b ) );
		Assert.IsFalse( a == b );
		Assert.IsTrue( a != b );
	}

	[TestMethod]
	public void Test_Variant_Equality_DifferentType()
	{
		Variant a = 42;
		Variant b = "42";

		Assert.IsFalse( a == b );
	}

	[TestMethod]
	public void Test_Variant_Equality_BothNull()
	{
		var a = new Variant();
		var b = new Variant();

		Assert.IsTrue( a == b );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
	}

	[TestMethod]
	public void Test_Variant_Reassign_ChangesType()
	{
		var v = new Variant( 42 );
		Assert.AreEqual( typeof( int ), v.Type );

		v.Value = "hello";
		Assert.AreEqual( typeof( string ), v.Type );
		Assert.AreEqual( "hello", v.Value );
	}

	[TestMethod]
	public void Test_Variant_String_SpecialCharacters()
	{
		AssertRoundTrip<string>( "Hello \"world\"" );
		AssertRoundTrip<string>( "line1\nline2" );
		AssertRoundTrip<string>( "tab\there" );
		AssertRoundTrip<string>( "unicode: \u00e9\u00e0\u00fc\u2603" );
		AssertRoundTrip<string>( "" );
	}

	[TestMethod]
	public void Test_Variant_EmptyObject_Json()
	{
		var deserialized = Json.Deserialize<Variant>( "{}" );
		Assert.IsNull( deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void Test_Variant_ExtraProperties_Json()
	{
		// Extra properties should be skipped without error
		var deserialized = Json.Deserialize<Variant>( "{\"t\":\"System.Int32\",\"v\":42,\"extra\":\"ignored\"}" );
		Assert.AreEqual( typeof( int ), deserialized.Type );
		Assert.AreEqual( 42, deserialized.Value );
	}

	[TestMethod]
	public void Test_Variant_MalformedJson_MissingType()
	{
		// "v" without "t" - value should be deferred then dropped since type is unknown
		var deserialized = Json.Deserialize<Variant>( "{\"v\":42}" );
		Assert.IsNull( deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void Test_Variant_MalformedJson_MissingValue()
	{
		// "t" without "v"
		var deserialized = Json.Deserialize<Variant>( "{\"t\":\"System.Int32\"}" );
		Assert.AreEqual( typeof( int ), deserialized.Type );
		Assert.IsNull( deserialized.Value );
	}

	[TestMethod]
	public void Test_Variant_Json_ValueBeforeType()
	{
		// "v" comes before "t" - exercises the deferred deserialization path
		var deserialized = Json.Deserialize<Variant>( "{\"v\":42,\"t\":\"System.Int32\"}" );
		Assert.AreEqual( typeof( int ), deserialized.Type );
		Assert.AreEqual( 42, deserialized.Value );
	}
}
