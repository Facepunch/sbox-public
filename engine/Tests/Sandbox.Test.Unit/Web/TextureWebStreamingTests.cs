using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using Sandbox.Services;

namespace Web;

[TestClass]
public class TextureWebStreamingTests
{
	[TestMethod]
	[DataRow( 10.0, 0.0f, 5.0 )]
	[DataRow( 20.0, 0.0f, 10.0 )]
	[DataRow( 21.0, 0.0f, 1.0 )]
	[DataRow( 50.0, 0.0f, 30.0 )]
	[DataRow( 100.0, 0.0f, 80.0 )]
	[DataRow( 1000.0, 0.0f, 800.0 )]
	[DataRow( 100.0, 2.0f, 2.0 )]
	[DataRow( 10.0, 20.0f, 5.0 )]
	public void BackgroundBudgetReservesBandwidthAndDemoCapOnlySlowsIt( double measuredMbps, float maximumMbps, double expectedMbps )
	{
		Assert.AreEqual( (int)(expectedMbps * 1_000_000 / 8), TextureWebStreaming.GetBackgroundLimit( measuredMbps * 1_000_000 / 8, maximumMbps ) );
	}

	static readonly byte[] Prefix = Enumerable.Range( 0, 128 ).Select( x => (byte)x ).ToArray();

	[TestMethod, DoNotParallelize]
	public async Task DisabledStreamingUsesFullDownloadWithoutPreparingPrefix()
	{
		var enabled = TextureWebStreaming.Enabled;
		try
		{
			TextureWebStreaming.Enabled = false;
			var file = File();
			Assert.IsFalse( TextureWebStreaming.IsSupported( file ) );
			Assert.AreEqual( file.Size, TextureWebStreaming.DownloadSize( file ) );
			Assert.IsNull( await TextureWebStreaming.TryPrepareAsync( file, default ) );
		}
		finally { TextureWebStreaming.Enabled = enabled; }
	}

	[TestMethod]
	public void RollingSpeedResistsSmallPackagesButAdaptsToLargeTransfers()
	{
		const double fast = 100 * 1024 * 1024;
		Assert.AreEqual( fast, TextureWebStreaming.AverageDownloadSpeed( 0, 200 * 1024 * 1024, 2 ) );
		var afterSmallPackage = TextureWebStreaming.AverageDownloadSpeed( fast, 1024 * 1024, 2 );
		Assert.IsTrue( afterSmallPackage > fast * 0.96 && afterSmallPackage < fast );
		// Sustained evidence of a slower connection still reduces the estimate.
		var afterLargePackage = TextureWebStreaming.AverageDownloadSpeed( fast, 128 * 1024 * 1024, 16 );
		Assert.IsTrue( afterLargePackage > 8 * 1024 * 1024 && afterLargePackage < 10 * 1024 * 1024 );
	}

	static ManifestSchema.File File() => new()
	{
		Url = "https://cdn.sbox.game/asset/test.vtex_c",
		Path = "test.vtex_c",
		Size = 4096,
		Texture = new()
		{
			Version = TextureWebStreamingInfo.CurrentVersion,
			Width = 512,
			Height = 512,
			Mips = [new() { Level = 1, PrefixSize = Prefix.Length, PrefixHash = Convert.ToHexString( SHA256.HashData( Prefix ) ) }]
		}
	};

	sealed class Handler( Func<HttpRequestMessage, HttpResponseMessage> response ) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken token )
		{
			token.ThrowIfCancellationRequested();
			Assert.AreEqual( "bytes=0-127", request.Headers.Range.ToString() );
			return Task.FromResult( response( request ) );
		}
	}

	static HttpResponseMessage Response( byte[] bytes, HttpStatusCode status = HttpStatusCode.PartialContent, long start = 0 )
	{
		var response = new HttpResponseMessage( status ) { Content = new ByteArrayContent( bytes ) };
		response.Content.Headers.ContentRange = new ContentRangeHeaderValue( start, start + 127, 4096 );
		return response;
	}

	[TestMethod]
	public async Task ValidPrefixPreservesExactBytes()
	{
		using var handler = new Handler( _ => Response( Prefix ) );
		using var client = new HttpClient( handler );
		CollectionAssert.AreEqual( Prefix, await TextureWebStreaming.DownloadPrefixAsync( client, File(), default ) );
	}

	[TestMethod]
	public async Task RejectsIgnoredOrWrongRangesAndCorruption()
	{
		foreach ( var response in new[] { Response( Prefix, HttpStatusCode.OK ), Response( Prefix, start: 1 ),
			Response( new byte[128] ), Response( new byte[129] ) } )
		{
			using var handler = new Handler( _ => response );
			using var client = new HttpClient( handler );
			await Assert.ThrowsExceptionAsync<InvalidDataException>( () => TextureWebStreaming.DownloadPrefixAsync( client, File(), default ) );
		}
	}

	[TestMethod]
	public async Task TruncatedAndCanceledDownloadsCannotMount()
	{
		using var handler = new Handler( _ => Response( new byte[64] ) );
		using var client = new HttpClient( handler );
		await Assert.ThrowsExceptionAsync<EndOfStreamException>( () => TextureWebStreaming.DownloadPrefixAsync( client, File(), default ) );
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();
		try { await TextureWebStreaming.DownloadPrefixAsync( client, File(), cancel.Token ); Assert.Fail( "Canceled request succeeded" ); }
		catch ( OperationCanceledException ) { }
	}

	[TestMethod]
	public void OldManifestsAndInvalidDescriptorsUseFullDownloads()
	{
		var file = File();
		Assert.IsTrue( TextureWebStreaming.IsSupported( file ) );
		file.Texture = null;
		Assert.AreEqual( file.Size, TextureWebStreaming.DownloadSize( file ) );
		file = File(); file.Texture.Mips[0].PrefixSize = 5000;
		Assert.IsFalse( TextureWebStreaming.IsSupported( file ) );
		file = File(); file.Texture.Mips[0].PrefixHash = new string( 'z', 64 );
		Assert.IsFalse( TextureWebStreaming.IsSupported( file ) );
		file = File(); file.Url = "file:///test.vtex_c";
		Assert.IsFalse( TextureWebStreaming.IsSupported( file ) );
		foreach ( var url in new[] { "http://cdn.sbox.game/asset/test.vtex_c", "http://127.0.0.1:7343/test.vtex_c" } )
		{
			file = File(); file.Url = url;
			Assert.IsFalse( TextureWebStreaming.IsSupported( file ) );
			Assert.AreEqual( file.Size, TextureWebStreaming.DownloadSize( file ) );
		}
		file = File(); file.Path = "test.vsnd_c";
		Assert.IsFalse( TextureWebStreaming.IsSupported( file ) );
	}

	[TestMethod]
	[DataRow( 8192, 8192, 3, 1024 )]
	[DataRow( 4096, 4096, 2, 1024 )]
	[DataRow( 2048, 2048, 2, 512 )]
	[DataRow( 1024, 1024, 2, 256 )]
	[DataRow( 512, 512, 1, 256 )]
	[DataRow( 256, 256, 0, 256 )]
	[DataRow( 256, 128, 0, 256 )]
	[DataRow( 128, 128, 0, 128 )]
	[DataRow( 2, 1, 0, 2 )]
	[DataRow( 256, 512, 1, 256 )]
	[DataRow( 384, 256, 0, 384 )]
	[DataRow( 768, 256, 1, 384 )]
	[DataRow( 2048, 8192, 3, 1024 )]
	[DataRow( 8192, 1024, 3, 1024 )]
	[DataRow( 3000, 1000, 2, 750 )]
	[DataRow( 16384, 4096, 4, 1024 )]
	public void InitialQualityFollowsSourceResolution( int width, int height, int expectedLevel, int expectedResolution )
	{
		var file = File();
		file.Texture.Width = width;
		file.Texture.Height = height;
		var levels = Math.Min( 4, (int)Math.Log2( Math.Max( width, height ) ) );
		file.Texture.Mips = Enumerable.Range( 1, levels ).Select( level => new TextureWebStreamingInfo.Mip
		{
			Level = level,
			PrefixSize = 128 << (levels - level),
			PrefixHash = new string( 'a', 64 )
		} ).ToArray();
		var mip = TextureWebStreaming.GetInitialMip( file );
		if ( expectedLevel == 0 )
		{
			Assert.IsNull( mip );
			Assert.AreEqual( file.Size, TextureWebStreaming.DownloadSize( file ) );
			return;
		}
		Assert.IsNotNull( mip );
		Assert.AreEqual( expectedLevel, mip.Level );
		Assert.AreEqual( expectedResolution, Math.Max( width, height ) >> mip.Level );
		Assert.AreEqual( (long)mip.PrefixSize, TextureWebStreaming.DownloadSize( file ) );
	}

	[TestMethod]
	public void RejectsInvalidIndexesAndMissingRequiredQuality()
	{
		foreach ( var invalidate in new Action<TextureWebStreamingInfo>[]
		{
			x => x.Version = 0,
			x => x.Version = 3,
			x => x.Width = 0,
			x => x.Height = 65536,
			x => x.Width = x.Height = 1,
			x => x.Mips = null,
			x => x.Mips = [],
			x => x.Mips = [null],
			x => x.Mips[0].Level = 0,
			x => x.Mips[0].Level = 32,
			x => x.Mips[0].PrefixSize = 16,
			x => x.Mips[0].PrefixHash = null,
			x => x.Mips = [x.Mips[0], x.Mips[0]],
			x => x.Width = 8192 // Requires level 3, which is missing; do not silently choose lower quality.
		} )
		{
			var file = File();
			invalidate( file.Texture );
			Assert.IsNull( TextureWebStreaming.GetInitialMip( file ) );
			Assert.AreEqual( file.Size, TextureWebStreaming.DownloadSize( file ) );
		}
	}

	[TestMethod]
	public void SelectedPrefixMustFitMemoryLimit()
	{
		var file = File();
		file.Size = 32 * 1024 * 1024;
		file.Texture.Width = file.Texture.Height = 4096;
		file.Texture.Mips = [new() { Level = 1, PrefixSize = 16 * 1024 * 1024, PrefixHash = new string( 'a', 64 ) },
			new() { Level = 2, PrefixSize = 6 * 1024 * 1024, PrefixHash = new string( 'b', 64 ) }];
		Assert.AreEqual( 6L * 1024 * 1024, TextureWebStreaming.DownloadSize( file ) );
		file.Texture.Mips[1].PrefixSize = 8 * 1024 * 1024 + 1;
		Assert.AreEqual( file.Size, TextureWebStreaming.DownloadSize( file ) );
	}

	[TestMethod]
	public async Task DownloadsSelectedMipInsteadOfFirstIndexEntry()
	{
		var file = File();
		file.Texture.Width = file.Texture.Height = 4096;
		file.Texture.Mips = [new() { Level = 1, PrefixSize = 512, PrefixHash = new string( 'a', 64 ) },
			new() { Level = 2, PrefixSize = Prefix.Length, PrefixHash = Convert.ToHexString( SHA256.HashData( Prefix ) ) }];
		using var handler = new Handler( _ => Response( Prefix ) );
		using var client = new HttpClient( handler );
		CollectionAssert.AreEqual( Prefix, await TextureWebStreaming.DownloadPrefixAsync( client, file, default ) );
	}
}
