using Sandbox.Internal;
using Sandbox.Network;
using System.IO;
using System.Threading;

namespace Sandbox;

internal class LargeNetworkFiles
{
	public BaseFileSystem Files { get; private set; }
	public StringTable StringTable { get; init; }

	record struct LargeFileInfo( long Size, ulong CRC );

	RedirectFileSystem RedirectFileSystem { get; set; }

	HashSet<string> downloadQueue = new();

	public LargeNetworkFiles( string name )
	{
		StringTable = new( name, true );
		StringTable.OnChangeOrAdd += OnTableEntryUpdated;
		StringTable.OnRemoved += OnTableEntryRemoved;
		StringTable.OnSnapshot += OnTableSnapshot;
	}

	/// <summary>
	/// Reset the string table.
	/// </summary>
	public void Reset()
	{
		StringTable.Reset();

		Files?.Dispose();
		RedirectFileSystem = AssetDownloadCache.CreateRedirectFileSystem();
		Files = new BaseFileSystem( RedirectFileSystem );
	}

	/// <summary>
	/// Add all files from the network.
	/// </summary>
	public void Refresh()
	{
		foreach ( var (_, entry) in StringTable.Entries )
		{
			AddFileToFileSystem( entry.Name, entry.Read<LargeFileInfo>() );
		}
	}

	/// <summary>
	/// Add a file from the mounted filesystem used to serve download requests.
	/// </summary>
	public bool AddFile( BaseFileSystem mounted, string fileName )
	{
		Stream stream;
		try
		{
			stream = mounted.OpenRead( fileName );
		}
		catch ( FileNotFoundException )
		{
			return false;
		}
		catch ( DirectoryNotFoundException )
		{
			return false;
		}

		// Redirect filesystems can return null for an unmapped path.
		if ( stream is null ) return false;
		using ( stream )
		{
			var size = stream.Length;
			var crc = Sandbox.Utility.Crc64.FromStream( stream );
			var normalizedFileName = NormalizeFileName( fileName );
			StringTable.Set( normalizedFileName, new LargeFileInfo( size, crc ) );

			if ( AssetDownloadCache.DebugNetworkFiles )
				Log.Info( $"Adding LARGE File {fileName} ({size.FormatBytes()})" );
		}

		return true;
	}

	/// <summary>
	/// Remove a networked file.
	/// </summary>
	public void RemoveFile( string fileName )
	{
		var normalizedFileName = NormalizeFileName( fileName );
		StringTable.Remove( normalizedFileName );
	}

	string NormalizeFileName( string fileName )
	{
		return BaseFileSystem.NormalizeFilename( fileName ).TrimStart( '/' );
	}

	void OnTableEntryUpdated( StringTable.Entry entry )
	{
		AddFileToFileSystem( entry.Name, entry.Read<LargeFileInfo>() );
	}

	void OnTableEntryRemoved( StringTable.Entry entry )
	{

	}

	void OnTableSnapshot()
	{
		Log.Info( "Checking for network files.." );
		var sw = System.Diagnostics.Stopwatch.StartNew();

		Refresh();

		Log.Info( $"..done in {sw.Elapsed.TotalSeconds:0.00}s" );
	}

	void AddFileToFileSystem( string fileName, LargeFileInfo contents )
	{
		// Can we find this file somewhere, or do we need to download it?

		if ( EngineFileSystem.Mounted.FileExists( fileName ) )
		{
			var size = EngineFileSystem.Mounted.FileSize( fileName );
			if ( size == contents.Size )
			{
				var crc = EngineFileSystem.Mounted.GetCrc( fileName );
				if ( crc == contents.CRC )
				{
					if ( AssetDownloadCache.DebugNetworkFiles )
					{
						Log.Info( $"Skipping downloading {fileName} - we already have it" );
					}
					return;
				}
			}
		}

		if ( !AssetDownloadCache.IsLegalDownload( fileName ) )
			return;

		if ( AssetDownloadCache.TryMount( RedirectFileSystem, fileName, contents.CRC ) )
			return;

		if ( AssetDownloadCache.DebugNetworkFiles )
		{
			Log.Info( $"Queued Network File: {fileName} / {contents.Size} / {contents.CRC}" );
		}
		downloadQueue.Add( fileName );
	}

	public async Task RunDownloadQueue( NetworkSystem system, CancellationToken token )
	{
		if ( RedirectFileSystem is null )
			return;

		Assert.NotNull( Connection.Host );

		Log.Info( $"Downloading {downloadQueue.Count} files.." );
		var currentCount = 0;
		var sw = System.Diagnostics.Stopwatch.StartNew();

		foreach ( var file in downloadQueue )
		{
			if ( !StringTable.Entries.TryGetValue( file, out var entry ) )
				continue;

			var info = entry.Read<LargeFileInfo>();

			if ( AssetDownloadCache.DebugNetworkFiles )
			{
				Log.Info( $"Download file {file}" );
			}

			LoadingScreen.Title = $"Downloading Files ({currentCount + 1}/{downloadQueue.Count})";
			LoadingScreen.Subtitle = file;

			if ( RedirectFileSystem.FileExists( file.NormalizeFilename( true ) ) )
			{
				currentCount++;
				continue;
			}

			token.ThrowIfCancellationRequested();

			if ( Connection.Host is null )
			{
				throw new TaskCanceledException( "Connection became null" );
			}

			var fn = AssetDownloadCache.GetAbsolutePath( file, info.CRC );
			Directory.CreateDirectory( Path.GetDirectoryName( fn ) );
			var temporary = fn + "." + Guid.NewGuid().ToString( "N" ) + ".partial";
			FileStream output = null;
			try
			{
				// Closed before the rename, or unconditionally in finally on failure/cancellation.
#pragma warning disable CA2000
				output = File.Open( temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None );
#pragma warning restore CA2000
				await NetworkFileTransfer.DownloadAsync( output, info.Size, info.CRC, async ( offset, length, cancellation ) =>
				{
					var host = Connection.Host ?? throw new TaskCanceledException( "Connection became null" );
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
					timeout.CancelAfter( TimeSpan.FromSeconds( 60 ) );
					var response = await host.SendRequest( new RequestFilePart( file, offset, length, info.CRC ), timeout.Token );
					return response as byte[];
				}, token );
				output.Dispose();
				output = null;

				token.ThrowIfCancellationRequested();
				File.Move( temporary, fn, overwrite: true );
				RedirectFileSystem.AddAbsFile( file, fn );
			}
			finally
			{
				output?.Dispose();
				if ( File.Exists( temporary ) ) File.Delete( temporary );
			}

			currentCount++;
		}

		LoadingScreen.Subtitle = null;
		Log.Info( $"Download Complete ({currentCount} files total) ({sw.Elapsed.TotalSeconds:0.00}s)" );

		downloadQueue.Clear();
	}

	internal void NetworkInitialize( GameNetworkSystem instance )
	{
		instance.AddHandler<RequestFile>( OnRequestNetworkFile );
		instance.AddHandler<RequestFilePart>( OnRequestNetworkFilePart );
	}

	async Task OnRequestNetworkFile( RequestFile file, Connection connection, Guid msgGuid )
	{
		if ( !EngineFileSystem.Mounted.FileExists( file.filename ) )
		{
			Log.Warning( $"Client ({connection.Name}) requested missing file: {file.filename}" );
			connection.SendResponse( msgGuid, Array.Empty<byte>() );
			return;
		}

		var contents = await EngineFileSystem.Mounted.ReadAllBytesAsync( file.filename );

		connection.SendResponse( msgGuid, contents );
	}

	async Task OnRequestNetworkFilePart( RequestFilePart file, Connection connection, Guid msgGuid )
	{
		if ( !AssetDownloadCache.IsLegalDownload( file.Filename )
			|| !StringTable.Entries.TryGetValue( file.Filename, out var entry ) )
		{
			connection.SendResponse( msgGuid, Array.Empty<byte>() );
			return;
		}

		var info = entry.Read<LargeFileInfo>();
		if ( file.Crc != info.CRC || !NetworkFileTransfer.IsValidRange( info.Size, file.Offset, file.Length ) )
		{
			connection.SendResponse( msgGuid, Array.Empty<byte>() );
			return;
		}

		try
		{
			using var stream = EngineFileSystem.Mounted.OpenRead( file.Filename );
			if ( stream is null || stream.Length != info.Size ) throw new IOException( "Network file changed" );
			var data = await NetworkFileTransfer.ReadPartAsync( stream, file.Offset, file.Length );
			connection.SendResponse( msgGuid, data );
		}
		catch ( IOException e )
		{
			Log.Warning( e, $"Failed to read network file part: {file.Filename}" );
			connection.SendResponse( msgGuid, Array.Empty<byte>() );
		}
	}

	[Expose]
	public record struct RequestFilePart( string Filename, long Offset, int Length, ulong Crc );

	[Expose]
	public record struct RequestFile( string filename );

}
