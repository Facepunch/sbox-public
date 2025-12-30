
using System;

namespace SerializedObject;


public partial class From_TypeLibrary
{
	[TestMethod]
	public void Iterate_Properties()
	{
		var source = new MyClass();
		source.String = "Banana";

		var obj = typeLibrary.GetSerializedObject( source );

		foreach ( var property in obj )
		{
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()}" );
		}

		Assert.AreEqual( 9, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "String" ) );

		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()}" );
		}
	}
}
