using System.Text;
using Sandbox.Html;

namespace Sandbox.UI;

/// <summary>
/// Converts documentation HTML to text for IntelliSense labels.
/// </summary>
internal static class ScriptDocumentation
{
	internal static string PlainText( string description )
	{
		if ( string.IsNullOrEmpty( description ) )
		{
			return "";
		}
		var text = new StringBuilder();
		Append( INode.Parse( description ), text );
		return text.ToString().Trim();
	}

	static void Append( INode node, StringBuilder text )
	{
		if ( node.IsText )
		{
			text.Append( node.OuterHtml );
			return;
		}
		if ( node.IsComment || node.Name is "script" or "style" )
		{
			return;
		}
		var lineBreak = node.Name is "br" or "p" or "div" or "li" or "para";
		if ( lineBreak && text.Length > 0 && text[^1] != '\n' )
		{
			text.Append( '\n' );
		}
		foreach ( var child in node.Children )
		{
			Append( child, text );
		}
		if ( lineBreak && text.Length > 0 && text[^1] != '\n' )
		{
			text.Append( '\n' );
		}
	}
}
