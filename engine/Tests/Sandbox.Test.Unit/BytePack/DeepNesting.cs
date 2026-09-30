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

	// A dead end only because DictionaryPacker leaves TargetType null, so MakeGenericType rejects
	// it before recursing. Pinned so giving it one can't quietly open the path up.
	static byte[] BuildNestedDictionary( int depth )
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );

		// Deserialize eats this one, then hands off to DictionaryPacker.Read.
		w.Write( (byte)BytePack.Identifier.Dictionary );

		for ( int i = 0; i < depth; i++ )
		{
			w.Write( 1 );                                        // one entry
			w.Write( (byte)BytePack.Identifier.Int );            // key handler
			w.Write( (byte)BytePack.Identifier.Dictionary );     // value handler - would recurse
			w.Write( 0 );                                        // the key itself
		}

		w.Write( 0 );
		w.Write( (byte)BytePack.Identifier.Int );
		w.Write( (byte)BytePack.Identifier.Int );

		w.Flush();
		return ms.ToArray();
	}

	[TestMethod]
	public void NestedDictionary_IsRejectedBeforeRecursing()
	{
		Assert.ThrowsException<ArgumentNullException>( () => Deserialize( BuildNestedDictionary( 10_000 ) ) );
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
