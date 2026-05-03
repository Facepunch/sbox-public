using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Editor.Copilot;

/// <summary>
/// Tiny streaming-friendly markdown parser.
///
/// Splits a chunk of markdown into ordered <see cref="Segment"/>s — either
/// plain prose (rendered as Qt rich-text HTML via <see cref="Label"/>) or a
/// fenced code block (rendered as a styled <see cref="TextEdit"/> with copy
/// button by <see cref="CodeBlockWidget"/>).
///
/// We deliberately do NOT use a full CommonMark library — the parser must
/// tolerate half-finished input (the assistant is still streaming).
/// </summary>
public static class MarkdownParser
{
	public enum SegmentKind
	{
		Prose,
		Code,
	}

	public class Segment
	{
		public SegmentKind Kind     { get; init; }
		public string      Language { get; init; } = "";
		public string      Text     { get; init; } = "";

		/// <summary>True when the closing fence has not been seen yet.</summary>
		public bool        IsOpen   { get; init; }
	}

	private static readonly Regex CodeFenceRegex = new(
		@"^[ \t]{0,3}```([A-Za-z0-9_+\-#.]*)\s*$",
		RegexOptions.Multiline | RegexOptions.Compiled );

	/// <summary>
	/// Parse a (possibly partial) markdown document into ordered segments.
	/// </summary>
	public static List<Segment> Parse( string text )
	{
		var result = new List<Segment>();
		if ( string.IsNullOrEmpty( text ) ) return result;

		var lines = text.Split( '\n' );
		var prose = new StringBuilder();
		var code  = new StringBuilder();
		string lang = null;
		bool inCode = false;

		void FlushProse()
		{
			if ( prose.Length == 0 ) return;
			result.Add( new Segment { Kind = SegmentKind.Prose, Text = prose.ToString().TrimEnd( '\n' ) } );
			prose.Clear();
		}

		for ( int i = 0; i < lines.Length; i++ )
		{
			var line  = lines[i];
			var match = CodeFenceRegex.Match( line );

			if ( match.Success )
			{
				if ( !inCode )
				{
					FlushProse();
					inCode = true;
					lang   = match.Groups[1].Value;
					continue;
				}

				// Closing fence
				result.Add( new Segment
				{
					Kind     = SegmentKind.Code,
					Language = lang ?? "",
					Text     = code.ToString().TrimEnd( '\n' ),
					IsOpen   = false
				} );
				code.Clear();
				inCode = false;
				lang   = null;
				continue;
			}

			if ( inCode )
				code.Append( line ).Append( '\n' );
			else
				prose.Append( line ).Append( '\n' );
		}

		// Flush remainder — code block may still be open if streaming
		if ( inCode )
		{
			result.Add( new Segment
			{
				Kind     = SegmentKind.Code,
				Language = lang ?? "",
				Text     = code.ToString().TrimEnd( '\n' ),
				IsOpen   = true
			} );
		}
		else
		{
			FlushProse();
		}

		return result;
	}

	// ── prose → HTML ──────────────────────────────────────────────────────────

	private static readonly Regex InlineCodeRegex = new( @"`([^`\n]+)`",                  RegexOptions.Compiled );
	private static readonly Regex BoldRegex       = new( @"\*\*([^\*\n]+)\*\*",            RegexOptions.Compiled );
	private static readonly Regex ItalicRegex     = new( @"(?<!\*)\*(?!\*)([^\*\n]+)\*",   RegexOptions.Compiled );
	private static readonly Regex LinkRegex       = new( @"\[([^\]]+)\]\(([^)]+)\)",        RegexOptions.Compiled );
	private static readonly Regex Heading3Regex   = new( @"^###\s+(.+)$",                   RegexOptions.Multiline | RegexOptions.Compiled );
	private static readonly Regex Heading2Regex   = new( @"^##\s+(.+)$",                    RegexOptions.Multiline | RegexOptions.Compiled );
	private static readonly Regex Heading1Regex   = new( @"^#\s+(.+)$",                     RegexOptions.Multiline | RegexOptions.Compiled );
	private static readonly Regex BulletRegex     = new( @"^\s*[-*]\s+(.+)$",               RegexOptions.Multiline | RegexOptions.Compiled );

	/// <summary>
	/// Convert a prose segment to safe Qt rich-text HTML.
	/// Escapes raw HTML first, then applies inline + block-level conversions.
	/// </summary>
	public static string ProseToHtml( string prose )
	{
		if ( string.IsNullOrEmpty( prose ) ) return "";

		// HTML-escape everything first
		var s = prose
			.Replace( "&",  "&amp;" )
			.Replace( "<",  "&lt;" )
			.Replace( ">",  "&gt;" );

		// Headings
		s = Heading3Regex.Replace( s, m => $"<h3 style='margin:6px 0 2px;'>{m.Groups[1].Value}</h3>" );
		s = Heading2Regex.Replace( s, m => $"<h2 style='margin:8px 0 4px;'>{m.Groups[1].Value}</h2>" );
		s = Heading1Regex.Replace( s, m => $"<h1 style='margin:10px 0 4px;'>{m.Groups[1].Value}</h1>" );

		// Bullets — wrap consecutive lines in <ul>
		s = WrapBullets( s );

		// Inline
		s = LinkRegex.Replace( s,
			m => $"<a href=\"{m.Groups[2].Value}\" style=\"color:#58a6ff;\">{m.Groups[1].Value}</a>" );
		s = InlineCodeRegex.Replace( s,
			m => $"<code style=\"background:rgba(255,255,255,0.08); padding:1px 4px; border-radius:3px; font-family:Consolas,monospace;\">{m.Groups[1].Value}</code>" );
		s = BoldRegex   .Replace( s, m => $"<b>{m.Groups[1].Value}</b>" );
		s = ItalicRegex .Replace( s, m => $"<i>{m.Groups[1].Value}</i>" );

		// Paragraph breaks (double newline) and line breaks (single newline)
		s = s.Replace( "\r", "" );
		s = Regex.Replace( s, @"\n\n+", "<br><br>" );
		s = s.Replace( "\n", "<br>" );

		return s;
	}

	private static string WrapBullets( string s )
	{
		var sb     = new StringBuilder();
		var lines  = s.Split( '\n' );
		bool inUl  = false;

		foreach ( var line in lines )
		{
			var m = BulletRegex.Match( line );
			if ( m.Success )
			{
				if ( !inUl ) { sb.Append( "<ul style='margin:2px 0; padding-left:18px;'>" ); inUl = true; }
				sb.Append( "<li>" ).Append( m.Groups[1].Value ).Append( "</li>" );
			}
			else
			{
				if ( inUl ) { sb.Append( "</ul>" ); inUl = false; }
				sb.Append( line ).Append( '\n' );
			}
		}

		if ( inUl ) sb.Append( "</ul>" );
		return sb.ToString();
	}
}
