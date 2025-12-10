using System;
using System.Collections.Generic;

namespace SerializedObject;

public partial class From_TypeLibrary
{
	[TestMethod]
	public void HashSet_String()
	{
		var source = new HashSet<string>() { "one", "two", "three" };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()} ({property.PropertyType})" );
		}

		Assert.AreEqual( 3, obj.Count() );

		// Verify values exist by looking through properties
		Assert.IsNotNull( obj.FirstOrDefault( x => x.GetValue<string>() == "one" ) );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.GetValue<string>() == "two" ) );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.GetValue<string>() == "three" ) );

		// adding one via collection
		obj.Add( "four" );
		Assert.AreEqual( 4, obj.Count() );
		Assert.IsTrue( source.Contains( "four" ) );

		// iterate/list after adding
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<string>()} ({property.PropertyType})" );
		}

		// Try adding a duplicate (should fail)
		bool added = obj.Add( "four" );
		Assert.IsFalse( added );
		Assert.AreEqual( 4, obj.Count() );

		// removing specific
		var threeProperty = obj.FirstOrDefault( x => x.GetValue<string>() == "three" );
		Assert.IsNotNull( threeProperty );
		obj.Remove( threeProperty );
		Assert.AreEqual( 3, obj.Count() );
		Assert.IsFalse( source.Contains( "three" ) );

		// Removing by value
		obj.RemoveAt( "two" );
		Assert.AreEqual( 2, obj.Count() );
		Assert.IsFalse( source.Contains( "two" ) );

		// Adding one manually to source
		source.Add( "five" );
		Assert.AreEqual( 3, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.GetValue<string>() == "five" ) );
	}

	[TestMethod]
	public void HashSet_Value()
	{
		var source = new HashSet<Vector3>() { { Vector3.Up }, { Vector3.Down } };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// iterate/list
		foreach ( var property in obj )
		{
			Assert.IsNotNull( property );
			Console.WriteLine( $"{property.Name} = {property.GetValue<Vector3>()} ({property.PropertyType})" );
		}

		Assert.AreEqual( 2, obj.Count() );
		Assert.IsNotNull( obj.FirstOrDefault( x => x.Name == Vector3.Up.ToString() ) );

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

	[TestMethod]
	public void HashSet_ModifyElement()
	{
		var source = new HashSet<string>() { "one", "two", "three" };

		var obj = typeLibrary.GetSerializedObject( source ) as SerializedCollection;
		Assert.IsNotNull( obj );

		// Find property for "two"
		var twoProperty = obj.FirstOrDefault( x => x.GetValue<string>() == "two" );
		Assert.IsNotNull( twoProperty );

		// Modify "two" to "modified"
		twoProperty.SetValue( "modified" );

		// Original should be gone, new value should be present
		Assert.IsFalse( source.Contains( "two" ) );
		Assert.IsTrue( source.Contains( "modified" ) );
		Assert.AreEqual( 3, obj.Count() );

		// Verify the new element exists in the serialized object
		Assert.IsNotNull( obj.FirstOrDefault( x => x.GetValue<string>() == "modified" ) );
	}
}
