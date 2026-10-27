using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.Html;

/// <summary>
/// Renders Steam-flavoured BBCode (workshop descriptions) to html - the elements the menu's <c>HtmlPanel</c> builds
/// panels from. sbox.web has the same parser for its pages.
/// <para>
/// Safe for untrusted input: all text is html-encoded, only whitelisted tags become markup, links and images must
/// be http(s), and anything unknown or unbalanced is left as literal text. Unclosed tags are closed at the end so
/// the output is always well formed.
/// </para>
/// </summary>
public static partial class BBCodeParser
{
	[GeneratedRegex( @"\[(/?)([a-zA-Z0-9]+|\*)(?:=([^\]\r\n]*))?\]" )]
	private static partial Regex TagRegex();

	/// <summary>
	/// Tags that wrap their contents in an html element.
	/// </summary>
	static readonly Dictionary<string, (string Open, string Close, bool Block)> Wrappers = new( StringComparer.OrdinalIgnoreCase )
	{
		["b"] = ("<strong>", "</strong>", false),
		["i"] = ("<em>", "</em>", false),
		["u"] = ("<u>", "</u>", false),
		["strike"] = ("<s>", "</s>", false),
		["spoiler"] = ("<spoiler>", "</spoiler>", false),
		["h1"] = ("<h1>", "</h1>", true),
		["h2"] = ("<h2>", "</h2>", true),
		["h3"] = ("<h3>", "</h3>", true),
		["list"] = ("<ul>", "</ul>", true),
		["olist"] = ("<ol>", "</ol>", true),
		["*"] = ("<li>", "</li>", true),
		["table"] = ("<div class=\"bb-table\">", "</div>", true),
		["tr"] = ("<div class=\"bb-tr\">", "</div>", true),
		["th"] = ("<div class=\"bb-th\">", "</div>", true),
		["td"] = ("<div class=\"bb-td\">", "</div>", true),
		["p"] = ("<p>", "</p>", true),
		// Opened by hand so the author can go in, but closed like any other
		["quote"] = ("<blockquote>", "</blockquote>", true),
	};

	static bool IsBlock( string name ) => name is "hr" or "code" || (Wrappers.TryGetValue( name, out var w ) && w.Block);

	/// <summary>
	/// Tags whose contents are taken raw, up to the matching close tag.
	/// </summary>
	static readonly HashSet<string> Raw = new( StringComparer.OrdinalIgnoreCase ) { "code", "noparse", "url", "img" };

	static readonly HashSet<string> Lists = new( StringComparer.OrdinalIgnoreCase ) { "list", "olist" };

	/// <summary>
	/// Convert BBCode to an html fragment.
	/// </summary>
	public static string ToHtml( string bbcode )
	{
		if ( string.IsNullOrWhiteSpace( bbcode ) ) return "";

		var source = bbcode.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
		var sb = new StringBuilder( source.Length + source.Length / 4 );
		var open = new List<string>();
		var pos = 0;

		// Newlines straight after a block tag are layout in the source, not content
		var swallowNewline = false;

		void Text( string text )
		{
			if ( swallowNewline && text.StartsWith( '\n' ) ) text = text[1..];
			swallowNewline = false;
			if ( text.Length == 0 ) return;

			sb.Append( WebUtility.HtmlEncode( text ).Replace( "\n", "<br>" ) );
		}

		void Close( int index )
		{
			for ( var i = open.Count - 1; i >= index; i-- )
			{
				sb.Append( Wrappers[open[i]].Close );
				open.RemoveAt( i );
			}
		}

		while ( pos < source.Length )
		{
			var match = TagRegex().Match( source, pos );
			if ( !match.Success )
			{
				Text( source[pos..] );
				break;
			}

			var closing = match.Groups[1].Length > 0;
			var name = match.Groups[2].Value.ToLowerInvariant();

			// Likewise the newline (and any indent) before one
			var before = source[pos..match.Index];
			if ( IsBlock( name ) )
			{
				var newline = before.LastIndexOf( '\n' );
				if ( string.IsNullOrWhiteSpace( before[(newline + 1)..] ) )
					before = newline < 0 ? "" : before[..newline];
			}

			Text( before );
			pos = match.Index + match.Length;
			var arg = match.Groups[3].Success ? match.Groups[3].Value.Trim().Trim( '"', '\'' ) : null;

			if ( !closing && Raw.Contains( name ) )
			{
				var end = source.IndexOf( $"[/{name}]", pos, StringComparison.OrdinalIgnoreCase );
				var inner = end < 0 ? source[pos..] : source[pos..end];
				pos = end < 0 ? source.Length : end + name.Length + 3;

				RenderRaw( sb, name, arg, inner );
				swallowNewline = name is "code";
				continue;
			}

			if ( !closing && name == "hr" )
			{
				sb.Append( "<hr>" );
				swallowNewline = true;
				continue;
			}

			if ( !closing && name == "quote" )
			{
				open.Add( "quote" );
				sb.Append( "<blockquote>" );
				if ( !string.IsNullOrEmpty( arg ) )
					sb.Append( "<div class=\"bb-quote-author\">" ).Append( WebUtility.HtmlEncode( arg ) ).Append( "</div>" );
				swallowNewline = true;
				continue;
			}

			if ( Wrappers.ContainsKey( name ) )
			{
				if ( !closing )
				{
					// A new list item ends the previous one in the same list
					if ( name == "*" )
					{
						var list = open.FindLastIndex( Lists.Contains );
						if ( list < 0 ) { Text( match.Value ); continue; }

						var item = open.FindLastIndex( x => x == "*" );
						if ( item > list ) Close( item );
					}

					open.Add( name );
					sb.Append( Wrappers[name].Open );
					swallowNewline = Wrappers[name].Block;
					continue;
				}

				var index = open.FindLastIndex( x => x == name );
				if ( index < 0 ) { Text( match.Value ); continue; }

				Close( index );
				swallowNewline = Wrappers[name].Block;
				continue;
			}

			Text( match.Value );
		}

		Close( 0 );
		return sb.ToString();
	}

	static void RenderRaw( StringBuilder sb, string name, string arg, string inner )
	{
		switch ( name )
		{
			case "noparse":
				sb.Append( WebUtility.HtmlEncode( inner ).Replace( "\n", "<br>" ) );
				return;

			case "code":
				sb.Append( "<pre>" ).Append( WebUtility.HtmlEncode( inner.Trim( '\n' ) ) ).Append( "</pre>" );
				return;

			case "img":
				if ( SafeUrl( inner.Trim() ) is { } src )
					sb.Append( "<img src=\"" ).Append( WebUtility.HtmlEncode( src ) ).Append( "\">" );
				else
					sb.Append( WebUtility.HtmlEncode( inner ) );
				return;

			case "url":
				// [url=href]label[/url] or [url]href[/url]. The label can itself hold BBCode (eg an image).
				var href = SafeUrl( arg ?? inner.Trim() );
				var label = arg is null ? WebUtility.HtmlEncode( inner.Trim() ) : ToHtml( inner );

				if ( href is null )
				{
					sb.Append( label );
					return;
				}

				sb.Append( "<a href=\"" ).Append( WebUtility.HtmlEncode( href ) ).Append( "\">" )
					.Append( string.IsNullOrEmpty( label ) ? WebUtility.HtmlEncode( href ) : label )
					.Append( "</a>" );
				return;
		}
	}

	/// <summary>
	/// The url if it's an absolute http(s) link, otherwise null.
	/// </summary>
	static string SafeUrl( string url )
	{
		if ( string.IsNullOrWhiteSpace( url ) ) return null;
		if ( !Uri.TryCreate( url.Trim(), UriKind.Absolute, out var uri ) ) return null;
		return uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
	}
}
