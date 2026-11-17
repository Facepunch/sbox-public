using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Converts a <c>.shader</c> that isn't a material (post-processing, UI, tools, compute, the engine's fullscreen passes)
/// into a pass module (<see cref="PassModule"/>): its blocks in file order, each program's block in its
/// <c>#if ( PROGRAM == ... )</c>, <c>[shader]</c> on its entry points, the HEADER and MODES as comments, and the
/// declarations vfx reads (combos, features, render states) kept as written for the compiler to read. The code itself is
/// unchanged: vfx compiles it with Slang already.
/// </summary>
public static partial class PassConverter
{
	static readonly string[] Programs = ["VS", "PS", "CS", "GS", "HS", "DS"];

	/// <summary>Why a shader isn't converted, or null when it is.</summary>
	public static string Refuse( string shader )
	{
		var blocks = Blocks( shader );
		if ( blocks.Exists( b => b.Name is "GS" or "HS" or "DS" ) ) return "it has a geometry or tessellation stage, which Borealis.Gpu doesn't draw";
		if ( !blocks.Exists( b => b.Name is "PS" or "CS" ) ) return "it has no pixel or compute stage";
		return null;
	}

	/// <summary>The pass module for <paramref name="shader"/>'s text, named <paramref name="name"/> in its comments.</summary>
	public static string Convert( string shader, string name )
	{
		var blocks = Blocks( shader );
		var text = new StringBuilder();
		var included = false;
		text.Append( "// " ).Append( name ).Append( ".shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes\n" );
		text.Append( "// and as the fallback (docs/managed/shader-format-replacement.md, \"Pass modules\").\n" );

		foreach ( var block in blocks )
		{
			switch ( block.Name )
			{
				case "HEADER":
					foreach ( var line in Lines( block.Body ) ) text.Append( "// HEADER: " ).Append( line.Trim() ).Append( '\n' );
					break;
				case "MODES":
					foreach ( var line in Lines( block.Body ) ) text.Append( "// MODES: " ).Append( line.Trim() ).Append( '\n' );
					if ( !included ) text.Append( "\n#include \"sbox/pass.slangh\"\n" );
					included = true;
					break;
				case "FEATURES":
				case "COMMON":
				case null:
					// Code before MODES (an include of feature lines) needs the declarations' macros first
					if ( !included && Lines( block.Body ).Any( l => !l.TrimStart().StartsWith( "//", StringComparison.Ordinal ) ) )
					{
						text.Append( "\n#include \"sbox/pass.slangh\"\n" );
						included = true;
					}

					text.Append( block.Body );
					break;
				default:
					var program = block.Name;
					text.Append( "#if ( PROGRAM == VFX_PROGRAM_" ).Append( program ).Append( " )\n" );
					text.Append( Entries( block.Body, program ) );
					text.Append( "\n#endif // " ).Append( program ).Append( '\n' );
					break;
			}
		}

		// A shader without a MODES block still gets the header, first
		var result = text.ToString();
		if ( !result.Contains( "#include \"sbox/pass.slangh\"", StringComparison.Ordinal ) )
		{
			var at = result.IndexOf( '\n', result.IndexOf( '\n' ) + 1 ) + 1;
			result = result.Insert( at, "\n#include \"sbox/pass.slangh\"\n" );
		}

		return Regex.Replace( result.Replace( "\r\n", "\n" ), @"\n{3,}", "\n\n" );
	}

	/// <summary>
	/// Why a compiled shader's embedded source (each program's text, by <c>VFX_PROGRAM_*</c> name) isn't converted by
	/// <see cref="FromEmbedded"/>, or null when it is.
	/// </summary>
	public static string RefuseEmbedded( IReadOnlyDictionary<string, string> programs )
	{
		if ( programs is null || programs.Count == 0 ) return "it has no embedded source";
		if ( programs.Keys.Any( p => p is "VFX_PROGRAM_GS" or "VFX_PROGRAM_HS" or "VFX_PROGRAM_DS" ) ) return "it has a geometry or tessellation stage, which Borealis.Gpu doesn't draw";
		if ( !programs.ContainsKey( "VFX_PROGRAM_PS" ) && !programs.ContainsKey( "VFX_PROGRAM_CS" ) ) return "it has no pixel or compute stage";
		return null;
	}

	/// <summary>
	/// The pass module for a compiled shader's embedded source: each program's text (the <c>.shader</c> with the other
	/// programs' blocks blanked, its own includes expanded, as the compiler embeds it in a package's <c>.shader_c</c>) in
	/// its <c>#if ( PROGRAM == ... )</c> with <c>[shader]</c> on its entry point, and the shader's features (from the
	/// compiled file: the FEATURES block isn't in any program's text) declared first, so static combos that follow them
	/// get their ranges.
	/// </summary>
	public static string FromEmbedded( IReadOnlyDictionary<string, string> programs, IEnumerable<(string Name, int Min, int Max)> features, string name )
	{
		var text = new StringBuilder();
		text.Append( "// " ).Append( name ).Append( " from the source embedded in its compiled file, as a pass module (docs/managed/pass-modules.md)\n" );
		text.Append( "#include \"sbox/pass.slangh\"\n" );
		foreach ( var (feature, min, max) in features ?? [] ) text.Append( "Feature( " ).Append( feature ).Append( ", " ).Append( min ).Append( ".." ).Append( max ).Append( ", \"\" );\n" );

		foreach ( var (key, program) in new[] { ("VFX_PROGRAM_VS", "VS"), ("VFX_PROGRAM_PS", "PS"), ("VFX_PROGRAM_CS", "CS") } )
		{
			if ( !programs.TryGetValue( key, out var source ) ) continue;
			text.Append( "\n#if ( PROGRAM == " ).Append( key ).Append( " )\n" );
			// #line names the .shader, which isn't here: includes are found from the module's folder, and the lines stay
			var body = LineDirective().Replace( source.Replace( "\r\n", "\n" ), "" );
			text.Append( Entries( body, program ) );
			text.Append( "\n#endif // " ).Append( program ).Append( '\n' );
		}

		return text.ToString();
	}

	/// <summary><c>[shader( "stage" )]</c> before each definition of the program's entry point.</summary>
	static string Entries( string body, string program )
	{
		var (entry, stage) = program switch
		{
			"VS" => ("MainVs", "vertex"),
			"PS" => ("MainPs", "pixel"),
			"CS" => ("MainCs", "compute"),
			_ => (null, null),
		};

		if ( entry is null ) return body;
		return Regex.Replace( body, $@"^(?<lead>[ \t]*)(?<sig>[^\n;]*?\b{entry}\s*\((?:[^;{{]|\n)*?\)\s*(?::\s*\w+\s*)?(?=\{{|\n\s*\{{))", m =>
			$"{m.Groups["lead"].Value}[shader( \"{stage}\" )] {m.Groups["sig"].Value}", RegexOptions.Multiline );
	}

	static IEnumerable<string> Lines( string body ) => body.Replace( "\r\n", "\n" ).Split( '\n' ).Where( l => l.Trim().Length > 0 && l.Trim() is not "{" and not "}" );

	sealed record Block( string Name, string Body );

	/// <summary>The file in order: named blocks (<c>NAME { ... }</c>, braces stripped) and the code between them (Name null).</summary>
	static List<Block> Blocks( string text )
	{
		text = text.Replace( "\r\n", "\n" );
		var blocks = new List<Block>();
		var loose = new StringBuilder();
		int i = 0;
		while ( i < text.Length )
		{
			var m = BlockStart().Match( text, i );
			if ( !m.Success || m.Index != i || !IsBlockName( m.Groups["name"].Value ) )
			{
				// Copy up to the next line, skipping comments and strings whole so braces in them don't count
				var end = SkipLine( text, i );
				loose.Append( text, i, end - i );
				i = end;
				continue;
			}

			var open = text.IndexOf( '{', m.Index + m.Length - 1 );
			var close = Matching( text, open );
			if ( loose.Length > 0 )
			{
				blocks.Add( new Block( null, loose.ToString() ) );
				loose.Clear();
			}

			blocks.Add( new Block( m.Groups["name"].Value, Dedent( text[(open + 1)..close] ) ) );
			i = close + 1;
		}

		if ( loose.Length > 0 ) blocks.Add( new Block( null, loose.ToString() ) );
		return blocks;
	}

	static bool IsBlockName( string name ) => name is "HEADER" or "MODES" or "FEATURES" or "COMMON" || Array.IndexOf( Programs, name ) >= 0;

	[GeneratedRegex( @"(?<=^|\n)[ \t]*(?<name>[A-Z]+)[ \t]*\n?[ \t]*\{", RegexOptions.Singleline )]
	private static partial Regex BlockStart();

	[GeneratedRegex( @"^[ \t]*#[ \t]*line\b[^\n]*", RegexOptions.Multiline )]
	private static partial Regex LineDirective();

	/// <summary>The end of the line starting at <paramref name="i"/>, past any block comment it opens.</summary>
	static int SkipLine( string text, int i )
	{
		while ( i < text.Length && text[i] != '\n' )
		{
			if ( text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				var end = text.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				i = end < 0 ? text.Length : end + 2;
				continue;
			}

			if ( text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				var end = text.IndexOf( '\n', i );
				return end < 0 ? text.Length : end + 1;
			}

			i++;
		}

		return Math.Min( i + 1, text.Length );
	}

	/// <summary>The brace closing the one at <paramref name="open"/>, past comments and strings.</summary>
	static int Matching( string text, int open )
	{
		var depth = 0;
		for ( int i = open; i < text.Length; i++ )
		{
			var c = text[i];
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				i = text.IndexOf( '\n', i );
				if ( i < 0 ) break;
				continue;
			}

			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				i = text.IndexOf( "*/", i + 2, StringComparison.Ordinal ) + 1;
				if ( i <= 0 ) break;
				continue;
			}

			if ( c == '"' )
			{
				i = text.IndexOf( '"', i + 1 );
				if ( i < 0 ) break;
				continue;
			}

			if ( c == '{' ) depth++;
			if ( c == '}' && --depth == 0 ) return i;
		}

		throw new FormatException( $"unbalanced braces from offset {open}" );
	}

	/// <summary>A block's body one tab less indented, without its first and last blank lines.</summary>
	static string Dedent( string body )
	{
		var lines = body.Split( '\n' ).ToList();
		while ( lines.Count > 0 && lines[0].Trim().Length == 0 ) lines.RemoveAt( 0 );
		while ( lines.Count > 0 && lines[^1].Trim().Length == 0 ) lines.RemoveAt( lines.Count - 1 );
		var indented = lines.Where( l => l.Trim().Length > 0 ).ToList();
		var tab = indented.Count > 0 && indented.All( l => l.StartsWith( '\t' ) );
		return string.Join( '\n', lines.Select( l => tab && l.StartsWith( '\t' ) ? l[1..] : l ) ) + "\n";
	}
}
