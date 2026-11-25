using Sandbox;
using Sandbox.Internal;
using System;
using System.IO;

namespace BytePackTests;

public class NoMembers
{
}

// A collection's count comes off the wire. A huge one, or elements that read no bytes at all,
// used to keep allocating long after the payload ran out.
[TestClass]
public class CollectionCountsTest
{
	static TypeLibrary library;

	[ClassInitialize]
	public static void Init( TestContext _ )
	{
		library = new TypeLibrary();
		library.AddAssembly( typeof( CollectionCountsTest ).Assembly, true );
	}

	static byte[] Dictionary( int count )
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );

		var identity = library.GetType<NoMembers>().Identity;

		w.Write( (byte)BytePack.Identifier.Dictionary );
		w.Write( count );
		w.Write( (byte)BytePack.Identifier.Runtime );
		w.Write( identity );
		w.Write( (byte)BytePack.Identifier.Runtime );
		w.Write( identity );

		w.Flush();
		return ms.ToArray();
	}

	[DataTestMethod]
	[DataRow( int.MaxValue )]
	[DataRow( 2 )]
	public void ForgedDictionaryCountThrows( int count )
	{
		Assert.ThrowsException<InvalidDataException>( () => library.FromBytes<object>( Dictionary( count ) ) );
	}

	[TestMethod]
	public void ForgedArrayCountThrows()
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );
		w.Write( (byte)BytePack.Identifier.Array );
		w.Write( int.MaxValue );
		w.Write( (byte)1 );
		w.Flush();

		Assert.ThrowsException<InvalidDataException>( () => library.FromBytes<object>( ms.ToArray() ) );
	}
}
