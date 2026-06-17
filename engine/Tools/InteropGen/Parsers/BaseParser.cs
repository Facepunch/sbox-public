using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen.Parsers;

/// <summary>
/// Line-oriented parser for the .def DSL. Reads a file line by line, skipping blanks and comments,
/// and routes each line either to a pushed sub-parser (class/function body) or to a directive handler.
/// Subclasses add the directives they understand via <see cref="TryHandleDirective"/>.
/// </summary>
internal class BaseParser
{
	// A directive line: the first word is the keyword, the rest is its argument.
	private static readonly Regex DirectiveRegex = new( @"([a-z]+?)\s+(.+)", RegexOptions.IgnoreCase );

	/// <summary>
	/// An attribute line like [nogc]. Shared by the parsers that accept attributes.
	/// </summary>
	protected static readonly Regex AttributeRegex = new( @"^\[(.+)\]", RegexOptions.IgnoreCase );

	protected Definition definition;
	protected Stack<BaseParser> subParser = new();
	protected Stack<string> fileStack = new();
	protected bool Finished;
	protected List<string> Attributes = [];

	public void Parse( Definition definition, string text, string filename )
	{
		this.definition = definition;
		fileStack.Push( filename );

		ParseText( text );
	}

	private void ParseText( string text )
	{
		foreach ( string line in text.Split( ['\n', '\r'], StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( string.IsNullOrWhiteSpace( line ) )
			{
				continue;
			}

			// Skip comment lines
			if ( line.TrimStart().StartsWith( "//", StringComparison.Ordinal ) )
			{
				continue;
			}

			definition.FullText += $"{line}\n";

			SubParseLine( line );
		}
	}

	private void CleanupFinishedSubParsers()
	{
		while ( subParser.Count > 0 && subParser.Peek().Finished )
		{
			_ = subParser.Pop();
		}
	}

	public void SubParseLine( string line )
	{
		CleanupFinishedSubParsers();

		if ( subParser.Count > 0 )
		{
			subParser.Peek().SubParseLine( line );
			return;
		}

		ParseLine( line );
	}

	public virtual void ParseLine( string line )
	{
		// A line like "ident foo" is a directive: the first word selects a handler, the rest is the argument.
		Match match = DirectiveRegex.Match( line );
		if ( match.Success )
		{
			string keyword = match.Groups[1].Value;
			string arg = match.Groups[2].Value.Trim();

			// Strip surrounding quotes
			if ( arg.Length >= 2 && arg[0] == '"' && arg[^1] == '"' )
			{
				arg = arg[1..^1];
			}

			if ( TryHandleDirective( keyword, arg ) )
			{
				return;
			}
		}

		Log.Warning( $"Unhandled Line \"{line}\" in \"{fileStack.Peek()}\"" );
	}

	/// <summary>
	/// Handle a directive line. Overridden by parsers that define directives; returns false if unknown.
	/// </summary>
	protected virtual bool TryHandleDirective( string keyword, string arg )
	{
		return false;
	}

	public void IncludeFile( string filename )
	{
		string path = Path.GetDirectoryName( fileStack.Peek() );
		string fullname = Path.Combine( path, filename );

		fileStack.Push( fullname );

		string text = File.ReadAllText( fullname );
		ParseText( text );

		_ = fileStack.Pop();
	}

	public void IncludeFolder( string filename )
	{
		SearchOption option = filename.EndsWith( "/*" ) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

		string path = Path.GetDirectoryName( fileStack.Peek() );
		string fullname = Path.Combine( path, filename );

		// Directory.GetFiles returns rooted paths, so include them directly.
		foreach ( string file in Directory.GetFiles( fullname.TrimEnd( '*' ), "*.def", option ).OrderBy( x => x ) )
		{
			IncludeFile( file );
		}
	}
}
