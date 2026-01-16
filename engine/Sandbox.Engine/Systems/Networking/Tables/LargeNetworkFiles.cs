using Sandbox.Internal;
using Sandbox.Network;
using System.Threading;

namespace Sandbox;

internal class LargeNetworkFiles
{
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

		RedirectFileSystem?.Dispose();
		RedirectFileSystem = AssetDownloadCache.CreateRedirectFileSystem();
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
	/// Add a file to be networked.
	/// </summary>
	public bool AddFile( string fileName )
	{
		if ( !EngineFileSystem.Mounted.FileExists( fileName ) )
			return false;

		var crc = EngineFileSystem.Mounted.GetCrc( fileName );
		var size = EngineFileSystem.Mounted.FileSize( fileName );
		var normalizedFileName = NormalizeFileName( fileName );
		StringTable.Set( normalizedFileName, new LargeFileInfo( size, crc ) );

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

			system.UpdateLoading( $"Download file ({currentCount + 1}/{downloadQueue.Count}) {file}" );

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

			// download the file
			var response = await Connection.Host.SendRequest( new RequestFile { filename = file } );

			token.ThrowIfCancellationRequested();

			if ( response is not byte[] data || data.Length == 0 )
			{
				Log.Warning( $"Failed to download file {file}! (response: {response})" );
				currentCount++;
				continue;
			}

			var fn = AssetDownloadCache.StoreFile( file, info.CRC, data );
			if ( fn is not null )
			{
				RedirectFileSystem.AddAbsFile( file, fn );
			}

			currentCount++;
		}

		downloadQueue.Clear();

		Log.Info( $"Download Complete ({downloadQueue.Count()} files total) ({sw.Elapsed.TotalSeconds:0.00}s)" );
	}

	internal void NetworkInitialize( GameNetworkSystem instance )
	{
		instance.AddHandler<RequestFile>( OnRequestNetworkFile );
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

	[Expose]
	public record struct RequestFile( string filename );

}
