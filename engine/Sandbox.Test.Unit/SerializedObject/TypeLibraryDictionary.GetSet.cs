
using System;
using System.Text.Json;

namespace SerializedObject;


public partial class From_TypeLibraryDictionary
{
	[TestMethod]
	public void GetSet_String()
	{
		var source = new CaseInsensitiveDictionary<string>();
		source["String"] = "Banana";

		var obj = typeLibrary.GetSerializedObjectDictionary<MyClass>( source );

		var prop = obj.GetProperty( "String" );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<string>(), "Property value was null" );
		Assert.AreEqual( source["String"], prop.GetValue<string>(), "Value doesn't match real value" );
		Assert.AreEqual( source["String"], prop.As.String, "Value doesn't match real value" );

		// change value
		prop.SetValue( "Apple" );
		Assert.AreEqual( source["String"], prop.GetValue<string>(), "Real object value didn't change" );
		Assert.AreEqual( source["String"], prop.As.String, "Real object value didn't change" );
		Assert.AreEqual( source["String"], "Apple", "Real object value didn't change" );
		Assert.AreEqual( prop.As.String, "Apple", "Real object value didn't change" );
		Assert.AreEqual( prop.GetValue<string>(), "Apple", "Real object value didn't change" );
	}

	[TestMethod]
	public void GetSet_Vector3()
	{
		var source = new CaseInsensitiveDictionary<string>();
		source["Vector3"] = "[500, 400, 20.7]";

		var obj = typeLibrary.GetSerializedObjectDictionary<MyClass>( source );

		var prop = obj.GetProperty( "Vector3" );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<Vector3>(), "Property value was null" );
		Assert.IsNotNull( prop.GetValue<string>(), "Property value was null when rendered as a string" );
		Assert.AreEqual( new Vector3( 500, 400, 20.7f ), prop.GetValue<Vector3>(), "Value doesn't match real value" );
		Assert.AreEqual( new Vector3( 500, 400, 20.7f ), prop.As.Vector3, "Value doesn't match real value" );

		// change value
		prop.SetValue( new Vector3( 7, 5, 3 ) );
		Assert.AreEqual( new Vector3( 7, 5, 3 ), prop.GetValue<Vector3>(), "Real object value didn't change" );
		Assert.AreEqual( new Vector3( 7, 5, 3 ), prop.As.Vector3, "Real object value didn't change" );
	}

	[TestMethod]
	public void GetSet_Struct()
	{
		var source = new CaseInsensitiveDictionary<string>();

		{
			var deepStruct = new MyDeepStruct
			{
				String = "Green",
				Color = Color.Green
			};

			source["DeepStruct"] = JsonSerializer.Serialize( deepStruct );
		}

		var obj = typeLibrary.GetSerializedObjectDictionary<MyClass>( source );

		var prop = obj.GetProperty( "DeepStruct" );

		Assert.IsNotNull( prop, "Couldn't find property" );
		Assert.IsNotNull( prop.GetValue<MyDeepStruct>(), "Property value was null" );
		Assert.IsNotNull( prop.GetValue<string>(), "Property value was null when rendered as a string" );
		//Assert.AreEqual( source.DeepStruct, prop.GetValue<MyDeepStruct>(), "Value doesn't match real value" );

		var ds = new MyDeepStruct();
		ds.Color = Color.Red;
		ds.String = "Red";

		// change value
		prop.SetValue( ds );
		Assert.AreEqual( "Red", prop.GetValue<MyDeepStruct>().String, "Real object value didn't change" );
		Assert.AreEqual( Color.Red, prop.GetValue<MyDeepStruct>().Color, "Real object value didn't change" );
	}
}
