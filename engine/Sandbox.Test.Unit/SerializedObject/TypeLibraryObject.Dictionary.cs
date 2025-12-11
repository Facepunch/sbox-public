
using System;
using System.Collections.Generic;

namespace SerializedObject;


public partial class From_TypeLibrary
{
	[TestMethod]
	public void Dictionary_Type()
	{
		var source = new Dictionary<string, string>() { { "A", "one" }, { "B", "two" }, { "C", "three" } };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Assert.IsNotNull( property.GetKey() );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()} ({property.PropertyType})" );
		}

		Assert.AreEqual( 3, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "A" ) );

		// changing a key
		obj.FirstOrDefault().GetKey().SetValue( "Z" );
		Assert.AreEqual( 3, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "Z" ) );
		Assert.AreEqual( "one", source["Z"] );

		// adding one via collection
		obj.Add( "D", "four" );
		Assert.AreEqual( 4, obj.Count() );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()} ({property.PropertyType})" );
		}

		// removing specific
		obj.RemoveAt( "B" );
		Assert.AreEqual( 3, obj.Count() );

		// Adding one manually
		source["e"] = "five";
		Assert.AreEqual( 4, obj.Count() );
	}

	[TestMethod]
	public void Dictionary_Value()
	{
		var source = new Dictionary<int, Vector3>() { { 1, Vector3.Up }, { 3, Vector3.Down } };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<Vector3>()} ({property.PropertyType})" );
		}

		Assert.AreEqual( 2, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "1" ) );

		Assert.IsTrue( obj.First().TryGetAsObject( out var vectorObject ) );

		vectorObject.GetProperty( "x" ).SetValue( 32.0f );
		vectorObject.GetProperty( "y" ).SetValue( 33.0f );
		vectorObject.GetProperty( "z" ).SetValue( 34.0f );

		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<Vector3>()} ({property.PropertyType})" );
		}

	}
}
