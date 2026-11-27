using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// The shader API level: one integer for what shader authors may rely on (the public modules and headers, the surface
/// interfaces, the attribute and render state vocabularies, generated names). It goes up only for a breaking change;
/// additions don't change it. Slang code sees it as <c>SBOX_SHADER_API</c>: a define in every compile, and a
/// <c>static const int</c> in <c>sbox.material</c>. Every offline compile (<see cref="SlangOutput"/>) records the level it
/// was compiled against. The engine supports the current level and the one before it; names the previous level had are
/// kept as shims marked <c>[deprecated( "..." )]</c>, which warn at compile time naming the replacement. See
/// docs/reference/shader-api.md.
/// </summary>
public static partial class ShaderApi
{
	/// <summary>The current level. Raise it with a breaking change, keep shims for the level before, and log it in the changelog.</summary>
	public const int Level = 1;

	/// <summary>The oldest level still supported: <see cref="Level"/> or the one before, until 12 months after its successor ships.</summary>
	public const int Oldest = 1;

	/// <summary>The define (and <c>sbox.material</c> constant) that holds the level in Slang code.</summary>
	public const string Define = "SBOX_SHADER_API";

	/// <summary>Whether shaders compiled against <paramref name="level"/> still compile, through its shims when it's the previous one.</summary>
	public static bool Supported( int level ) => level >= Oldest && level <= Level;

	/// <summary>The levels supported, for messages: <c>1</c>, or <c>2–3</c>.</summary>
	public static string Range => Oldest == Level ? $"{Level}" : $"{Oldest}–{Level}";

	/// <summary>
	/// The one rejection message for a compiled shader the engine can't use and can't recompile (spec 2.4):
	/// <c>shaders/foo.shader (package x.y): compiled for shader API 1; this engine supports 2–3. Open it in the editor to upgrade.</c>
	/// </summary>
	public static string Rejection( string shader, string package, string reason ) =>
		$"{shader}{(string.IsNullOrEmpty( package ) ? "" : $" (package {package})")}: {reason}. Open it in the editor to upgrade.";

	/// <summary>The reason for a compiled shader at a level the engine dropped (or a newer one than it knows).</summary>
	public static string LevelReason( int level ) => $"compiled for shader API {level}; this engine supports {Range}";

	/// <summary>
	/// Slang's deprecation warnings in a successful compile's diagnostics (a shim marked <c>[deprecated( "..." )]</c> used),
	/// one line each as <c>path(line): warning: Old has been deprecated: use New (shader API 2)</c>: the shim's message names
	/// the replacement. Reads Slang's multi-line diagnostics (a <c>warning[E31200]</c> header, a <c>--&gt;</c> location and the
	/// message under the caret) and its one-line form.
	/// </summary>
	public static IEnumerable<string> Deprecations( string diagnostics )
	{
		if ( string.IsNullOrEmpty( diagnostics ) || diagnostics.IndexOf( "deprecated", StringComparison.OrdinalIgnoreCase ) < 0 ) yield break;
		var lines = diagnostics.Replace( "\r", "" ).Split( '\n' );
		for ( int i = 0; i < lines.Length; i++ )
		{
			var line = lines[i];
			if ( OneLine().Match( line ) is { Success: true } single )
			{
				yield return $"{single.Groups["path"].Value}({single.Groups["line"].Value}): warning: {single.Groups["message"].Value.Trim()}";
				continue;
			}

			if ( !Header().IsMatch( line ) ) continue;
			string location = null, message = null;
			for ( int j = i + 1; j < lines.Length && !Header().IsMatch( lines[j] ) && !lines[j].StartsWith( "error", StringComparison.Ordinal ); j++ )
			{
				if ( location is null && Location().Match( lines[j] ) is { Success: true } at ) location = $"{at.Groups["path"].Value}({at.Groups["line"].Value})";
				else if ( message is null && Caret().Match( lines[j] ) is { Success: true } caret ) message = caret.Groups["message"].Value.Trim();
				if ( location is not null && message is not null ) break;
			}

			yield return $"{location ?? "?"}: warning: {message ?? line.Trim()}";
		}
	}

	[GeneratedRegex( @"^warning\[E\d+\]:.*deprecated", RegexOptions.IgnoreCase )]
	private static partial Regex Header();

	[GeneratedRegex( @"^\s*-->\s*(?<path>.+?):(?<line>\d+):\d+\s*$" )]
	private static partial Regex Location();

	[GeneratedRegex( @"^\s*\|\s*\^+\s*(?<message>\S.*)$" )]
	private static partial Regex Caret();

	[GeneratedRegex( @"^(?<path>.+?)\((?<line>\d+)\):\s*warning\s*\d*:\s*(?<message>.*deprecated.*)$", RegexOptions.IgnoreCase )]
	private static partial Regex OneLine();

	/// <summary>
	/// Whether <paramref name="name"/> (as an include names it, <c>sbox/view.slangh</c>) is public: every <c>sbox/*.slangh</c>
	/// header and <c>sbox/*.slang</c> module is, and authors may rely on it from one level to the next. Only the <c>.fxc</c>
	/// names, Valve's old headers, aren't: they're legacy shims (<see cref="SlangHeaders.Replaced"/>).
	/// </summary>
	public static bool IsPublic( string name )
	{
		name = name.Replace( '\\', '/' );
		return name.StartsWith( "sbox/", StringComparison.OrdinalIgnoreCase )
			&& (name.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ));
	}

	/// <summary>
	/// The warning for an author's Slang including <paramref name="name"/>, naming our header to use where there is one; null
	/// when it's public or not a Valve <c>.fxc</c> name (the author's own files, HLSL headers). With <paramref name="import"/>
	/// it's a module name (<c>sbox.surface</c>).
	/// </summary>
	public static string IncludeWarning( string name, bool import = false )
	{
		if ( import )
		{
			if ( !name.StartsWith( "sbox.", StringComparison.Ordinal ) ) return null;
			name = name.Replace( '.', '/' ) + ".slang";
		}

		name = name.Replace( '\\', '/' );
		if ( IsPublic( name ) ) return null;
		if ( SlangHeaders.Replaced.TryGetValue( name, out var ours ) )
			return $"\"{name}\" is a legacy shim for old .shader code: include \"{ours}\" instead";

		if ( !name.EndsWith( ".fxc", StringComparison.OrdinalIgnoreCase ) || name.Contains( '/' ) ) return null;
		return $"\"{name}\" is one of Valve's old headers and may change in any release: include an sbox/ header instead";
	}

	/// <summary>Every <c>#include "x"</c> and <c>import x;</c> in <paramref name="text"/>, with its line (from 1).</summary>
	public static IEnumerable<(int Line, string Name, bool Import)> References( string text )
	{
		if ( text.IndexOf( "include", StringComparison.Ordinal ) < 0 && text.IndexOf( "import", StringComparison.Ordinal ) < 0 ) yield break;
		var line = 1;
		var at = 0;
		foreach ( Match m in Reference().Matches( text ) )
		{
			for ( ; at < m.Index; at++ )
			{
				if ( text[at] == '\n' ) line++;
			}

			yield return m.Groups["include"].Success ? (line, m.Groups["include"].Value, false) : (line, m.Groups["import"].Value, true);
		}
	}

	[GeneratedRegex( @"^[ \t]*(?:#[ \t]*include[ \t]*[""<](?<include>[^"">\r\n]+)[>""]|(?:__exported[ \t]+)?import[ \t]+(?<import>[A-Za-z_][\w.]*)[ \t]*;)", RegexOptions.Multiline )]
	private static partial Regex Reference();

	/// <summary>
	/// Whether a module was made by the converters from a <c>.shader</c> (<see cref="PassConverter"/>): legacy code, which
	/// includes the shims on purpose, so it isn't warned about.
	/// </summary>
	public static bool IsConverted( string text ) => text.StartsWith( "// ", StringComparison.Ordinal ) && text.AsSpan( 0, Math.Min( text.Length, 400 ) ).IndexOf( "as a pass module", StringComparison.Ordinal ) >= 0;
}
