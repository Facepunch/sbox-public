using Sandbox.Network;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	private StringTable _networkTable;
	// Promotion adopts shared source, but must not start server-only files from the former client's packages.
	private bool _wasNetworkClient;

	/// <summary>
	/// Whether named scripts are supplied by the host instead of local resource scans.
	/// </summary>
	internal bool ReceivesNetworkFiles { get; private set; }

	/// <summary>
	/// Rejects local declaration edits while the host owns this context's script sources.
	/// Temporary commands remain available through the ordinary execution access check.
	/// </summary>
	private void CheckSourceAccess()
	{
		CheckAccess();
		if ( _context.ReceivesNetworkScripts )
		{
			throw new InvalidOperationException( "Script sources are controlled by the host." );
		}
	}

	/// <summary>
	/// Installs the script manifest and source payloads. Existing accepted files seed the host's table;
	/// clients defer execution until their assemblies and packages have finished loading.
	/// </summary>
	internal void InitializeNetworking( StringTable table, bool isClient )
	{
		CheckAccess();
		ReceivesNetworkFiles = isClient;
		_wasNetworkClient |= isClient;
		_networkTable = table;
		PublishNetworkFiles();
	}

	/// <summary>
	/// Only ordinary .csx files are shared. The server suffix is case-insensitive on every platform.
	/// </summary>
	internal static bool IsSharedFile( string path )
	{
		return path.EndsWith( ".csx", StringComparison.OrdinalIgnoreCase )
			&& !path.EndsWith( ".server.csx", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Replicates accepted source, keeping rejected edits out of snapshots and avoiding unchanged updates.
	/// Called before startup so newly published declarations are available to subsequent network messages.
	/// </summary>
	private void PublishNetworkFiles( bool sendUpdates = false )
	{
		if ( _networkTable is null || ReceivesNetworkFiles )
		{
			return;
		}

		foreach ( var path in _networkTable.Entries.Keys.ToArray() )
		{
			if ( !_namedFiles.ContainsKey( path ) || !IsSharedFile( path ) )
			{
				_networkTable.Remove( path );
			}
		}

		foreach ( var (path, source) in _namedFiles )
		{
			if ( !IsSharedFile( path ) )
			{
				continue;
			}

			if ( _networkTable.Entries.TryGetValue( path, out var entry ) && entry.ReadAsString() == source )
			{
				continue;
			}

			_networkTable.SetString( path, source );
		}

		// Startup can immediately spawn objects using these types. Send their source before those messages.
		if ( sendUpdates && _networkTable.HasChanged && _context == Engine.GlobalContext.Game )
		{
			Networking.System?.SendTableUpdates();
		}
	}

	/// <summary>
	/// Applies the complete received file set as one declaration batch, including additions and removals.
	/// Unchanged scripts retain their state and do not execute their startup statements again.
	/// </summary>
	internal FileLoadResult LoadNetworkFiles()
	{
		CheckAccess();
		if ( !ReceivesNetworkFiles || _networkTable is null )
		{
			throw new InvalidOperationException( "Script networking has not been initialized as a client." );
		}

		var sources = _networkTable.Entries.Values
			.Where( entry => IsSharedFile( entry.Name ) )
			.ToDictionary( entry => entry.Name, entry => entry.ReadAsString(), StringComparer.Ordinal );

		return LoadFiles( sources, null );
	}
}
