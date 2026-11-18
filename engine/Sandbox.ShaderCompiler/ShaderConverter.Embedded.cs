using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

public static partial class ShaderConverter
{
	/// <summary>
	/// A <c>.shader</c>'s text again from the source a compiled file embeds (a package's <c>.shader_c</c>), for
	/// <see cref="Convert"/>: each program's text is the whole file with the other programs' blocks and every block's
	/// header blanked in place, so the programs line up line for line. A line the vertex and pixel text share is
	/// <c>COMMON</c>, one only the vertex text has is <c>VS</c>, one only the pixel text has <c>PS</c>; the features come from
	/// the compiled file, since no program's text has the FEATURES block. Null without a pixel program.
	/// </summary>
	public static string FromEmbedded( IReadOnlyDictionary<string, string> programs, IEnumerable<(string Name, int Min, int Max)> features )
	{
		if ( programs is null || !programs.TryGetValue( "VFX_PROGRAM_PS", out var pixel ) ) return null;
		programs.TryGetValue( "VFX_PROGRAM_VS", out var vertex );

		var ps = Lines( pixel );
		var vs = vertex is null ? null : Lines( vertex );
		var common = new StringBuilder();
		var vsBlock = new StringBuilder();
		var psBlock = new StringBuilder();

		if ( vs is null || vs.Length != ps.Length )
		{
			// Not lined up (no vertex program, or a compiler that didn't blank in place): each program whole in its block
			psBlock.AppendJoin( '\n', ps );
			if ( vs is not null ) vsBlock.AppendJoin( '\n', vs );
		}
		else
		{
			for ( int n = 0; n < ps.Length; n++ )
			{
				var (v, p) = (vs[n], ps[n]);
				var (vBlank, pBlank) = (string.IsNullOrWhiteSpace( v ), string.IsNullOrWhiteSpace( p ));
				if ( vBlank && pBlank ) continue;
				if ( v == p ) common.Append( p ).Append( '\n' );
				else
				{
					if ( !vBlank ) vsBlock.Append( v ).Append( '\n' );
					if ( !pBlank ) psBlock.Append( p ).Append( '\n' );
				}
			}
		}

		var text = new StringBuilder();
		text.Append( "FEATURES\n{\n" );
		foreach ( var (name, min, max) in features ?? [] ) text.Append( "\tFeature( " ).Append( name ).Append( ", " ).Append( min ).Append( ".." ).Append( max ).Append( ", \"\" );\n" );
		text.Append( "}\n\nCOMMON\n{\n" ).Append( common ).Append( "}\n\n" );
		if ( vs is not null ) text.Append( "VS\n{\n" ).Append( vsBlock ).Append( "}\n\n" );
		text.Append( "PS\n{\n" ).Append( psBlock ).Append( "}\n" );
		return text.ToString();
	}

	/// <summary>A program's lines with the compiler's <c>#line</c> directives blanked (they name the <c>.shader</c>, which isn't here).</summary>
	static string[] Lines( string program ) => EmbeddedLineDirective().Replace( program.Replace( "\r\n", "\n" ), "" ).Split( '\n' );

	[GeneratedRegex( @"^[ \t]*#line\b[^\n]*", RegexOptions.Multiline )]
	private static partial Regex EmbeddedLineDirective();
}
