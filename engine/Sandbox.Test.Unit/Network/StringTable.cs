using Sandbox.Network;

namespace Networking;

[TestClass]
public class StringTable
{
	private void StringTablesMatch( Sandbox.Network.StringTable a, Sandbox.Network.StringTable b )
	{
		Assert.AreEqual( a.Entries.Count, b.Entries.Count );

		foreach ( var e in a.Entries )
		{
			Assert.IsTrue( b.Entries.TryGetValue( e.Key, out var d ) );
			Assert.IsTrue( d.Data.SequenceEqual( e.Value.Data ) );
		}
	}

	[TestMethod]
	public void Set()
	{
		Sandbox.Network.StringTable table = new( "Assembly", true );

		table.Set( "TestAssembly", new byte[883712] );

		Assert.AreEqual( 1, table.Entries.Count );
	}

	void DoUpdateExchange( Sandbox.Network.StringTable a, Sandbox.Network.StringTable b )
	{
		Assert.IsTrue( a.HasChanged );

		var bs = ByteStream.Create( 1024 );
		a.BuildUpdateMessage( ref bs );
		var updateData = bs.ToArray();
		bs.Dispose();
		Assert.IsTrue( a.HasChanged );
		a.ClearChanges();
		Assert.IsFalse( a.HasChanged );

		Assert.AreNotEqual( 0, updateData.Length );

		var reader = ByteStream.CreateReader( updateData );
		b.ReadUpdate( reader );
		reader.Dispose();
	}

	void DoSnapshotExchange( Sandbox.Network.StringTable a, Sandbox.Network.StringTable b )
	{
		var bs = ByteStream.Create( 1024 );
		a.BuildSnapshotMessage( ref bs );
		var snapshotData = bs.ToArray();
		bs.Dispose();

		Assert.AreNotEqual( 0, snapshotData.Length );

		var reader = ByteStream.CreateReader( snapshotData );
		b.Reset();

		b.ReadSnapshot( reader );
		reader.Dispose();
	}

	[TestMethod]
	public void Updates()
	{
		Sandbox.Network.StringTable a = new( "Assembly", true );

		a.Set( "TestAssembly", new byte[883712] );
		a.Set( "OtherAssembly", new byte[2345] );

		Assert.AreEqual( 2, a.Entries.Count );

		Sandbox.Network.StringTable b = new( "Assembly", true );

		DoUpdateExchange( a, b );

		Assert.AreEqual( a.Entries.Count, b.Entries.Count );

		a.Remove( "TestAssembly" );
		Assert.AreEqual( 1, a.Entries.Count );

		DoUpdateExchange( a, b );

		Assert.AreEqual( a.Entries.Count, b.Entries.Count );

		a.Set( "HelloThere", new byte[2345] );
		Assert.AreEqual( 2, a.Entries.Count );

		DoUpdateExchange( a, b );

		Assert.AreEqual( a.Entries.Count, b.Entries.Count );
	}

	[TestMethod]
	public void EmptyEntries()
	{
		Sandbox.Network.StringTable a = new( "Assembly", true );

		// start with an empty entry
		a.Set( "TestAssembly", new byte[0] );
		a.Set( "OtherAssembly", new byte[2345] );

		Assert.AreEqual( 2, a.Entries.Count );
		Sandbox.Network.StringTable b = new( "Assembly", true );
		DoUpdateExchange( a, b );
		StringTablesMatch( a, b );

		// Change to empty
		a.Set( "OtherAssembly", new byte[0] );
		DoUpdateExchange( a, b );
		StringTablesMatch( a, b );

		// change to not empty
		a.Set( "TestAssembly", new byte[43] );
		a.Set( "OtherAssembly", new byte[56] );
		DoUpdateExchange( a, b );
		StringTablesMatch( a, b );

		// change to empty again
		a.Set( "OtherAssembly", new byte[0] );
		DoSnapshotExchange( a, b );
		StringTablesMatch( a, b );
	}

}
