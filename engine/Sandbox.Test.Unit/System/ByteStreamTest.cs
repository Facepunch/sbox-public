using System;
using static Sandbox.IByteParsable;

namespace SystemTest;

public class MyDataClass : IByteParsable<MyDataClass>
{
	public string DataContents;

	public static object ReadObject( ref ByteStream stream, ByteParseOptions o ) => Read( ref stream, o );
	public static void WriteObject( ref ByteStream stream, object value, ByteParseOptions o ) => Write( ref stream, value as MyDataClass, o );

	public static MyDataClass Read( ref ByteStream stream, ByteParseOptions o )
	{
		if ( stream.Read<byte>() == 0 )
			return default;

		var value = new MyDataClass();
		value.DataContents = stream.Read<string>();

		return value;
	}

	public static void Write( ref ByteStream stream, MyDataClass value, ByteParseOptions o )
	{
		if ( value is null )
		{
			stream.Write( (byte)0 );
			return;
		}

		stream.Write( (byte)1 );
		stream.Write( value.DataContents );
	}
}

public class MyDataClassChild : MyDataClass
{

}

public struct MyPlainOldDataClass : IByteParsable<MyPlainOldDataClass>
{
	public Vector3 Position;

	public static object ReadObject( ref ByteStream stream, ByteParseOptions o ) => Read( ref stream, o );
	public static void WriteObject( ref ByteStream stream, object value, ByteParseOptions o ) => Write( ref stream, (MyPlainOldDataClass)value, o );

	public static MyPlainOldDataClass Read( ref ByteStream stream, ByteParseOptions o )
	{
		var value = new MyPlainOldDataClass();
		value.Position = stream.Read<Vector3>();

		return value;
	}

	public static void Write( ref ByteStream stream, MyPlainOldDataClass value, ByteParseOptions o )
	{
		stream.Write( value.Position );
	}
}

[TestClass]
public class ByteStreamTest
{
	[TestMethod]
	public void WriterBasic()
	{
		var s = ByteStream.Create( 512 );

		s.Write( (int)32 );
		Assert.AreEqual( 4, s.Position );
		s.Write( (uint)32 );
		Assert.AreEqual( 8, s.Position );
		s.Write( (short)32 );
		Assert.AreEqual( 10, s.Position );
		s.Write( (ushort)32 );
		Assert.AreEqual( 12, s.Position );
		s.Write( (long)32 );
		Assert.AreEqual( 20, s.Position );
		s.Write( (ulong)32 );
		Assert.AreEqual( 28, s.Position );
		s.Write( Vector2.One );
		Assert.AreEqual( 36, s.Position );
		s.Write( Vector3.One );
		Assert.AreEqual( 48, s.Position );
		s.Write( Vector4.One );
		Assert.AreEqual( 64, s.Position );
		s.Write( Transform.Zero );
		s.Write( "hello there this is a big string" );

		Console.WriteLine( $"Written {s.Position}b" );

		s.Dispose();
	}

	[TestMethod]
	public void ReadWriteInt()
	{
		var s = ByteStream.Create( 512 );

		s.Write( 32 );
		s.Write( 64 );
		s.Write( 512 );

		var data = s.ToArray();

		s.Dispose();

		s = ByteStream.CreateReader( data );
		Assert.AreEqual( 32, s.Read<int>() );
		Assert.AreEqual( 64, s.Read<int>() );
		Assert.AreEqual( 512, s.Read<int>() );

		s.Dispose();
	}

	[TestMethod]
	public void ReadWriteFloat()
	{
		var s = ByteStream.Create( 512 );

		s.Write( 32.0f );
		s.Write( 64.0f );
		s.Write( 512.0f );

		var data = s.ToArray();

		s.Dispose();

		s = ByteStream.CreateReader( data );
		Assert.AreEqual( 32.0f, s.Read<float>() );
		Assert.AreEqual( 64.0f, s.Read<float>() );
		Assert.AreEqual( 512.0f, s.Read<float>() );

		s.Dispose();
	}

	[TestMethod]
	public void ReadWriteVector3()
	{
		var s = ByteStream.Create( 512 );

		s.Write( new Vector3( 4, 5, 1 ) );
		s.Write( new Vector3( 44, 1235, 5121 ) );
		s.Write( new Vector3( 0, 0, 0 ) );

		var data = s.ToArray();

		s.Dispose();

		s = ByteStream.CreateReader( data );
		Assert.AreEqual( new Vector3( 4, 5, 1 ), s.Read<Vector3>() );
		Assert.AreEqual( new Vector3( 44, 1235, 5121 ), s.Read<Vector3>() );
		Assert.AreEqual( new Vector3( 0, 0, 0 ), s.Read<Vector3>() );

		s.Dispose();
	}

	[TestMethod]
	public void ReadWriteByteParsable()
	{
		var s = ByteStream.Create( 512 );

		var a = new MyDataClass();
		a.DataContents = "Hello There Happy Birthday Superman";

		s.Write( a );

		var data = s.ToArray();

		Assert.AreNotEqual( 0, data.Length );

		s.Dispose();

		s = ByteStream.CreateReader( data );

		var b = s.Read<MyDataClass>();

		Assert.AreEqual( b.DataContents, a.DataContents );

		s.Dispose();
	}

	[TestMethod]
	public void ReadWriteByteParsableStruct()
	{
		var s = ByteStream.Create( 512 );

		var a = new MyPlainOldDataClass();
		a.Position = Vector3.Up * 1000.0f;

		s.Write( a );

		var data = s.ToArray();

		Assert.AreNotEqual( 0, data.Length );

		s.Dispose();

		s = ByteStream.CreateReader( data );

		var b = s.Read<MyPlainOldDataClass>();

		Assert.AreEqual( b.Position, a.Position );

		s.Dispose();
	}

	[TestMethod]
	public void Growing()
	{
		var s = ByteStream.Create( 2 );

		s.Write( 32 );
		s.Write( 64 );
		s.Write( 512 );

		var data = s.ToArray();

		s.Dispose();

		s = ByteStream.CreateReader( data );
		Assert.AreEqual( 32, s.Read<int>() );
		Assert.AreEqual( 64, s.Read<int>() );
		Assert.AreEqual( 512, s.Read<int>() );

		s.Dispose();
	}

	[TestMethod]
	public void WriteStreamToStream()
	{
		using var s = ByteStream.Create( 2 );

		s.Write( 64 );
		s.Write( "Hello" );
		s.Write( 512 );

		using var t = ByteStream.Create( 2 );
		t.Write( 32 );
		t.Write( s );
		t.Write( 109 );

		Assert.AreEqual( s.Length + 4 + 4, t.Length );

		var data = t.ToArray();

		using var r = ByteStream.CreateReader( data );
		Assert.AreEqual( 32, r.Read<int>() );
		Assert.AreEqual( 64, r.Read<int>() );
		Assert.AreEqual( "Hello", r.Read<string>() );
		Assert.AreEqual( 512, r.Read<int>() );
		Assert.AreEqual( 109, r.Read<int>() );
	}

	[TestMethod]
	public void No_Negative_Buffers()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ByteStream.Create( -1 ) );
	}

	[TestMethod]
	public void ReadBuffer()
	{
		var data = Enumerable.Range( 0, 256 ).Select( x => (byte)x ).ToArray();
		using var r = ByteStream.CreateReader( data );

		var buffer = new byte[256];

		r.Read<int>();

		Assert.AreEqual( 128, r.Read( buffer, 128, 128 ) );

		Assert.AreEqual( 4, buffer[128] );
		Assert.AreEqual( 5, buffer[129] );
		Assert.AreEqual( 127 + 4, buffer[128 + 127] );
	}

	[TestMethod]
	public void ReadBufferUntilStreamEnd()
	{
		var data = Enumerable.Range( 0, 16 ).Select( x => (byte)x ).ToArray();
		using var r = ByteStream.CreateReader( data );

		var buffer = new byte[256];

		r.Read<int>();

		Assert.AreEqual( 12, r.Read( buffer, 128, 128 ) );

		Assert.AreEqual( 4, buffer[128] );
		Assert.AreEqual( 5, buffer[129] );
		Assert.AreEqual( 15, buffer[128 + 11] );

		Assert.AreEqual( 0, r.Read( buffer, 0, 256 ) );
	}

	[TestMethod]
	public void ReadBufferOutOfRange()
	{
		var buffer = new byte[256];

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
		{
			using var r = ByteStream.CreateReader( new byte[128] );
			return r.Read( buffer, -1, 16 );
		} );

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
		{
			using var r = ByteStream.CreateReader( new byte[128] );
			return r.Read( buffer, 384, 16 );
		} );

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
		{
			using var r = ByteStream.CreateReader( new byte[128] );
			return r.Read( buffer, 128, 256 );
		} );
	}

	[TestMethod]
	public void LeakExploit()
	{
		const int chunkSize = 0x50000000 - 128;
		byte[] chunk1 = new byte[chunkSize];

		const int kLeakSize = 8;

		Assert.ThrowsException<System.OverflowException>( () =>
		{
			ByteStream leakStream = ByteStream.Create( 128 );
			leakStream.Write( chunk1, (int)(0x80000000 - kLeakSize), kLeakSize );
			leakStream.Position = 0;
			ulong arrPtr = leakStream.Read<ulong>();

		} );
	}

	/// <summary>
	/// We were getting rare access violations when decompressing a <see cref="ByteStream"/>.
	/// </summary>
	[TestMethod]
	public void DecompressStressTest()
	{
		var payloadBytes = CreateRandomBytes( 0x1eb78437, 1024 );

		using var payload = new ByteStream( payloadBytes );
		using var compressed = payload.Compress();

		var compressedBytes = compressed.ToArray();

		for ( var i = 0; i < 10; ++i )
		{
			using var temp = new ByteStream( compressedBytes );

			GC.Collect();

			using var decompressed = temp.Decompress();

			Assert.AreEqual( payloadBytes.Length, decompressed.Length );

			foreach ( var b in payloadBytes )
			{
				Assert.AreEqual( b, decompressed.Read<byte>() );
			}
		}
	}

	[TestMethod]
	public void WriteOffset()
	{
		using var stream = new ByteStream( 10 );

		var data = new byte[100];

		data[50] = 123;
		data[59] = 231;

		stream.Write( data, 50, 10 );

		var written = stream.ToArray();

		Assert.AreEqual( 123, written[0] );
		Assert.AreEqual( 231, written[9] );
	}

	private static byte[] CreateRandomBytes( int seed, int length )
	{
		var random = new Random( seed );
		var bytes = new byte[length];

		random.NextBytes( bytes );

		return bytes;
	}

	// This isn't : unmanaged
	public readonly record struct TypeWrapper( Type Value );

	interface IConstrainetBreaker<TFrom>
	{
		void Write( TFrom value );
		void Read();
	}

	public class ConstraintBreaker<TFrom> : IConstrainetBreaker<TFrom> where TFrom : unmanaged
	{
		public void Write( TFrom value )
		{
			using var byteStream = ByteStream.Create( IntPtr.Size );
			byteStream.Write( value );
		}

		public void Read()
		{
			using var byteStream = ByteStream.Create( IntPtr.Size );
			var x = byteStream.Read<TFrom>();
		}
	}


	[TestMethod]
	public void CannotWriteNonPod()
	{
		var breakerType = typeof( ConstraintBreaker<> ).MakeGenericType( [typeof( TypeWrapper )] );
		var instance = (IConstrainetBreaker<TypeWrapper>)Activator.CreateInstance( breakerType );

		Assert.ThrowsException<InvalidOperationException>(
			() =>
			{
				instance.Write( new TypeWrapper( typeof( int ) ) );
			} );
	}

	[TestMethod]
	public void CannotReadNonPod()
	{
		var breakerType = typeof( ConstraintBreaker<> ).MakeGenericType( [typeof( TypeWrapper )] );
		var instance = (IConstrainetBreaker<TypeWrapper>)Activator.CreateInstance( breakerType );

		Assert.ThrowsException<InvalidOperationException>(
			() =>
			{
				instance.Read();
			} );
	}
}
