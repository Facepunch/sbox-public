using System.Collections.Generic;
using System.Collections.Specialized;

namespace Networking;

[TestClass]
public class NetDictionary
{
	[TestMethod]
	public void AddRemoveAndCount()
	{
		var dictionary = new NetDictionary<string, int>();
		Assert.IsTrue( dictionary.Count == 0 );

		dictionary.Add( "foo", 0 );
		Assert.IsTrue( dictionary.Count == 1 );
		Assert.AreEqual( dictionary["foo"], 0 );
		Assert.IsTrue( dictionary.ContainsKey( "foo" ) );

		dictionary.Remove( "foo" );
		Assert.IsTrue( dictionary.Count == 0 );
	}

	[TestMethod]
	public void Iterate()
	{
		var dictionary = new NetDictionary<string, int>();

		dictionary.Add( "a", 1 );
		dictionary.Add( "b", 2 );
		dictionary.Add( "c", 3 );

		var current = 0;
		foreach ( var (k, v) in dictionary )
		{
			var testKey = string.Empty;

			if ( current == 0 )
				testKey = "a";
			else if ( current == 1 )
				testKey = "b";
			else if ( current == 2 )
				testKey = "c";

			Assert.AreEqual( k, testKey );
			Assert.AreEqual( v, current + 1 );

			current++;
		}

		Assert.AreEqual( 3, current );

		Assert.AreEqual( 1, dictionary["a"] );
		Assert.AreEqual( 2, dictionary["b"] );
		Assert.AreEqual( 3, dictionary["c"] );
	}

	[TestMethod]
	public void OnChangedIsInvokedWhenItemIsAdded()
	{
		var dict = new NetDictionary<string, int>();

		var callCount = 0;
		NetDictionaryChangeEvent<string, int> receivedEvent = default;

		dict.OnChanged = ev =>
		{
			callCount++;
			receivedEvent = ev;
		};

		dict.Add( "foo", 42 );

		Assert.AreEqual( 1, callCount );
		Assert.AreEqual( NotifyCollectionChangedAction.Add, receivedEvent.Type );
		Assert.AreEqual( "foo", receivedEvent.Key );
		Assert.AreEqual( 42, receivedEvent.NewValue );
	}

	[TestMethod]
	public void OnChangedIsInvokedWhenItemIsRemoved()
	{
		var dict = new NetDictionary<string, int>();

		dict.Add( "foo", 10 );
		dict.Add( "bar", 20 );

		var callCount = 0;
		NetDictionaryChangeEvent<string, int> receivedEvent = default;

		dict.OnChanged = ev =>
		{
			callCount++;
			receivedEvent = ev;
		};

		dict.Remove( "foo" );

		Assert.AreEqual( 1, callCount );
		Assert.AreEqual( NotifyCollectionChangedAction.Remove, receivedEvent.Type );
		Assert.AreEqual( "foo", receivedEvent.Key );
		Assert.AreEqual( 10, receivedEvent.OldValue );
		Assert.IsFalse( dict.ContainsKey( "foo" ) );
	}

	[TestMethod]
	public void OnChangedIsInvokedWhenDictionaryIsCleared()
	{
		var dict = new NetDictionary<string, int>();

		dict.Add( "foo", 1 );
		dict.Add( "bar", 2 );

		var callCount = 0;
		NetDictionaryChangeEvent<string, int> receivedEvent = default;

		dict.OnChanged = ev =>
		{
			callCount++;
			receivedEvent = ev;
		};

		dict.Clear();

		Assert.AreEqual( 1, callCount );
		Assert.AreEqual( NotifyCollectionChangedAction.Reset, receivedEvent.Type );
		Assert.AreEqual( 0, dict.Count );
	}

	[TestMethod]
	public void ReplaceInvokesWithCorrectValues()
	{
		var dict = new NetDictionary<string, int>();

		dict.Add( "foo", 10 );

		var callCount = 0;
		NetDictionaryChangeEvent<string, int> receivedEvent = default;

		dict.OnChanged = ev =>
		{
			callCount++;
			receivedEvent = ev;
		};

		// This should represent a Replace: old 10 -> new 99
		dict["foo"] = 99;

		Assert.AreEqual( 1, callCount );

		Assert.AreEqual( NotifyCollectionChangedAction.Replace, receivedEvent.Type );
		Assert.AreEqual( "foo", receivedEvent.Key );
		Assert.AreEqual( 10, receivedEvent.OldValue );
		Assert.AreEqual( 99, receivedEvent.NewValue );

		Assert.AreEqual( 99, dict["foo"] );
	}

	[TestMethod]
	public void ValidAccess()
	{
		var dictionary = new NetDictionary<string, int>();

		Assert.ThrowsException<KeyNotFoundException>( () =>
		{
			var _ = dictionary["a"];
		} );
	}
}
