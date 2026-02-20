// Description: Html Agility Pack - HTML Parsers, selectors, traversors, manupulators.
// Website & Documentation: http://html-agility-pack.net
// Forum & Issues: https://github.com/zzzprojects/html-agility-pack
// License: https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE
// More projects: http://www.zzzprojects.com/
// Copyright � ZZZ Projects Inc. 2014 - 2017. All rights reserved.

using System.Web;

namespace Sandbox.Html;

/// <summary>
/// Represents an HTML text node.
/// </summary>
[SkipHotload]
internal sealed class TextNode : Node
{
	private string _text;

	internal TextNode( Document ownerdocument, int index ) : base( NodeType.Text, ownerdocument, index )
	{
	}

	/// <summary>
	/// Gets or Sets the HTML between the start and end tags of the object. In the case of a text node, it is equals to OuterHtml.
	/// </summary>
	public override string InnerHtml => OuterHtml;

	/// <summary>
	/// Gets or Sets the object and its content in HTML.
	/// </summary>
	public override string OuterHtml => _text ?? (_text = HttpUtility.HtmlDecode( base.OuterHtml ));
}
