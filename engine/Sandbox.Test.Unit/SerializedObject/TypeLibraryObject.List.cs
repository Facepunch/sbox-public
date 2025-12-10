
using System;
using System.Collections.Generic;

namespace SerializedObject;


public partial class From_TypeLibrary
{
	[TestMethod]
	public void List_Type()
	{
		var source = new List<string>() { "one", "two", "three" };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()} ({property.PropertyType})" );
		}

		Assert.AreEqual( 3, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "0" ) );

		// adding one via collection
		obj.Add( "four" );
		Assert.AreEqual( 4, obj.Count() );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()} ({property.PropertyType})" );
		}

		// removing specific
		obj.RemoveAt( 2 );
		Assert.AreEqual( 3, obj.Count() );

		// Adding one manually
		source.Add( "five" );
		Assert.AreEqual( 4, obj.Count() );
	}

	[TestMethod]
	public void List_Value()
	{
		var source = new List<Vector3>() { Vector3.Up, Vector3.Down };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<Vector3>()} ({property.PropertyType})" );
		}

		Assert.AreEqual( 2, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == "0" ) );

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
