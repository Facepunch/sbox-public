
using System;
using System.Text.Json;

namespace SerializedObject;


public partial class From_TypeLibraryDictionary
{
	[TestMethod]
	public void Iterate_Properties()
	{
		var source = new CaseInsensitiveDictionary<string>();
		source["String"] = "Banana";

		var obj = typeLibrary.GetSerializedObjectDictionary<MyClass>( source );

		Assert.AreEqual( 8, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "String" ) );

		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()}" );
		}
	}
}
