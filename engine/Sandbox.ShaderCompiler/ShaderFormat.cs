using System.IO;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Which format a shader file is, told by its content, never its name: a <c>.shader</c> may be a VFX source (the legacy
/// format) or a Slang one, and a <c>.shader_c</c> a vfx resource file (a VCS container) or a Slang compile
/// (<see cref="SlangOutput"/>, <c>SLGC</c>). Every place that picks a route by extension asks here, so renaming
/// <c>.slang</c> to <c>.shader</c> stays a rename.
/// </summary>
public static class ShaderFormat
{
	/// <summary>
	/// Whether <paramref name="text"/> is a VFX source: it has a top-level <c>MODES</c>, <c>VS</c>, <c>PS</c> or <c>CS</c>
	/// block (a block name at brace depth zero, outside comments and strings, followed by <c>{</c>). No Slang file can start
	/// a statement with one of those names and a brace.
	/// </summary>
	public static bool IsVfxSource( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return false;
		var depth = 0;
		for ( int i = 0; i < text.Length; i++ )
		{
			var c = text[i];
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				i = text.IndexOf( '\n', i );
				if ( i < 0 ) return false;
				continue;
			}

			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				i = text.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				if ( i < 0 ) return false;
				i++;
				continue;
			}

			if ( c == '"' )
			{
				while ( ++i < text.Length && text[i] != '"' && text[i] != '\n' ) if ( text[i] == '\\' ) i++;
				continue;
			}

			if ( c == '{' ) depth++;
			else if ( c == '}' ) depth = Math.Max( 0, depth - 1 );
			else if ( depth == 0 && char.IsLetter( c ) && (i == 0 || !IsWord( text[i - 1] )) )
			{
				var end = i;
				while ( end < text.Length && IsWord( text[end] ) ) end++;
				var word = text.AsSpan( i, end - i );
				if ( word is "MODES" or "VS" or "PS" or "CS" && StartsLine( text, i ) )
				{
					var after = end;
					while ( after < text.Length && char.IsWhiteSpace( text[after] ) ) after++;
					if ( after < text.Length && text[after] == '{' ) return true;
				}

				i = end - 1;
			}
		}

		return false;
	}

	static bool IsWord( char c ) => char.IsLetterOrDigit( c ) || c == '_';

	/// <summary>Whether only spaces come before <paramref name="at"/> on its line (<c>struct PS {</c> isn't a block).</summary>
	static bool StartsLine( string text, int at )
	{
		for ( int i = at - 1; i >= 0; i-- )
		{
			if ( text[i] == '\n' ) return true;
			if ( text[i] is not (' ' or '\t' or '\r') ) return false;
		}

		return true;
	}

	/// <summary>Whether the file at <paramref name="path"/> is a VFX source (<see cref="IsVfxSource"/>); false when it can't be read.</summary>
	public static bool IsVfxSourceFile( string path )
	{
		try
		{
			return File.Exists( path ) && IsVfxSource( File.ReadAllText( path ) );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return false;
		}
	}

	/// <summary>
	/// Whether a content folder is the engine's own (<c>game/core</c>, with the engine's pass headers): its shaders are all
	/// Slang, so a VFX <c>.shader</c> there is left from an older engine and stands in front of no module.
	/// </summary>
	public static bool IsEngineContent( string root ) => File.Exists( Path.Combine( root, "shaders", "sbox", "pass.slangh" ) );

	/// <summary>Whether the file at <paramref name="path"/> is a Slang source: it exists and isn't a VFX source.</summary>
	public static bool IsSlangSourceFile( string path ) => File.Exists( path ) && !IsVfxSourceFile( path );

	/// <summary>Whether a compiled file starting with <paramref name="start"/> is a Slang compile (<c>SLGC</c>) rather than a vfx resource.</summary>
	public static bool IsSlangCompiled( ReadOnlySpan<byte> start ) => start.Length >= 4 && start[0] == (byte)'S' && start[1] == (byte)'L' && start[2] == (byte)'G' && start[3] == (byte)'C';

	/// <summary>Whether the compiled file at <paramref name="path"/> is a Slang compile (<see cref="IsSlangCompiled"/>); false when it can't be read.</summary>
	public static bool IsSlangCompiledFile( string path )
	{
		try
		{
			if ( !File.Exists( path ) ) return false;
			using var stream = File.OpenRead( path );
			Span<byte> start = stackalloc byte[4];
			return stream.Read( start ) == 4 && IsSlangCompiled( start );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return false;
		}
	}

	/// <summary>
	/// The Slang sources in <paramref name="folder"/>: every <c>.slang</c>, and every <c>.shader</c> that's Slang by content
	/// (<see cref="IsSlangSourceFile"/>; a VFX one isn't). Nothing when the folder doesn't exist.
	/// </summary>
	public static IEnumerable<string> SlangSources( string folder, SearchOption option = SearchOption.TopDirectoryOnly )
	{
		if ( !Directory.Exists( folder ) ) return [];
		return Directory.GetFiles( folder, "*.slang", option ).Concat( Directory.GetFiles( folder, "*.shader", option ).Where( IsSlangSourceFile ) );
	}

	/// <summary>
	/// The Slang compiles in <paramref name="folder"/> shipped without their source (a package's): every <c>.slang_c</c> with
	/// no <c>.slang</c> beside it, and every <c>.shader_c</c> that's a Slang compile by content (<c>SLGC</c>) with no
	/// <c>.shader</c> beside it. The compiled files' paths; nothing when the folder doesn't exist.
	/// </summary>
	public static IEnumerable<string> CompiledAlone( string folder, SearchOption option = SearchOption.TopDirectoryOnly )
	{
		if ( !Directory.Exists( folder ) ) return [];
		return Directory.GetFiles( folder, "*.slang" + SlangOutput.Suffix, option ).Concat( Directory.GetFiles( folder, "*.shader" + SlangOutput.Suffix, option ).Where( IsSlangCompiledFile ) )
			.Where( f => !File.Exists( f[..^SlangOutput.Suffix.Length] ) );
	}

	/// <summary>
	/// Whether the file at <paramref name="path"/> is a Slang compile by its name and content: a <c>.slang_c</c>, or a
	/// <c>.shader_c</c> starting <c>SLGC</c>.
	/// </summary>
	public static bool IsSlangCompiledPath( string path ) =>
		path.EndsWith( ".slang" + SlangOutput.Suffix, StringComparison.OrdinalIgnoreCase )
		|| (path.EndsWith( ".shader" + SlangOutput.Suffix, StringComparison.OrdinalIgnoreCase ) && IsSlangCompiledFile( path ));

	/// <summary>
	/// Whether the file at <paramref name="path"/> is Slang source by its name and content: a <c>.slang</c> or <c>.slangh</c>,
	/// or a <c>.shader</c> that isn't a VFX source.
	/// </summary>
	public static bool IsSlangSourcePath( string path ) =>
		path.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) || path.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase )
		|| (path.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) && IsSlangSourceFile( path ));

	/// <summary>
	/// The shader name a Slang file stands for, <c>x.shader</c>, from its source or compile (<c>x.slang</c>,
	/// <c>x.slang_c</c>, <c>x.shader</c>, <c>x.shader_c</c>); anything else as it is.
	/// </summary>
	public static string ShaderNameOf( string path )
	{
		foreach ( var extension in (ReadOnlySpan<string>)[".slang" + SlangOutput.Suffix, ".shader" + SlangOutput.Suffix, ".slang", ".shader"] )
			if ( path.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) ) return path[..^extension.Length] + ".shader";
		return path;
	}

	/// <summary>
	/// The Slang module source of a shader named by any of its paths (<c>x.shader</c>, <c>x.shader_c</c>, <c>x.slang</c>): the
	/// <c>.slang</c> beside it, else a <c>.shader</c> beside it that isn't a VFX source; null when there's neither.
	/// </summary>
	public static string SlangSourceBeside( string path )
	{
		var name = path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? path[..^2] : path;
		var slang = Path.ChangeExtension( name, ".slang" );
		if ( File.Exists( slang ) ) return slang;
		var shader = Path.ChangeExtension( name, ".shader" );
		return IsSlangSourceFile( shader ) ? shader : null;
	}
}
