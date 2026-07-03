using System;
using System.IO.Hashing;

namespace Sandbox.Test.Unit;

// Pins xxhash3 output. It feeds the network protocol, so a change means bumping Protocol.Network/Api.
[TestClass]
public class FastHashStabilityTests
{
	// Canonical XXH3-64 reference vectors (seed 0). Frozen since xxHash v0.8.0.
	[TestMethod]
	public void CanonicalXxHash3ReferenceVectors()
	{
		Assert.AreEqual( 0x2D06800538D394C2UL, XxHash3.HashToUInt64( ReadOnlySpan<byte>.Empty ) );
		Assert.AreEqual( 0x78AF5F94892F3950UL, XxHash3.HashToUInt64( "abc"u8 ) );
	}

	// Pinned engine FastHash output for fixed inputs.
	[TestMethod]
	public void FastHashOutputIsStable()
	{
		Assert.AreEqual( 0x6E1B911F175FBAC2UL, "hello".FastHash64() );
		Assert.AreEqual( 392149698, "hello".FastHash() );

		Assert.AreEqual( 0xB1642670647639DFUL, "/prefab/test.prefab".FastHash64() );
		Assert.AreEqual( 1685469663, "/prefab/test.prefab".FastHash() );

		Assert.AreEqual( 0x4E6315D1A0F0FEE2UL, "sandbox.test.fasthash.pin".FastHash64() );
		Assert.AreEqual( -1594818846, "sandbox.test.fasthash.pin".FastHash() );
	}
}
