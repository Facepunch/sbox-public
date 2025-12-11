
using System;

namespace SerializedObject;


public partial class From_TypeLibrary
{
	[TestMethod]
	public void GetSet_String()
	{
		var source = new MyClass();
		source.String = "Banana";

		var obj = typeLibrary.GetSerializedObject( source );

		var prop = obj.GetProperty( nameof( source.String ) );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<string>(), "Property value was null" );
		Assert.AreEqual( source.String, prop.GetValue<string>(), "Value doesn't match real value" );
		Assert.AreEqual( source.String, prop.As.String, "Value doesn't match real value" );

		// change value
		prop.SetValue( "Apple" );
		Assert.AreEqual( source.String, prop.GetValue<string>(), "Real object value didn't change" );
		Assert.AreEqual( source.String, prop.As.String, "Real object value didn't change" );
	}

	[TestMethod]
	public void GetSet_Vector3()
	{
		var source = new MyClass();
		source.Vector3 = new Vector3( 500, 400, 20.7f );

		var obj = typeLibrary.GetSerializedObject( source );

		var prop = obj.GetProperty( nameof( source.Vector3 ) );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<Vector3>(), "Property value was null" );
		Assert.IsNotNull( prop.GetValue<string>(), "Property value was null when rendered as a string" );
		Assert.AreEqual( source.Vector3, prop.GetValue<Vector3>(), "Value doesn't match real value" );
		Assert.AreEqual( source.Vector3, prop.As.Vector3, "Value doesn't match real value" );

		// change value
		prop.SetValue( new Vector3( 7, 5, 3 ) );
		Assert.AreEqual( source.Vector3, prop.GetValue<Vector3>(), "Real object value didn't change" );
		Assert.AreEqual( source.Vector3, prop.As.Vector3, "Real object value didn't change" );
	}

	[TestMethod]
	public void GetSet_Struct()
	{
		var source = new MyClass();
		source.DeepStruct = new MyDeepStruct
		{
			String = "Green",
			Color = Color.Green
		};

		var obj = typeLibrary.GetSerializedObject( source );

		var prop = obj.GetProperty( nameof( source.DeepStruct ) );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<MyDeepStruct>(), "Property value was null" );
		Assert.IsNotNull( prop.GetValue<string>(), "Property value was null when rendered as a string" );
		Assert.AreEqual( source.DeepStruct, prop.GetValue<MyDeepStruct>(), "Value doesn't match real value" );

		var ds = source.DeepStruct;
		ds.Color = Color.Red;
		ds.String = "Red";

		// change value
		prop.SetValue( ds );
		Assert.AreEqual( source.DeepStruct.String, prop.GetValue<MyDeepStruct>().String, "Real object value didn't change" );
		Assert.AreEqual( source.DeepStruct.Color, prop.GetValue<MyDeepStruct>().Color, "Real object value didn't change" );
		Assert.AreEqual( source.DeepStruct.String, ds.String, "Real object value didn't change" );
		Assert.AreEqual( source.DeepStruct.Color, ds.Color, "Real object value didn't change" );
	}

	[TestMethod]
	public void GetSet_VeryDeep()
	{
		var source = new MyClass();
		source.String = "Banana";
		source.DeepStruct = new MyDeepStruct { Vector = new Vector3( 2, 3, 4 ) };

		var obj = typeLibrary.GetSerializedObject( source );

		var deepStructProp = obj.GetProperty( nameof( source.DeepStruct ) );
		Assert.IsNotNull( deepStructProp, "Couldn't find DeepStruct property" );

		if ( !deepStructProp.TryGetAsObject( out var deepStructObj ) )
		{
			Assert.Fail();
		}

		foreach ( var prop in deepStructObj )
		{
			Console.WriteLine( $"{prop.Name} {prop.PropertyType}" );
		}

		var vectorProp = deepStructObj.GetProperty( "Vector" );
		Assert.IsNotNull( vectorProp, "Couldn't find MyDeepStruct.Vector property" );

		if ( !vectorProp.TryGetAsObject( out var vectorObj ) )
		{
			Assert.Fail();
		}

		Assert.AreEqual( 2, vectorObj.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 3, vectorObj.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 4, vectorObj.GetProperty( "z" ).As.Float );

		vectorObj.GetProperty( "x" ).As.Float = 10;
		vectorObj.GetProperty( "y" ).As.Float = 11;
		vectorObj.GetProperty( "z" ).As.Float = 12;

		Assert.AreEqual( 10, vectorObj.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 11, vectorObj.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 12, vectorObj.GetProperty( "z" ).As.Float );

		Assert.AreEqual( 10, source.DeepStruct.Vector.x );
		Assert.AreEqual( 11, source.DeepStruct.Vector.y );
		Assert.AreEqual( 12, source.DeepStruct.Vector.z );
	}

	[TestMethod]
	public void GetSet_MultipleObjects()
	{
		var source = new MyClass();
		source.String = "Banana";
		source.DeepStruct = new MyDeepStruct { Vector = new Vector3( 2, 3, 4 ) };

		var obj1 = typeLibrary.GetSerializedObject( source );
		var obj2 = typeLibrary.GetSerializedObject( source );

		var deepStructProp1 = obj1.GetProperty( nameof( source.DeepStruct ) );
		var deepStructProp2 = obj2.GetProperty( nameof( source.DeepStruct ) );
		Assert.IsNotNull( deepStructProp1, "Couldn't find DeepStruct property" );
		Assert.IsNotNull( deepStructProp2, "Couldn't find DeepStruct property" );

		if ( !deepStructProp1.TryGetAsObject( out var deepStructObj1 ) ) Assert.Fail();
		if ( !deepStructProp2.TryGetAsObject( out var deepStructObj2 ) ) Assert.Fail();

		var vectorProp1 = deepStructObj1.GetProperty( "Vector" );
		var vectorProp2 = deepStructObj2.GetProperty( "Vector" );
		Assert.IsNotNull( vectorProp1, "Couldn't find MyDeepStruct.Vector property" );
		Assert.IsNotNull( vectorProp2, "Couldn't find MyDeepStruct.Vector property" );

		if ( !vectorProp1.TryGetAsObject( out var vectorObj1 ) ) Assert.Fail();
		if ( !vectorProp2.TryGetAsObject( out var vectorObj2 ) ) Assert.Fail();

		Assert.AreEqual( 2, vectorObj1.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 3, vectorObj1.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 4, vectorObj1.GetProperty( "z" ).As.Float );
		Assert.AreEqual( 2, vectorObj2.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 3, vectorObj2.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 4, vectorObj2.GetProperty( "z" ).As.Float );

		vectorObj1.GetProperty( "x" ).As.Float = 10;
		vectorObj1.GetProperty( "y" ).As.Float = 11;
		vectorObj1.GetProperty( "z" ).As.Float = 12;

		Assert.AreEqual( 10, vectorObj1.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 11, vectorObj1.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 12, vectorObj1.GetProperty( "z" ).As.Float );

		Assert.AreEqual( 10, vectorObj2.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 11, vectorObj2.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 12, vectorObj2.GetProperty( "z" ).As.Float );

		Assert.AreEqual( 10, source.DeepStruct.Vector.x );
		Assert.AreEqual( 11, source.DeepStruct.Vector.y );
		Assert.AreEqual( 12, source.DeepStruct.Vector.z );

		vectorObj2.GetProperty( "x" ).As.Float = 50;
		vectorObj2.GetProperty( "y" ).As.Float = 51;
		vectorObj1.GetProperty( "z" ).As.Float = 52;

		Assert.AreEqual( 50, vectorObj1.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 51, vectorObj1.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 52, vectorObj1.GetProperty( "z" ).As.Float );

		Assert.AreEqual( 50, vectorObj2.GetProperty( "x" ).As.Float );
		Assert.AreEqual( 51, vectorObj2.GetProperty( "y" ).As.Float );
		Assert.AreEqual( 52, vectorObj2.GetProperty( "z" ).As.Float );

		Assert.AreEqual( 50, source.DeepStruct.Vector.x );
		Assert.AreEqual( 51, source.DeepStruct.Vector.y );
		Assert.AreEqual( 52, source.DeepStruct.Vector.z );
	}

	/// <summary>
	/// Can we convert between a bunch of number types automatically
	/// </summary>
	[TestMethod]
	public void GetSet_AutomaticConversion()
	{
		var source = new MyClass();
		source.Float = 33;

		var obj = typeLibrary.GetSerializedObject( source );
		var prop = obj.GetProperty( "Float" );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<MyDeepStruct>(), "Property value was null" );
		Assert.AreEqual( "33", prop.GetValue<string>() );
		Assert.AreEqual( 33, prop.GetValue<int>() );
		Assert.AreEqual( 33, prop.GetValue<float>() );
		Assert.AreEqual( 33, prop.GetValue<double>() );
		Assert.AreEqual( 33, prop.GetValue<decimal>() );

		prop.SetValue( "678" );

		Assert.AreEqual( "678", prop.GetValue<string>() );
		Assert.AreEqual( 678, prop.GetValue<int>() );
		Assert.AreEqual( 678, prop.GetValue<float>() );
		Assert.AreEqual( 678, prop.GetValue<double>() );
		Assert.AreEqual( 678, prop.GetValue<decimal>() );

		prop.SetValue( "8000.563" );

		Assert.AreEqual( "8000.563", prop.GetValue<string>() );
		Assert.AreEqual( 8000.563f, prop.GetValue<float>() );
		Assert.AreEqual( 8001, prop.GetValue<int>() );
		Assert.IsTrue( Math.Abs( 8000.563 - prop.GetValue<double>() ) < 0.001 );
		Assert.IsTrue( Math.Abs( 8000.563m - prop.GetValue<decimal>() ) < 0.001m );
	}
}
