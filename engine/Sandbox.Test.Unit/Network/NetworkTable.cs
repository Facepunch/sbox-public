using System;
using System.Collections.Generic;
using System.Text.Json;
using Sandbox.Network;

namespace Networking;

[TestClass]
public class NetworkTable
{
	[TestMethod]
	public void SetValue()
	{
		var table = new Sandbox.Network.NetworkTable();
		table.Register( 3, new Sandbox.Network.NetworkTable.Entry { GetValue = () => false } );
		table.SetValue( 3, false );
		Assert.AreEqual( table.GetValue( 3 ), false );
	}

	[TestMethod]
	public void Changes()
	{
		bool value = false;
		var serverTable = new Sandbox.Network.NetworkTable();
		serverTable.Register( 3, new Sandbox.Network.NetworkTable.Entry { GetValue = () => value, SetValue = v => value = (bool)v } );
		serverTable.SetValue( 3, false );

		Assert.IsTrue( serverTable.HasAnyChanges );

		var bs = ByteStream.Create( 32 );
		serverTable.WriteChanged( ref bs );

		Assert.IsFalse( serverTable.HasAnyChanges );

		serverTable.Dispose();

		var serialized = bs.ToArray();
		var reader = ByteStream.CreateReader( serialized );

		{
			bool clientValue = true;
			using var clientTable = new Sandbox.Network.NetworkTable();
			clientTable.Register( 3, new Sandbox.Network.NetworkTable.Entry { GetValue = () => clientValue, SetValue = v => clientValue = (bool)v } );

			Assert.AreEqual( clientTable.GetValue( 3 ), true );

			clientTable.Read( ref reader );

			Assert.AreEqual( clientTable.GetValue( 3 ), false );
		}
	}

	[TestMethod]
	public void GetValue()
	{
		float value = 1.0f;

		var serverTable = new Sandbox.Network.NetworkTable();
		serverTable.Register( 3, new Sandbox.Network.NetworkTable.Entry { GetValue = () => value, SetValue = ( v ) => value = (float)v } );

		Assert.AreEqual( serverTable.GetValue( 3 ), value );

		value = 2.0f;

		Assert.AreEqual( serverTable.GetValue( 3 ), 2.0f );

		serverTable.QueryValues();
		Assert.IsTrue( serverTable.HasAnyChanges );


		var bs = ByteStream.Create( 32 );
		serverTable.WriteChanged( ref bs );

		Assert.IsFalse( serverTable.HasAnyChanges );

		serverTable.QueryValues();

		Assert.IsFalse( serverTable.HasAnyChanges );
	}

	public void ExchangeTest<T>( T a, T b, Action<T> modifyvalue = null )
	{
		// Init server table
		object serverValue = a;
		var serverTable = new Sandbox.Network.NetworkTable();
		serverTable.Register( 3, new Sandbox.Network.NetworkTable.Entry { TargetType = typeof( T ), GetValue = () => serverValue, SetValue = ( v ) => serverValue = v } );

		var client = new Sandbox.Network.NetworkTable();
		object clientValue = default;
		client.Register( 3, new Sandbox.Network.NetworkTable.Entry { TargetType = typeof( T ), GetValue = () => clientValue, SetValue = ( v ) => clientValue = v } );

		if ( serverValue != default )
		{
			Assert.AreNotEqual( serverValue, clientValue );
		}

		// exchange snapshot
		{
			var snapshot = ByteStream.Create( 32 );
			serverTable.WriteAll( ref snapshot );
			snapshot.Position = 0;

			// read the values
			client.Read( ref snapshot );

			AreEqual( serverValue, clientValue );
		}

		// server value change
		serverTable.SetValue( 3, b );
		Assert.IsTrue( serverTable.HasAnyChanges );
		Assert.AreNotEqual( serverValue, clientValue );

		// exchange update
		{
			var snapshot = ByteStream.Create( 32 );
			serverTable.WriteChanged( ref snapshot );
			snapshot.Position = 0;

			// read the values
			client.Read( ref snapshot );

			AreEqual( serverValue, clientValue );
		}

		// server value change
		if ( modifyvalue is not null )
		{
			serverTable.QueryValues();
			Assert.IsFalse( serverTable.HasAnyChanges );

			modifyvalue( (T)serverValue );

			serverTable.QueryValues();
			Assert.IsTrue( serverTable.HasAnyChanges );
			Assert.AreNotEqual( serverValue, clientValue );

			// exchange update
			{
				var snapshot = ByteStream.Create( 32 );
				serverTable.WriteChanged( ref snapshot );
				snapshot.Position = 0;

				// read the values
				client.Read( ref snapshot );

				AreEqual( serverValue, clientValue );
			}
		}

	}

	void AreEqual( object a, object b )
	{
		if ( a == null || b == null )
		{
			Assert.AreEqual( a, b );
			return;
		}

		var ta = a.GetType();
		var tb = b.GetType();

		Assert.AreEqual( ta, tb );

		var ja = JsonSerializer.Serialize( a );
		var jb = JsonSerializer.Serialize( b );

		Assert.AreEqual( ja, jb );
	}


	[TestMethod]
	public void ExchangeValues()
	{
		ExchangeTest<double>( 1.0, 2.0 );
		ExchangeTest<float>( 1.0f, 2.0f );
		ExchangeTest<int>( 1, 2 );
		ExchangeTest<uint>( 1, 2 );
		ExchangeTest<long>( 1, 2 );
		ExchangeTest<ulong>( 1, 2 );
		ExchangeTest<short>( 1, 2 );
		ExchangeTest<ushort>( 1, 2 );
		ExchangeTest<byte>( 1, 2 );
		ExchangeTest<uint>( 1, 2 );
	}

	[TestMethod]
	public void ExchangeGameValue()
	{
		ExchangeTest<Vector3>( Vector3.One, Vector3.One * 2 );
		ExchangeTest<Vector2>( Vector2.One, Vector2.One * 2 );
		ExchangeTest<Rotation>( Rotation.Identity, Rotation.From( 0, 0, 90 ) );
		ExchangeTest<Transform>( Transform.Zero, new Transform( Vector3.One, Rotation.From( 0, 0, 90 ), 2 ) );
	}

	[TestMethod]
	public void ExchangeString()
	{
		ExchangeTest<string>( "one", "two" );
		ExchangeTest<string>( "one", "" );
		ExchangeTest<string>( "", "two" );
		ExchangeTest<string>( null, "two" );
		ExchangeTest<string>( "two", null );
	}

	[TestMethod]
	public void ExchangeNetList()
	{
		ExchangeTest<NetList<int>>( new NetList<int> { 0, 1 }, new NetList<int> { 1, 0 }, t => t.Add( 4 ) );
		ExchangeTest<NetList<float>>( new NetList<float> { 0, 1 }, new NetList<float> { 1, 0 }, t => t.RemoveAt( 0 ) );
	}

	[TestMethod]
	public void ExchangeNetDictionary()
	{
		ExchangeTest<NetDictionary<int, int>>( new NetDictionary<int, int> { [0] = 1, [1] = 0 }, new NetDictionary<int, int> { [0] = 0, [1] = 1 }, t => t.Add( 2, 1 ) );
		ExchangeTest<NetDictionary<string, int>>( new NetDictionary<string, int> { ["Foo"] = 0, ["Bar"] = 1 }, new NetDictionary<string, int> { ["Foo"] = 1, ["Bar"] = 0 }, t => t.Add( "Other", 2 ) );
		ExchangeTest<NetDictionary<string, int>>( new NetDictionary<string, int> { ["Foo"] = 0, ["Bar"] = 1 }, new NetDictionary<string, int> { ["Foo"] = 1, ["Bar"] = 0 }, t => t.Remove( "Foo" ) );
	}

	[TestMethod]
	public void ExchangeList()
	{
		ExchangeTest<List<int>>( new List<int> { 0, 1 }, new List<int> { 1, 0 }, t => t.Add( 4 ) );
		ExchangeTest<List<float>>( new List<float> { 0, 1, 9, 4 }, new List<float> { 1, 0 }, t => t.Add( 4.0f ) );
		ExchangeTest<List<string>>( new List<string> { "a", "b" }, new List<string> { "orange", "apple" }, t => t.Add( "christ" ) );
	}

	[TestMethod]
	public void ExchangeDictionary()
	{
		ExchangeTest<Dictionary<int, int>>( new Dictionary<int, int> { [0] = 1, [1] = 0 }, new Dictionary<int, int> { [0] = 0, [1] = 1 }, t => t.Add( 2, 1 ) );
		ExchangeTest<Dictionary<string, int>>( new Dictionary<string, int> { ["Foo"] = 0, ["Bar"] = 1 }, new Dictionary<string, int> { ["Foo"] = 1, ["Bar"] = 0 }, t => t.Add( "Other", 2 ) );
	}
}
