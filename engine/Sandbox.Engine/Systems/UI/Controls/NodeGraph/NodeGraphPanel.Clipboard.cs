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

	/// <summary>
	/// The clipboard's text, for the menu's Paste. Game code can't read the clipboard - Ctrl+V hands
	/// it over instead - so by default the menu doesn't offer to paste; the editor can.
	/// </summary>
	protected virtual string ReadClipboard() => null;

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

	/// <summary>
	/// Put the selection on the clipboard.
	/// </summary>
	public void CopySelection() => TryCopy();

	/// <summary>
	/// Copy the selection, then remove what was copied.
	/// </summary>
	public void CutSelection() => TryCut();

	bool TryCopy()
	{
		if ( SerializeSelection() is not { } text ) return false;

		Sandbox.UI.Clipboard.SetText( text );
		return true;
	}

	bool TryCut()
	{
		if ( !TryCopy() ) return false;

		using var undoScope = UndoScope( "Cut Selection" );

		RemoveCopyable();
		return true;
	}

	/// <summary>
	/// Add the nodes in <paramref name="text"/>, from <see cref="SerializeSelection"/>, centred on
	/// <paramref name="position"/> in canvas units, or the mouse, and selected. False when the text
	/// isn't this graph's nodes.
	/// </summary>
	public bool Paste( string text, Vector2? position = null )
	{
		if ( Graph is null || !TryDecode( text, out var json ) ) return false;

		try
		{
			// Before deserializing: that adds the nodes to the graph
			using var undoScope = UndoScope( "Paste Selection" );

			ShowPasted( Graph.DeserializeNodes( json ).ToArray(), position ?? MouseTarget );
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

		using var undoScope = UndoScope( "Duplicate Selection" );

		ShowPasted( Graph.DeserializeNodes( json ).ToArray(), MouseTarget );
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
	/// The mouse in canvas units, or the middle of the view when it's outside.
	/// </summary>
	Vector2 MouseTarget => ScreenToCanvas( Viewport.IsInside( MousePosition ) ? MousePosition : Viewport.Center );

	/// <summary>
	/// Show freshly added <paramref name="nodes"/> centred on <paramref name="position"/>, as the new selection.
	/// </summary>
	void ShowPasted( IReadOnlyList<INode> nodes, Vector2 position )
	{
		if ( nodes.Count == 0 ) return;

		var center = new Vector2( nodes.Average( x => x.Position.x ), nodes.Average( x => x.Position.y ) );
		var offset = position - center;

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

	/// <summary>
	/// Copy, cut and paste travel up from the focused panel, and at every level the default handling
	/// also forwards to the parent - so take them here, once, as they pass the graph. A text field
	/// keeps its own.
	/// </summary>
	protected override void OnEvent( PanelEvent e )
	{
		// Consumed: the default handling would only repeat it, or overwrite what was copied
		if ( (e is CopyEvent or CutEvent or PasteEvent) && !FromTextEntry( e ) && HandleClipboard( e ) )
		{
			e.StopPropagation();
			return;
		}

		base.OnEvent( e );
	}

	bool HandleClipboard( PanelEvent e ) => e switch
	{
		PasteEvent paste => Paste( paste.ClipboardValue ),
		CutEvent => TryCut(),
		_ => TryCopy(),
	};

	static bool FromTextEntry( PanelEvent e ) => e.Target?.AncestorsAndSelf.Any( x => x is TextEntry ) == true;
}
