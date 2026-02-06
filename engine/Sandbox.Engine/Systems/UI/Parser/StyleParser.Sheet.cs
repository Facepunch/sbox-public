using Sandbox.Engine;

namespace Sandbox.UI;

internal static partial class StyleParser
{
	[ThreadStatic]
	static int IncludeLoops = 0;

	public static StyleSheet ParseSheet( string content, string filename = "none", IEnumerable<(string, string)> variables = null )
	{
		IncludeLoops = 0;

		StyleSheet sheet = new();
		sheet.AddVariables( variables );

		ParseToSheet( content, filename, sheet );

		return sheet;
	}

	private static void ParseToSheet( string content, string filename, StyleSheet sheet )
	{
		IncludeLoops++;

		filename ??= "none";
		filename = filename.NormalizeFilename();

		sheet.AddFilename( filename );

		content = StripComments( content );

		var p = new Parse( content, filename );
		while ( !p.IsEnd )
		{
			p = p.SkipWhitespaceAndNewlines();

			if ( p.IsEnd )
				break;

			if ( ParseVariable( ref p, sheet ) )
				continue;

			if ( ParseKeyframes( ref p, sheet ) )
				continue;

			if ( ParseMixinDefinition( ref p, sheet ) )
				continue;

			if ( ParseImport( ref p, sheet, filename ) )
				continue;

			// Handle top-level @include (emits rules without parent selector)
			if ( ParseTopLevelInclude( ref p, sheet ) )
				continue;

			var selector = p.ReadUntilOrEnd( "{;$@" );

			if ( selector is null )
				throw new System.Exception( $"Parse Error, expected class name {p.FileAndLine}" );

			if ( p.IsEnd ) throw new System.Exception( $"Parse Error, unexpected end {p.FileAndLine}" );

			if ( p.Current != '{' ) throw new System.Exception( $"Parse Error, unexpected character {p.Current} {p.FileAndLine}" );

			if ( p.Current == '{' )
			{
				ReadStyleBlock( ref p, selector, sheet, null );
			}
		}

		IncludeLoops--;
	}

	private static bool ParseVariable( ref Parse p, StyleSheet sheet )
	{
		if ( p.Current != '$' )
			return false;

		// We want the key with the $
		(string key, string value) = p.ReadKeyValue();

		bool isDefault = value.EndsWith( "!default", StringComparison.OrdinalIgnoreCase );
		if ( isDefault )
		{
			value = value[..^8].Trim();
		}

		// Console.WriteLine( $"Found [{key}] = [{value}] ({isDefault})" );

		sheet.SetVariable( key, value, isDefault );

		return true;
	}

	private static void TryImport( StyleSheet sheet, string filename, string includeFileAndLine )
	{
		if ( !GlobalContext.Current.FileMount.FileExists( filename ) )
			throw new System.Exception( $"Missing import {filename} ({includeFileAndLine})" );

		var text = GlobalContext.Current.FileMount.ReadAllText( filename );
		ParseToSheet( text, filename, sheet );
	}

	private static bool ParseImport( ref Parse p, StyleSheet sheet, string filename )
	{
		if ( p.Current != '@' )
			return false;

		// Don't consume if it's @mixin or @include
		if ( p.Is( "@mixin", 0, true ) || p.Is( "@include", 0, true ) )
			return false;

		var word = p.ReadWord( " ", true );

		if ( string.IsNullOrWhiteSpace( word ) )
			throw new System.Exception( $"Expected word after @ {p.FileAndLine}" );

		if ( word == "@import" )
		{
			if ( IncludeLoops > 10 )
				throw new System.Exception( $"Possible infinite @import loop {p.FileAndLine}" );

			var thisRoot = System.IO.Path.GetDirectoryName( filename );
			var files = p.ReadUntilOrEnd( ";" );

			if ( string.IsNullOrWhiteSpace( files ) )
				throw new System.Exception( $"Expected files then ; after @import {p.FileAndLine}" );

			// files could be
			//		1. "file", "file", "file"
			//		2. "file"
			//		3. 'file'

			foreach ( var file in files.Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
			{
				var cleanFile = file.Trim( ' ', '\"', '\'' );
				if ( cleanFile.StartsWith( "./" ) ) cleanFile = cleanFile.Substring( 2 );

				while ( cleanFile.StartsWith( "../" ) || cleanFile.StartsWith( "..\\" ) )
				{
					thisRoot = System.IO.Path.GetDirectoryName( thisRoot );
					cleanFile = cleanFile.Substring( 3 );
				}

				// if no extension clean it up as an include
				if ( !System.IO.Path.HasExtension( cleanFile ) ) cleanFile = $"_{cleanFile}.scss";

				// try to find file in local directory, if not found then fall back
				var localPath = System.IO.Path.Combine( thisRoot, cleanFile ).ToLower();
				if ( !GlobalContext.Current.FileMount.FileExists( localPath ) )
				{
					localPath = cleanFile.ToLower();
				}

				TryImport( sheet, localPath, p.FileAndLine );
			}

			if ( p.Is( ';' ) )
				p.Pointer++;

			return true;
		}

		throw new System.Exception( $"Unknown rule {word} {p.FileAndLine}" );
	}

	private static bool ParseKeyframes( ref Parse p, StyleSheet sheet )
	{
		var keyframe = KeyFrames.Parse( ref p, sheet );
		if ( keyframe == null )
			return false;

		sheet.AddKeyFrames( keyframe );
		return true;
	}

	/// <summary>
	/// Handle @include at the top level (outside any selector block).
	/// This expands the mixin content and parses any resulting style blocks.
	/// </summary>
	private static bool ParseTopLevelInclude( ref Parse p, StyleSheet sheet )
	{
		if ( !p.Is( "@include", 0, true ) )
			return false;

		var startLine = p.CurrentLine;
		var fileAndLine = p.FileAndLine;

		// Read the full @include statement
		var includeStatement = p.ReadUntilOrEnd( ";" );
		if ( p.Current == ';' )
			p.Pointer++;

		if ( !TryExpandInclude( includeStatement, sheet, fileAndLine, out var mixinName, out var expandedContent ) )
			throw new Exception( $"Failed to expand mixin in top-level @include {fileAndLine}" );

		// Parse the expanded content as if it were part of the stylesheet
		// This allows mixins to emit complete style blocks at the top level
		var innerParse = new Parse( expandedContent, p.FileName, startLine );

		while ( !innerParse.IsEnd )
		{
			innerParse = innerParse.SkipWhitespaceAndNewlines();
			if ( innerParse.IsEnd )
				break;

			var selector = innerParse.ReadUntilOrEnd( "{" );
			if ( string.IsNullOrWhiteSpace( selector ) )
				break;

			if ( innerParse.Current == '{' )
			{
				ReadStyleBlock( ref innerParse, selector, sheet, null );
			}
		}

		return true;
	}

	static void ReadStyleBlock( ref Parse p, string selectors, StyleSheet sheet, StyleBlock parentNode )
	{
		if ( p.Current != '{' )
			throw new System.Exception( $"Block doesn't start with {{ {p.FileAndLine}" );

		p.Pointer++;
		p = p.SkipWhitespaceAndNewlines();

		var node = new StyleBlock();
		node.LoadOrder = sheet.Nodes.Count();
		node.FileName = p.FileName;
		node.AbsolutePath = GlobalContext.Current.FileMount?.GetFullPath( p.FileName );
		node.FileLine = p.CurrentLine;
		node.SetSelector( selectors, parentNode );

		var styles = new Styles();

		while ( !p.IsEnd )
		{
			var content = p.ReadUntilOrEnd( ";{}" );
			if ( content is null ) throw new System.Exception( $"Parse Error, expected class name {p.FileAndLine}" );

			if ( p.Current == '{' )
			{
				// Check if this is an @include with a content block
				var trimmedContent = content.TrimStart();
				if ( trimmedContent.StartsWith( "@include", StringComparison.OrdinalIgnoreCase ) )
				{
					// This is @include mixin-name { ... } with a content block
					var contentBlock = ReadBracedContent( ref p );

					if ( TryExpandInclude( content, contentBlock, sheet, p.FileAndLine, out var mixinName, out var expandedContent ) )
					{
						// Pass 'node' as the context for nested selector resolution
						ProcessExpandedMixinContent( expandedContent, styles, sheet, node, p.FileName, p.CurrentLine );
						p = p.SkipWhitespaceAndNewlines();
						continue;
					}
				}

				// Regular nested selector block
				ReadStyleBlock( ref p, content, sheet, node );
				continue;
			}

			if ( p.Current == ';' )
			{
				// Check if this is an @include directive (without content block)
				if ( TryExpandInclude( content, null, sheet, p.FileAndLine, out var mixinName, out var expandedContent ) )
				{
					// Pass 'node' as the context for nested selector resolution
					ProcessExpandedMixinContent( expandedContent, styles, sheet, node, p.FileName, p.CurrentLine );
				}
				else
				{
					// Regular property
					try
					{
						content = sheet.ReplaceVariables( content );
					}
					catch ( System.Exception e )
					{
						throw new System.Exception( $"{e.Message} {p.FileAndLine}" );
					}

					styles.SetInternal( content, p.FileName, p.CurrentLine );
				}

				p.Pointer++;
				p = p.SkipWhitespaceAndNewlines();
			}

			if ( p.Current == '}' )
			{
				p.Pointer++;
				node.Styles = styles;

				// Only add this node if it's not empty
				if ( !node.IsEmpty )
				{
					sheet.Nodes.Add( node );
				}

				return;
			}
		}

		throw new System.Exception( $"Unexpected end of block {p.FileAndLine}" );
	}

	/// <summary>
	/// Process expanded mixin content, handling both properties and nested rules.
	/// The currentNode parameter is the StyleBlock where the @include appears - used for selector resolution.
	/// </summary>
	private static void ProcessExpandedMixinContent( string content, Styles styles, StyleSheet sheet, StyleBlock currentNode, string fileName, int lineOffset )
	{
		var p = new Parse( content, fileName, lineOffset );

		while ( !p.IsEnd )
		{
			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd )
				break;

			// Determine if this is a property or a nested rule
			// Properties: name: value;
			// Nested rules: selector { ... }
			//
			// The tricky part is distinguishing "color: red;" from "&:hover { }"
			// We need to look ahead to find whether { or ; comes first after the first :

			if ( IsNestedSelector( p ) )
			{
				// This is a nested rule
				var selector = p.ReadUntilOrEnd( "{" );
				if ( p.Current == '{' )
				{
					ReadStyleBlock( ref p, selector.Trim(), sheet, currentNode );
				}
			}
			else
			{
				// This is a property or @include
				var propertyName = p.ReadUntilOrEnd( ":;" );

				if ( p.Current == ':' )
				{
					p.Pointer++; // skip ':'
					p = p.SkipWhitespaceAndNewlines();

					var value = p.ReadUntilOrEnd( ";}" );

					if ( p.Current == ';' )
						p.Pointer++;

					var propertyContent = $"{propertyName}: {value}";

					try
					{
						propertyContent = sheet.ReplaceVariables( propertyContent );
					}
					catch ( Exception e )
					{
						throw new Exception( $"{e.Message} {p.FileAndLine}" );
					}

					styles.SetInternal( propertyContent, fileName, lineOffset );
				}
				else if ( p.Current == ';' )
				{
					// Could be an @include
					if ( TryExpandInclude( propertyName, sheet, p.FileAndLine, out _, out var nestedExpanded ) )
					{
						ProcessExpandedMixinContent( nestedExpanded, styles, sheet, currentNode, fileName, lineOffset );
					}
					p.Pointer++;
				}
				else
				{
					// End of content
					break;
				}
			}
		}
	}

	/// <summary>
	/// Determines if the current position in the parse is a nested selector
	/// rather than a CSS property.
	/// </summary>
	private static bool IsNestedSelector( in Parse original )
	{
		// Work with a copy since Parse is a struct
		var p = original;

		// Scan ahead to see what comes first: a { that indicates a block, or a ; that indicates a property
		// We need to handle pseudo-selectors like &:hover which contain :

		int braceDepth = 0;

		while ( !p.IsEnd )
		{
			var c = p.Current;

			if ( c == '(' ) braceDepth++;
			else if ( c == ')' ) braceDepth--;
			else if ( braceDepth == 0 )
			{
				if ( c == '{' )
				{
					// Found a { before a ; - this is a nested selector
					return true;
				}

				if ( c == ';' )
				{
					// Found a ; before a { - this is a property
					return false;
				}
			}

			p.Pointer++;
		}

		// End of content without finding either - treat as not a selector
		return false;
	}

	/// <summary>
	/// Read content within braces, handling nested braces correctly.
	/// Advances p past the closing brace.
	/// </summary>
	private static string ReadBracedContent( ref Parse p )
	{
		if ( p.Current != '{' )
			throw new Exception( $"Expected '{{' {p.FileAndLine}" );

		p.Pointer++; // skip opening brace

		int depth = 1;
		int start = p.Pointer;

		while ( !p.IsEnd && depth > 0 )
		{
			if ( p.Current == '{' ) depth++;
			else if ( p.Current == '}' ) depth--;

			if ( depth > 0 )
				p.Pointer++;
		}

		var content = p.Text.Substring( start, p.Pointer - start );

		if ( p.Current == '}' )
			p.Pointer++; // skip closing brace

		return content;
	}
}
