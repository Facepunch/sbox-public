using BenchmarkDotNet.Attributes;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;


[MemoryDiagnoser]
public class ByteStreamTest
{
	Guid Guid = Guid.NewGuid();
	byte[] byteBuffer = new byte[34];
	byte[] readByteBuffer = new byte[34];
	int initialBuffer = 4096;
	byte[] intData = Array.Empty<byte>();
	byte[] byteData = Array.Empty<byte>();
	byte[] guidData = Array.Empty<byte>();
	byte[] stringData = Array.Empty<byte>();

	[GlobalSetup]
	public void Setup()
	{
		{
			var writer = ByteStream.Create( initialBuffer );
			try
			{
				for ( int i = 0; i < 512; i++ )
				{
					writer.Write( i );
				}

				intData = writer.ToArray();
			}
			finally
			{
				writer.Dispose();
			}
		}

		{
			var writer = ByteStream.Create( initialBuffer );
			try
			{
				for ( int i = 0; i < 512; i++ )
				{
					writer.Write( byteBuffer );
				}

				byteData = writer.ToArray();
			}
			finally
			{
				writer.Dispose();
			}
		}

		{
			var writer = ByteStream.Create( initialBuffer );
			try
			{
				for ( int i = 0; i < 512; i++ )
				{
					writer.Write( Guid );
				}

				guidData = writer.ToArray();
			}
			finally
			{
				writer.Dispose();
			}
		}

		{
			var writer = ByteStream.Create( initialBuffer );
			try
			{
				for ( int i = 0; i < 512; i++ )
				{
					writer.Write( "Hello there" );
				}

				stringData = writer.ToArray();
			}
			finally
			{
				writer.Dispose();
			}
		}
	}

	[Benchmark]
	public void ByteStreamInt()
	{
		using var writer = ByteStream.Create( initialBuffer );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( i );
		}
	}

	[Benchmark]
	public void ByteStreamByte()
	{
		using var writer = ByteStream.Create( initialBuffer );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( byteBuffer );
		}
	}

	[Benchmark]
	public void ByteStreamGuid()
	{
		using var writer = ByteStream.Create( initialBuffer );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( Guid );
		}
	}

	[Benchmark]
	public void ByteStreamString()
	{
		using var writer = ByteStream.Create( initialBuffer );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( "Hello there" );
		}
	}

	[Benchmark]
	public int ByteStreamReadInt()
	{
		var reader = ByteStream.CreateReader( intData );
		int sum = 0;

		for ( int i = 0; i < 512; i++ )
		{
			sum += reader.Read<int>();
		}

		return sum;
	}

	[Benchmark]
	public int ByteStreamReadByte()
	{
		var reader = ByteStream.CreateReader( byteData );
		int sum = 0;
		for ( int i = 0; i < 512; i++ )
		{
			reader.Read( readByteBuffer, 0, readByteBuffer.Length );
			sum += readByteBuffer[0];
		}

		return sum;
	}

	[Benchmark]
	public int ByteStreamReadGuid()
	{
		var reader = ByteStream.CreateReader( guidData );
		int hash = 0;

		for ( int i = 0; i < 512; i++ )
		{
			hash ^= reader.Read<Guid>().GetHashCode();
		}

		return hash;
	}

	[Benchmark]
	public int ByteStreamReadString()
	{
		var reader = ByteStream.CreateReader( stringData );
		int totalLength = 0;

		for ( int i = 0; i < 512; i++ )
		{
			var value = reader.Read<string>();
			totalLength += value?.Length ?? 0;
		}

		return totalLength;
	}

	[Benchmark]
	public void PooledMemoryStreamInt()
	{
		var memoryStream = PooledMemoryStream.Rent( initialBuffer );
		using var writer = new BinaryWriter( memoryStream, Encoding.UTF8, true );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( (uint)i );
		}
	}

	[Benchmark]
	public void PooledMemoryStreamByte()
	{
		var memoryStream = PooledMemoryStream.Rent( initialBuffer );
		using var writer = new BinaryWriter( memoryStream, Encoding.UTF8, true );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( byteBuffer );
		}
	}

	[Benchmark]
	public unsafe void PooledMemoryStreamGuid()
	{
		var memoryStream = PooledMemoryStream.Rent( initialBuffer );
		using var writer = new BinaryWriter( memoryStream, Encoding.UTF8, true );

		for ( int i = 0; i < 512; i++ )
		{
			WriteGuid( writer, in Guid );
		}
	}

	[Benchmark]
	public unsafe void PooledMemoryStreamString()
	{
		var memoryStream = PooledMemoryStream.Rent( initialBuffer );
		using var writer = new BinaryWriter( memoryStream, Encoding.UTF8, true );

		for ( int i = 0; i < 512; i++ )
		{
			writer.Write( "Hello there" );
		}
	}

	private void WriteGuid( BinaryWriter writer, in Guid guid )
	{
		Span<byte> buffer = stackalloc byte[16];
		MemoryMarshal.Write( buffer, in guid );
		writer.Write( buffer );
	}

	/// <summary>
	/// A wrapper around <see cref="MemoryStream"/> used internally here to rent a pooled
	/// stream and avoid allocations where possible.
	/// </summary>
	private class PooledMemoryStream : MemoryStream
	{
		private PooledMemoryStream( int capacity ) : base( capacity )
		{
		}

		// Non-thread-safe pool queue
		private static readonly Queue<PooledMemoryStream> Pool = new();

		/// <summary>
		/// Rent a new stream from the pool or create one if none are available.
		/// </summary>
		public static PooledMemoryStream Rent( int initialSize = 8192 )
		{
			if ( !Pool.TryDequeue( out var s ) )
				return new PooledMemoryStream( initialSize );

			s.Position = 0;
			s.SetLength( 0 );

			return s;

		}

		/// <summary>
		/// Get a span of only the written portion of the buffer.
		/// </summary>
		public ReadOnlySpan<byte> GetWrittenSpan()
		{
			return new ReadOnlySpan<byte>( GetBuffer(), 0, (int)Length );
		}

		/// <summary>
		/// Return this stream to the pool and reset it.
		/// </summary>
		public void Return()
		{
			Position = 0;
			SetLength( 0 );
			Pool.Enqueue( this );
		}

		protected override void Dispose( bool disposing )
		{
			throw new InvalidOperationException( "Use Return() instead of Dispose() to recycle the stream" );
		}
	}

}

[MemoryDiagnoser]
public class ByteStreamPoolingBenchmarks
{
	const int BatchSize = 100;
	readonly int[] randomInitialSizes = new int[BatchSize];
	readonly byte[][] payloads = new byte[BatchSize][];
	readonly byte[] mediumPayload = new byte[4096];
	readonly byte[] largePayload = new byte[64 * 1024];

	[GlobalSetup]
	public void Setup()
	{
		var rng = new Random( 42 );

		for ( int i = 0; i < BatchSize; i++ )
		{
			// Keep sizes inside typical pool buckets to surface contention
			var size = rng.Next( 64, 64 * 1024 );
			randomInitialSizes[i] = size;

			var payloadLength = rng.Next( 16, 512 );
			var payload = new byte[payloadLength];
			rng.NextBytes( payload );
			payloads[i] = payload;
		}

		rng.NextBytes( mediumPayload );
		rng.NextBytes( largePayload );
	}

	[Benchmark( Description = "Create 100 random streams sequentially" )]
	public void CreateRandomBatchSequential()
	{
		for ( int i = 0; i < BatchSize; i++ )
		{
			using var stream = ByteStream.Create( randomInitialSizes[i] );
			stream.Write( payloads[i] );
		}
	}

	[Benchmark( Description = "Hold 100 random streams simultaneously" )]
	public void CreateRandomBatchHeld()
	{
		CreateBatchRecursive( 0 );
	}

	[Benchmark( Description = "Stress growth with large payload writes" )]
	public int LargePayloadWriteAndCopy()
	{
		using var stream = ByteStream.Create( 128 );

		for ( int i = 0; i < 8; i++ )
		{
			stream.Write( largePayload );
		}

		return stream.ToArray().Length;
	}

	[Benchmark( Description = "Rapid reuse of pooled buffers" )]
	public void RapidReuseMixedSizes()
	{
		for ( int i = 0; i < BatchSize; i++ )
		{
			using var stream = ByteStream.Create( 256 );
			stream.Write( mediumPayload );
			stream.Write( payloads[i] );
		}
	}

	void CreateBatchRecursive( int index )
	{
		if ( index >= BatchSize )
		{
			return;
		}

		using var stream = ByteStream.Create( randomInitialSizes[index] );
		stream.Write( payloads[index] );

		CreateBatchRecursive( index + 1 );
	}
}
