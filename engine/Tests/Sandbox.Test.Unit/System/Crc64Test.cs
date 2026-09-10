using Sandbox.Utility;
using System.IO;
using System.Text;

namespace SystemTests;

[TestClass]
public class Crc64Test
{
	/// <summary>
	/// Pin the hash against externally computed reference values (bit-by-bit CRC,
	/// reflected, poly 0x9A6C9329AC4BC9B5, init/xorout ~0). CRCs are persisted in
	/// the wild, so any silent change to the table/polynomial must fail here.
	/// </summary>
	[TestMethod]
	public void KnownAnswers()
	{
		Assert.AreEqual( 0xAE8B14860A799888UL, Crc64.FromString( "123456789" ) );
		Assert.AreEqual( 0x8D29D5C3F6EA8EBEUL, Crc64.FromString( "hello world" ) );
		Assert.AreEqual( 0x66329EAD6D81547FUL, Crc64.FromString( "the quick brown fox" ) );
		Assert.AreEqual( 0x0000000000000000UL, Crc64.FromString( "" ) );
	}

	/// <summary>
	/// The same input must always hash to the same value, and different
	/// inputs should hash differently.
	/// </summary>
	[TestMethod]
	public void Deterministic()
	{
		Assert.AreEqual( Crc64.FromString( "hello world" ), Crc64.FromString( "hello world" ) );
		Assert.AreNotEqual( Crc64.FromString( "hello world" ), Crc64.FromString( "hello worle" ) );
	}

	/// <summary>
	/// The hash must be order sensitive - transposed content is different content.
	/// </summary>
	[TestMethod]
	public void OrderSensitive()
	{
		Assert.AreNotEqual( Crc64.FromString( "ab" ), Crc64.FromString( "ba" ) );
	}

	/// <summary>
	/// Hashing the same data as a string, a byte array and a stream should
	/// all agree.
	/// </summary>
	[TestMethod]
	public void StringBytesAndStreamAgree()
	{
		const string text = "the quick brown fox";
		var bytes = Encoding.UTF8.GetBytes( text );

		var fromString = Crc64.FromString( text );
		var fromBytes = Crc64.FromBytes( bytes );

		using var stream = new MemoryStream( bytes );
		var fromStream = Crc64.FromStream( stream );

		Assert.AreEqual( fromString, fromBytes );
		Assert.AreEqual( fromBytes, fromStream );
	}

	/// <summary>
	/// The async stream overload should produce the same hash as the
	/// synchronous one.
	/// </summary>
	[TestMethod]
	public async Task AsyncStreamAgrees()
	{
		var bytes = Encoding.UTF8.GetBytes( "async hashing" );

		using var stream = new MemoryStream( bytes );
		var fromAsync = await Crc64.FromStreamAsync( stream );

		Assert.AreEqual( Crc64.FromBytes( bytes ), fromAsync );
	}

	/// <summary>
	/// Empty input should hash consistently and differ from non-empty input.
	/// </summary>
	[TestMethod]
	public void EmptyInput()
	{
		var empty = Crc64.FromBytes( System.Array.Empty<byte>() );

		Assert.AreEqual( empty, Crc64.FromString( "" ) );
		Assert.AreNotEqual( empty, Crc64.FromString( "x" ) );
	}
}
