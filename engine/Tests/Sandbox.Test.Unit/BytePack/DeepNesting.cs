using Sandbox;
using System;
using System.IO;

namespace BytePackTests;

// Regression tests for the recursion-depth DoS: a nested container payload must throw a
// catchable exception instead of overflowing the stack and killing the process.
// https://hackerone.com/reports/3796814
[TestClass]
public class DeepNestingTest : BaseRoundTrip
{
	// Wire bytes for object[1]{ object[1]{ ... } } nested `depth` levels deep, as ObjectArrayPacker.Write emits.
	static byte[] BuildNestedObjectArray( int depth )
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );

		// Each level: [Identifier.Array][int length = 1][byte kind = 1 (object element)]
		for ( int i = 0; i < depth; i++ )
		{
			w.Write( (byte)BytePack.Identifier.Array );
			w.Write( 1 );
			w.Write( (byte)1 );
		}

		w.Write( (byte)BytePack.Identifier.Null );

		w.Flush();
		return ms.ToArray();
	}

	[TestMethod]
	public void DeeplyNested_ObjectArray_ThrowsInsteadOfCrashing()
	{
		var payload = BuildNestedObjectArray( 100_000 );

		var ex = Assert.ThrowsException<Exception>( () => Deserialize( payload ) );
		StringAssert.Contains( ex.Message, "depth" );
	}

	[TestMethod]
	public void ModeratelyNested_ObjectArray_StillRoundTrips()
	{
		object nested = "leaf";
		for ( int i = 0; i < 10; i++ )
			nested = new object[] { nested };

		var result = Deserialize( Serialize( nested ) );

		for ( int i = 0; i < 10; i++ )
			result = ((object[])result)[0];

		Assert.AreEqual( "leaf", result );
	}
}
