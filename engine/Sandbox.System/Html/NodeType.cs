// Description: Html Agility Pack - HTML Parsers, selectors, traversors, manupulators.
// Website & Documentation: http://html-agility-pack.net
// Forum & Issues: https://github.com/zzzprojects/html-agility-pack
// License: https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE
// More projects: http://www.zzzprojects.com/
// Copyright © ZZZ Projects Inc. 2014 - 2017. All rights reserved.

namespace Sandbox.Html
{
	/// <summary>
	/// Represents the type of a node.
	/// </summary>
	internal enum NodeType
	{
		/// <summary>
		/// The root of a document.
		/// </summary>
		Document,

		/// <summary>
		/// An HTML element.
		/// </summary>
		Element,

		/// <summary>
		/// An HTML comment.
		/// </summary>
		Comment,

		/// <summary>
		/// A text node is always the child of an element or a document node.
		/// </summary>
		Text,
	}
}
