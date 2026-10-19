using System.IO;
using System.IO.Compression;
using System.Text;

namespace Sandbox.UI.NodeGraph;

//
// Copy and paste: Ctrl+C, Ctrl+X and Ctrl+V arrive as the UI's copy, cut and paste events, Ctrl+D
// duplicates. The graph turns nodes into text and back through IGraph.SerializeNodes and DeserializeNodes.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// Marks clipboard text as this graph's nodes. Graphs that share it paste into each other.
	/// </summary>
	protected virtual string ClipboardIdent => "nodegraph";

	/// <summary>
	/// Whether <paramref name="node"/> is copied with the selection. Nodes that can't be removed
	/// aren't, so there's never a second one.
	/// </summary>
	protected virtual bool CanCopy( NodePanel node ) => node.Node.CanRemove;

	NodePanel[] CopyableSelection() => SelectedNodes.Where( CanCopy ).ToArray();

	/// <summary>
	/// The selection as clipboard text, or null when nothing in it can be copied or the graph can't
	/// serialize nodes.
	/// </summary>
	public string SerializeSelection()
	{
		var nodes = CopyableSelection();
		if ( nodes.Length == 0 || Graph?.SerializeNodes( nodes.Select( x => x.Node ) ) is not { } json ) return null;

		using var ms = new MemoryStream();
		using ( var zs = new GZipStream( ms, CompressionMode.Compress ) )
		{
			var data = Encoding.UTF8.GetBytes( json );
			zs.Write( data, 0, data.Length );
		}

		return $"{ClipboardIdent}:{Convert.ToBase64String( ms.ToArray() )}";
	}

	public void CopySelection()
	{
		if ( SerializeSelection() is { } text ) Sandbox.UI.Clipboard.SetText( text );
	}

	/// <summary>
	/// Copy the selection, then remove what was copied.
	/// </summary>
	public void CutSelection()
	{
		if ( SerializeSelection() is not { } text ) return;

		Sandbox.UI.Clipboard.SetText( text );
		RemoveCopyable();
	}

	/// <summary>
	/// Add the nodes in <paramref name="text"/>, from <see cref="SerializeSelection"/>, centred on the
	/// mouse and selected. False when the text isn't this graph's nodes.
	/// </summary>
	public bool Paste( string text )
	{
		if ( Graph is null || !TryDecode( text, out var json ) ) return false;

		try
		{
			ShowPasted( Graph.DeserializeNodes( json ).ToArray() );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't paste nodes: {e.Message}" );
		}

		return true;
	}

	/// <summary>
	/// Copy the selection and paste it at the mouse, leaving the clipboard alone.
	/// </summary>
	public void DuplicateSelection()
	{
		var nodes = CopyableSelection();
		if ( nodes.Length == 0 || Graph?.SerializeNodes( nodes.Select( x => x.Node ) ) is not { } json ) return;

		ShowPasted( Graph.DeserializeNodes( json ).ToArray() );
	}

	void RemoveCopyable()
	{
		foreach ( var node in CopyableSelection() )
		{
			RemoveNode( node );
		}
	}

	bool TryDecode( string text, out string json )
	{
		json = null;

		var ident = $"{ClipboardIdent}:";
		if ( string.IsNullOrWhiteSpace( text ) || !text.StartsWith( ident, StringComparison.Ordinal ) ) return false;

		try
		{
			using var ms = new MemoryStream( Convert.FromBase64String( text[ident.Length..] ) );
			using var zs = new GZipStream( ms, CompressionMode.Decompress );
			using var reader = new StreamReader( zs, Encoding.UTF8 );
			json = reader.ReadToEnd();
			return true;
		}
		catch ( Exception e ) when ( e is FormatException or InvalidDataException )
		{
			Log.Warning( "Clipboard holds nodes that can't be read" );
			return false;
		}
	}

	/// <summary>
	/// Show freshly added <paramref name="nodes"/> centred on the mouse, as the new selection.
	/// </summary>
	void ShowPasted( IReadOnlyList<INode> nodes )
	{
		if ( nodes.Count == 0 ) return;

		var center = new Vector2( nodes.Average( x => x.Position.x ), nodes.Average( x => x.Position.y ) );
		var offset = ScreenToCanvas( Viewport.IsInside( MousePosition ) ? MousePosition : Viewport.Center ) - center;

		DeselectNodes();
		DeselectConnections();

		foreach ( var node in nodes )
		{
			node.Position = (node.Position + offset).SnapToGrid( GridSize );
			ShowNode( node ).Selected = true;
		}

		OrderComments();
		SelectionChanged?.Invoke();
	}

	public override string GetClipboardValue( bool cut )
	{
		if ( SerializeSelection() is not { } text ) return base.GetClipboardValue( cut );

		if ( cut ) RemoveCopyable();
		return text;
	}

	public override void OnPaste( string text )
	{
		if ( !Paste( text ) ) base.OnPaste( text );
	}
}
