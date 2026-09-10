using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Editor;

public partial class DockManager
{
	/// <summary>
	/// Called before a layout state is restored with the names of the open docks saved in that layout.
	/// Dynamic dock types can use this to create their dock widgets before native restoration begins.
	/// </summary>
	public Action<IReadOnlyCollection<string>> OnStateRestoring { get; set; }

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
		OnStateRestoring?.Invoke( GetSavedOpenDockNames( state ) );

		if ( !_nativeDockManager.restoreState( state, 1 ) )
			return false;

		OnLayoutLoaded?.Invoke();
		return true;
	}

	private static IReadOnlyCollection<string> GetSavedOpenDockNames( string state )
	{
		try
		{
			var compressed = Convert.FromBase64String( state );
			if ( compressed.Length <= sizeof( uint ) )
				return new HashSet<string>();

			using var input = new MemoryStream( compressed, sizeof( uint ), compressed.Length - sizeof( uint ) );
			using var zlib = new ZLibStream( input, CompressionMode.Decompress );
			var document = XDocument.Load( zlib );

			return document.Descendants( "Widget" )
				.Where( x => x.Attribute( "Closed" )?.Value != "1" )
				.Select( x => x.Attribute( "Name" )?.Value )
				.Where( x => !string.IsNullOrWhiteSpace( x ) )
				.ToHashSet();
		}
		catch ( FormatException )
		{
			return new HashSet<string>();
		}
		catch ( InvalidDataException )
		{
			return new HashSet<string>();
		}
		catch ( System.Xml.XmlException )
		{
			return new HashSet<string>();
		}
	}
}
