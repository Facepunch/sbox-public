using Sandbox;
using System;
using System.IO;
using System.Threading;

namespace NetworkTests;

[TestClass]
public class NetworkFileTransferTests
{
	[DataTestMethod]
	[DataRow( -1L, 0L, 1 )]
	[DataRow( 10L, -1L, 1 )]
	[DataRow( 10L, 0L, 0 )]
	[DataRow( 10L, 9L, 2 )]
	[DataRow( 10L, long.MaxValue, 1 )]
	[DataRow( long.MaxValue, 0L, NetworkFileTransfer.PartSize + 1 )]
	public void RejectsInvalidRanges( long size, long offset, int length ) =>
		Assert.IsFalse( NetworkFileTransfer.IsValidRange( size, offset, length ) );

	[TestMethod]
	public async Task TransfersFileLargerThanDecompressionLimit()
	{
		// Use the real offending asset's size, with deterministic, incompressible parts.
		const long size = 362624637;
		var part = new byte[NetworkFileTransfer.PartSize];
		new Random( 42 ).NextBytes( part );
		ulong crc = 0;
		for ( long offset = 0; offset < size; offset += part.Length )
			crc = Sandbox.Utility.Crc64.FromBytes( part, (int)Math.Min( part.Length, size - offset ), crc );

		var path = Path.GetTempFileName();
		try
		{
			using var output = new FileStream( path, FileMode.Create, FileAccess.ReadWrite, FileShare.None );
			long nextOffset = 0;
			int requests = 0;
			await NetworkFileTransfer.DownloadAsync( output, size, crc, ( offset, length, token ) =>
			{
				Assert.AreEqual( nextOffset, offset );
				Assert.IsTrue( length <= NetworkFileTransfer.PartSize );
				nextOffset += length;
				requests++;
				return Task.FromResult( length == part.Length ? part : part[..length] );
			}, CancellationToken.None );
			Assert.AreEqual( size, output.Length );
			Assert.AreEqual( 87, requests );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public async Task RejectsShortPart()
	{
		using var output = new MemoryStream();
		await Assert.ThrowsExceptionAsync<InvalidDataException>( () => NetworkFileTransfer.DownloadAsync(
			output, 10, 0, ( _, _, _ ) => Task.FromResult( new byte[9] ), CancellationToken.None ) );
	}

	[TestMethod]
	public async Task RejectsBadChecksum()
	{
		using var output = new MemoryStream();
		await Assert.ThrowsExceptionAsync<InvalidDataException>( () => NetworkFileTransfer.DownloadAsync(
			output, 10, 1, ( _, _, _ ) => Task.FromResult( new byte[10] ), CancellationToken.None ) );
	}

	[TestMethod]
	public async Task CancellationStopsBeforeRequestingNextPart()
	{
		using var output = new MemoryStream();
		using var cancellation = new CancellationTokenSource();
		int requests = 0;
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => NetworkFileTransfer.DownloadAsync(
			output, NetworkFileTransfer.PartSize + 1L, 0, ( _, length, _ ) =>
			{
				requests++;
				cancellation.Cancel();
				return Task.FromResult( new byte[length] );
			}, cancellation.Token ) );
		Assert.AreEqual( 1, requests );
	}

	[TestMethod]
	public async Task ReadsRequestedOffsetAndRejectsTruncatedSource()
	{
		using var source = new MemoryStream( new byte[] { 1, 2, 3, 4 } );
		CollectionAssert.AreEqual( new byte[] { 3, 4 }, await NetworkFileTransfer.ReadPartAsync( source, 2, 2 ) );
		await Assert.ThrowsExceptionAsync<InvalidDataException>( () => NetworkFileTransfer.ReadPartAsync( source, 3, 2 ) );
	}

	[TestMethod]
	public async Task SupportsEmptyFile()
	{
		using var output = new MemoryStream();
		await NetworkFileTransfer.DownloadAsync( output, 0, 0,
			( _, _, _ ) => throw new AssertFailedException( "Empty file must not request a part" ), CancellationToken.None );
	}
}
