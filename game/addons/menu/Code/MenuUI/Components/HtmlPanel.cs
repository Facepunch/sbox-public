
using Microsoft.AspNetCore.Components.Rendering;

/// <summary>
/// Takes a Html string and tries to turn it into a bunch of panels
/// </summary>
public class HtmlPanel : Panel
{
	/// <summary>
	/// The actual html value
	/// </summary>
	public string Html { get; set; }

	protected override int BuildHash() => HashCode.Combine( Html );

	protected override string GetRenderTreeChecksum() => $"{BuildHash()}";

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		if ( string.IsNullOrWhiteSpace( Html ) )
			return;

		try
		{
			var node = Sandbox.Html.INode.Parse( Html );

			int i = 0;
			BuildRenderTree( tree, node, ref i );
			FlushHtml( tree, node, ref i );
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}
	}

	static string[] breakinElements = ["div", "p", "h1", "h2", "h3", "h4", "figure", "pre", "img", "video", "blockquote", "ul", "li", "ol"];

	static bool IsTextElement( Sandbox.Html.INode node )
	{
		if ( node.IsText ) return true;
		if ( node.Name == "br" ) return true;
		if ( node.Name == "strong" ) return true;
		if ( node.Name == "spoiler" ) return true;
		if ( node.Name == "a" ) return true;
		if ( breakinElements.Contains( node.Name ) ) return false;

		return node.Children.All( IsTextElement );
	}

	string html = "";

	void FlushHtml( RenderTreeBuilder tree, Sandbox.Html.INode node, ref int index )
	{
		if ( string.IsNullOrWhiteSpace( html ) ) return;

		tree.OpenElement<Label>( index++ );
		tree.AddAttribute<Label>( index++, html, t => t.Text = html );
		tree.AddAttribute<Label>( index++, true, t => t.IsRich = true );
		tree.CloseElement();

		html = "";
	}

	void BuildRenderTree( RenderTreeBuilder tree, Sandbox.Html.INode node, ref int index )
	{
		if ( node.IsComment )
			return;

		if ( IsTextElement( node ) )
		{
			html += node.OuterHtml;
			return;
		}

		if ( node.Name == "figure" )
		{
			FlushHtml( tree, node, ref index );
			ProcessFigure( tree, node, ref index );
			return;
		}

		if ( node.Name == "img" )
		{
			FlushHtml( tree, node, ref index );

			var src = node.GetAttribute( "src", "" );
			tree.OpenElement<Image>( index++ );
			tree.AddAttribute<Image>( index++, src, t => t.SetTexture( src ) );
			tree.CloseElement();
			return;
		}

		bool skipWrap = node.IsDocument;

		if ( !skipWrap )
		{
			FlushHtml( tree, node, ref index );
			tree.OpenElement( index++, node.Name );
			// todo apply style attribute, class attribute?
			//	tree.AddContent( index++, $"<{node.Name}>" );
		}

		foreach ( var child in node.Children )
		{
			BuildRenderTree( tree, child, ref index );
		}

		if ( !skipWrap )
		{
			FlushHtml( tree, node, ref index );

			//	tree.AddContent( index++, $"</{node.Name}>" );
			tree.CloseElement();
		}
	}

	void ProcessFigure( RenderTreeBuilder tree, Sandbox.Html.INode node, ref int index )
	{
		if ( node.Name == "img" )
		{
			BuildRenderTree( tree, node, ref index );
			return;
		}

		if ( node.Name == "figcaption" )
			return;

		//tree.AddContent( index++, $"<{node.Name}>" );

		foreach ( var child in node.Children )
		{
			ProcessFigure( tree, child, ref index );
		}

		//tree.AddContent( index++, $"</{node.Name}>" );
	}
}
