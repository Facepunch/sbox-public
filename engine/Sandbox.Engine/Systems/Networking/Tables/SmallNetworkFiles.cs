using Sandbox.Network;
using System.IO;

namespace Sandbox;

internal class SmallNetworkFiles
{
	public StringTable StringTable { get; init; }
	public MemoryFileSystem Files { get; private set; }

	public SmallNetworkFiles( string name )
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
		Files = new MemoryFileSystem();
	}

	/// <summary>
	/// Add all files from the network.
	/// </summary>
	public void Refresh()
	{
		foreach ( var (_, entry) in StringTable.Entries )
		{
			AddFileToFileSystem( entry.Name, entry.Data );
		}
	}

	/// <summary>
	/// Add a file to be networked.
	/// </summary>
	public bool AddFile( BaseFileSystem fs, string fileName, byte[] contents )
	{
		if ( !fs.FileExists( fileName ) )
			return false;

		var normalizedFileName = NormalizeFileName( fileName );
		StringTable.Set( normalizedFileName, contents );

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
		AddFileToFileSystem( entry.Name, entry.Data );
	}

	void OnTableEntryRemoved( StringTable.Entry entry )
	{
		Files.DeleteFile( entry.Name );
	}

	void OnTableSnapshot()
	{
		Refresh();
	}

	void AddFileToFileSystem( string fileName, byte[] contents )
	{
		if ( !AssetDownloadCache.IsLegalDownload( fileName ) )
			return;

		var directory = Path.GetDirectoryName( fileName );
		if ( !Files.DirectoryExists( directory ) )
		{
			Files.CreateDirectory( directory );
		}

		Files.WriteAllBytes( fileName, contents );
	}
}
