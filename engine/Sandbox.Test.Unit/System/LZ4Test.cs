using Sandbox.Compression;
using System;
using System.Text;

namespace SystemTest;

[TestClass]
public class LZ4Test
{
	// maybe we should use some big real-world data here? 
	const string TestText = "DENTAL PLAN LISA NEEDS BRACES";
	string BigText = string.Concat( Enumerable.Repeat( TestText, 256 ) );

	[TestMethod]
	public void RoundTripBlock()
	{
		byte[] original = Encoding.UTF8.GetBytes( BigText );
		var compressed = LZ4.CompressBlock( original );

		var result = new byte[original.Length];
		int resultLength = LZ4.DecompressBlock( compressed, result.AsSpan() );
		Assert.AreEqual( original.Length, resultLength );

		Assert.IsTrue( original.AsSpan().SequenceEqual( result ) );
	}

	[TestMethod]
	public void RoundTripFrame()
	{
		byte[] original = Encoding.UTF8.GetBytes( BigText );

		var compressed = LZ4.CompressFrame( original );
		var decompressed = LZ4.DecompressFrame( compressed );

		Assert.IsTrue( original.AsSpan().SequenceEqual( decompressed ) );
	}
}
