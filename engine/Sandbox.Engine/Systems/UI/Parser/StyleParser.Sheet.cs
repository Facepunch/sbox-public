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

			if ( ParseImport( ref p, sheet, filename ) )
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
				ReadStyleBlock( ref p, content, sheet, node );
				continue;
			}

			if ( p.Current == ';' )
			{
				try
				{
					content = sheet.ReplaceVariables( content );
				}
				catch ( System.Exception e )
				{
					throw new System.Exception( $"{e.Message} {p.FileAndLine}" );
				}

				styles.SetInternal( content, p.FileName, p.CurrentLine );
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
}
