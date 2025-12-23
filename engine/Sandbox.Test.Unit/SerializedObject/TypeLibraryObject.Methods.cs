using System;

namespace SerializedObject;


public partial class From_TypeLibrary
{
	[TestMethod]
	public void InvokeMethod()
	{
		var source = new MyClass();
		source.String = "Banana";
		source.Color = Color.Green;

		var obj = typeLibrary.GetSerializedObject( source );

		foreach ( var property in obj )
		{
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()}" );
		}

		var method = obj.Single( x => x.Name == "SetColorRed" );

		method.Invoke();

		Assert.AreEqual( source.Color, Color.Red );
	}
}
