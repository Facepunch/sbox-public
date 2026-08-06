using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebTests;

/// <summary>
/// Web.DownloadFile streams package files from the cdn to disk. It used to wrap the body in
/// DataProgress.HttpContentStream, which reads stream.Length and drops the token CopyToAsync is given,
/// so a live response body threw and an in-flight download couldn't be cancelled.
/// </summary>
[TestClass]
[TestCategory( "LiveBackend" )]
public class DownloadFileTest
{
	const string ManifestUrl = "https://public.facepunch.com/sbox/package/manifest/1/facepunch.construct";

	static ManifestSchema.File[] files;

	[ClassInitialize]
	public static async Task FetchManifest( TestContext context )
	{
		var manifest = await Sandbox.Utility.Web.DownloadJson<ManifestSchema>( ManifestUrl );
		files = manifest.Files.Where( x => x.Size > 0 ).OrderBy( x => x.Size ).ToArray();
	}

	static string EmptyFolder()
	{
		var dir = Path.Combine( Path.GetTempPath(), "sbox_downloadfile", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( dir );
		return dir;
	}

	[TestMethod]
	public async Task DownloadsAPackageFile()
	{
		var file = files.First();
		var dir = EmptyFolder();

		var success = await Sandbox.Utility.Web.DownloadFile( file.Url, Path.Combine( dir, "file" ), default );

		Assert.IsTrue( success, $"couldn't download {file.Url}" );
		Assert.AreEqual( file.Size, new FileInfo( Path.Combine( dir, "file" ) ).Length );
		Assert.AreEqual( 1, Directory.GetFiles( dir ).Length, "left a temp file behind" );

		Directory.Delete( dir, true );
	}

	/// <summary>
	/// Cancelling has to interrupt the body read, not just the request. The biggest file in the package
	/// takes far longer than this to transfer, so finishing at all means the token was ignored.
	/// </summary>
	[TestMethod]
	public async Task CancellingStopsTheTransfer()
	{
		var file = files.Last();
		var dir = EmptyFolder();

		using var cancel = new CancellationTokenSource( 250 );
		var sw = Stopwatch.StartNew();
		var cancelled = false;

		try
		{
			await Sandbox.Utility.Web.DownloadFile( file.Url, Path.Combine( dir, "file" ), cancel.Token );
		}
		catch ( OperationCanceledException )
		{
			cancelled = true;
		}

		Assert.IsTrue( cancelled, "cancelling should throw out of DownloadFile" );
		Assert.IsTrue( sw.Elapsed < TimeSpan.FromSeconds( 5 ), $"took {sw.Elapsed} to give up on {file.Size.FormatBytes()}" );
		Assert.AreEqual( 0, Directory.GetFiles( dir ).Length, "left a temp file behind" );

		Directory.Delete( dir, true );
	}
}
