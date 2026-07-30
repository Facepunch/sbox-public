using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Editor;

public partial class DockManager
{
	/// <summary>
	/// Called when the layout state is loaded, e.g. when the default
	/// layout is applied or a saved layout is restored.
	/// </summary>
	public Action OnLayoutLoaded { get; set; }

	/// <summary>
	/// A string representing the entire state of the dock manager (position of all docks, etc).
	/// Setting this restores the layout and invokes <see cref="OnLayoutLoaded"/>.
	/// </summary>
	public string State
	{
		get => _nativeDockManager.saveState( 1 );
		set => RestoreState( value );
	}

	/// <summary>
	/// Restore a layout previously captured from <see cref="State"/>. Returns false if the
	/// state couldn't be restored, e.g. it was saved by an incompatible version.
	/// </summary>
	public bool RestoreState( string state )
	{
		CreateMissingInstances( state );

		if ( !_nativeDockManager.restoreState( state, 1 ) )
			return false;

		OnLayoutLoaded?.Invoke();
		return true;
	}

	/// <summary>
	/// A saved layout can contain extra instances of a dock type ("Asset Browser 2") that
	/// don't exist right now - create them first so the restore has something to place.
	/// </summary>
	void CreateMissingInstances( string state )
	{
		var xml = DecodeState( state );

		if ( string.IsNullOrEmpty( xml ) )
			return;

		foreach ( var info in docks.Values.ToArray() )
		{
			foreach ( Match match in Regex.Matches( xml, $"\"{Regex.Escape( info.Title )} (\\d+)\"" ) )
			{
				var instanceName = match.Value.Trim( '"' );

				if ( FindDockWidget( instanceName ) is not null )
					continue;

				var dock = CreateInstance( info, instanceName );
				if ( dock is null ) continue;

				AddDock( dock, DockArea.Center );
			}
		}
	}

	/// <summary>
	/// The native state is base64( qCompress( xml ) ): a 4 byte length prefix, then zlib.
	/// </summary>
	static string DecodeState( string state )
	{
		if ( string.IsNullOrEmpty( state ) )
			return state;

		try
		{
			var bytes = Convert.FromBase64String( state );
			if ( bytes.Length <= 6 ) return state;

			using var input = new MemoryStream( bytes, 4, bytes.Length - 4 );
			using var zlib = new ZLibStream( input, CompressionMode.Decompress );
			using var reader = new StreamReader( zlib );

			return reader.ReadToEnd();
		}
		catch
		{
			return state;
		}
	}
}
