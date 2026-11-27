using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Checks the Slang shaders a player's install has: every <c>.slang</c> and <c>.slangh</c> in the game folder as
/// <see cref="UploadBuildArtifacts"/> and the Steam depot ship it, each <c>#include</c> and <c>import</c> followed through the
/// branches a Slang compile can take, must find its file in that same install. Valve's <c>.fxc</c> headers aren't ours to
/// ship, so a Slang shader that reaches any of them fails too: our own headers under <c>core/shaders/sbox</c> replace them
/// (<c>SlangHeaders.Replaced</c>).
/// </summary>
internal static partial class CheckShaderIncludes
{
	internal static ExitCode Run() => BuildDisplay.Run( "Check shader includes", () => Check( Directory.GetCurrentDirectory() ) ? ExitCode.Success : ExitCode.Failure );

	/// <summary>Check the repo at <paramref name="repoRoot"/>, logging each problem; true when there are none.</summary>
	internal static bool Check( string repoRoot )
	{
		repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( repoRoot ) );
		var problems = Problems( Path.Combine( repoRoot, "game" ), out var checkedCount, out _ );
		foreach ( var problem in problems ) Log.Error( problem );
		Log.Info( $"Shader includes: {checkedCount} shipped Slang sources, {problems.Count} problems" );
		return problems.Count == 0;
	}

	/// <summary>
	/// What's wrong with the shipped Slang sources under <paramref name="gameRoot"/>, one line each; with how many were checked
	/// and every file their includes reached (game-relative).
	/// </summary>
	internal static List<string> Problems( string gameRoot, out int checkedCount, out HashSet<string> reached )
	{
		var shipped = UploadBuildArtifacts.CollectRunnableFiles( Path.GetDirectoryName( gameRoot ) )
			.Select( f => Relative( gameRoot, f.AbsolutePath ) )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		var problems = new SortedSet<string>( StringComparer.Ordinal );
		reached = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		checkedCount = 0;
		// A .shader is a Slang source too when its content says so (Sandbox.ShaderCompiler.ShaderFormat), not only a VFX one
		foreach ( var source in shipped.Where( f => f.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) || f.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase )
			|| (f.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) && Sandbox.ShaderCompiler.ShaderFormat.IsSlangSourceFile( Path.Combine( gameRoot, f ) )) ).Order( StringComparer.Ordinal ) )
		{
			checkedCount++;
			var walk = new Walk( gameRoot, shipped, source, problems, reached );
			walk.File( source );
		}

		foreach ( var file in reached )
		{
			if ( file.EndsWith( ".fxc", StringComparison.OrdinalIgnoreCase ) && IsValves( Path.Combine( gameRoot, file ) ) )
				problems.Add( $"{file} is Valve's: our Slang shaders must not include it (rewrite what they need under core/shaders/sbox)" );
		}

		return [.. problems];
	}

	static bool IsValves( string path )
	{
		using var reader = new StreamReader( path );
		var buffer = new char[400];
		var read = reader.Read( buffer, 0, buffer.Length );
		return new string( buffer, 0, read ).Contains( "Valve Corporation", StringComparison.Ordinal );
	}

	static string Relative( string root, string path ) => Path.GetRelativePath( root, path ).Replace( '\\', '/' );

	[GeneratedRegex( @"^\s*#\s*(\w+)\s*(.*?)\s*$" )]
	private static partial Regex Directive();

	[GeneratedRegex( @"^[<""]([^"">]+)[>""]" )]
	private static partial Regex IncludeName();

	[GeneratedRegex( @"^\s*import\s+([\w.]+)\s*;" )]
	private static partial Regex Import();

	/// <summary>
	/// One shipped source's includes, followed as the preprocessor would: a branch whose condition is known (from the macros
	/// defined so far) is taken or skipped, one that isn't (a combo, the stage) is followed both ways.
	/// </summary>
	sealed class Walk( string gameRoot, HashSet<string> shipped, string source, SortedSet<string> problems, HashSet<string> reached )
	{
		readonly Dictionary<string, string> macros = new( StringComparer.Ordinal );
		readonly HashSet<string> unsure = new( StringComparer.Ordinal );
		readonly HashSet<string> open = new( StringComparer.OrdinalIgnoreCase );
		readonly string shadersRoot = ShadersRoot( source );

		/// <summary>Where a source's own includes are found besides its folder: the shaders folder it's under (an addon's).</summary>
		static string ShadersRoot( string source )
		{
			var at = source.LastIndexOf( "/shaders/", StringComparison.OrdinalIgnoreCase );
			return at < 0 ? null : source[..(at + "/shaders".Length)];
		}

		string Resolve( string from, string name )
		{
			foreach ( var folder in new[] { Path.GetDirectoryName( from )?.Replace( '\\', '/' ), shadersRoot, "core/shaders" } )
			{
				if ( folder is null ) continue;
				var candidate = Relative( gameRoot, Path.GetFullPath( Path.Combine( gameRoot, folder, name ) ) );
				if ( System.IO.File.Exists( Path.Combine( gameRoot, candidate ) ) ) return candidate;
			}

			return null;
		}

		public void File( string file )
		{
			if ( !open.Add( file ) ) return;
			var lines = System.IO.File.ReadAllLines( Path.Combine( gameRoot, file ) );

			// Each open #if: whether this branch is live (true, false or null for either), and whether an earlier one surely was
			var branches = new Stack<(bool? Live, bool Taken, bool? Parent)>();
			bool? Live() => branches.Count == 0 ? true : branches.Peek().Live;
			var comment = false;

			for ( int i = 0; i < lines.Length; i++ )
			{
				var line = Uncommented( lines[i], ref comment );
				while ( line.EndsWith( '\\' ) && i + 1 < lines.Length ) line = line[..^1] + " " + Uncommented( lines[++i], ref comment );

				var directive = Directive().Match( line );
				if ( !directive.Success )
				{
					if ( Live() is not false && Import().Match( line ) is { Success: true } import )
						Include( file, i, import.Groups[1].Value.Replace( '.', '/' ) + ".slang" );
					continue;
				}

				var argument = directive.Groups[2].Value;
				switch ( directive.Groups[1].Value )
				{
					case "if":
					case "ifdef":
					case "ifndef":
						{
							var parent = Live();
							var condition = directive.Groups[1].Value switch
							{
								"ifdef" => Defined( argument ),
								"ifndef" => Not( Defined( argument ) ),
								_ => Evaluate( argument ),
							};
							var live = And( parent, condition );
							branches.Push( (live, live is true, parent) );
							break;
						}
					case "elif":
					case "else":
						{
							if ( branches.Count == 0 ) break;
							var (live, taken, parent) = branches.Pop();
							var condition = directive.Groups[1].Value == "else" ? true : Evaluate( argument );
							var before = taken ? false : live is false ? (bool?)true : null;
							var now = And( parent, And( before, condition ) );
							branches.Push( (now, taken || now is true, parent) );
							break;
						}
					case "endif":
						if ( branches.Count > 0 ) branches.Pop();
						break;
					case "define":
						if ( Live() is false ) break;
						var name = Regex.Match( argument, @"^\w+" ).Value;
						if ( name.Length == 0 ) break;
						macros[name] = argument[name.Length..].Trim();
						if ( Live() is null ) unsure.Add( name ); else unsure.Remove( name );
						break;
					case "undef":
						if ( Live() is false ) break;
						macros.Remove( argument.Trim() );
						if ( Live() is null ) unsure.Add( argument.Trim() ); else unsure.Remove( argument.Trim() );
						break;
					case "include":
						if ( Live() is false ) break;
						if ( IncludeName().Match( argument ) is { Success: true } include ) Include( file, i, include.Groups[1].Value );
						break;
				}
			}

			open.Remove( file );
		}

		void Include( string from, int line, string name )
		{
			// A Slang compile includes our header in place of one it replaces (Sandbox.ShaderCompiler.SlangHeaders)
			if ( Sandbox.ShaderCompiler.SlangHeaders.Replaced.TryGetValue( name, out var ours ) ) name = ours;
			var target = Resolve( from, name );
			if ( target is null )
			{
				problems.Add( $"{source}: {from}({line + 1}) includes {name}, which isn't in the game folder" );
				return;
			}

			reached.Add( target );
			if ( !shipped.Contains( target ) )
				problems.Add( $"{source}: {from}({line + 1}) includes {target}, which a player's install doesn't have" );

			if ( target.EndsWith( ".fxc", StringComparison.OrdinalIgnoreCase ) || target.EndsWith( ".hlsl", StringComparison.OrdinalIgnoreCase ) || target.EndsWith( ".slangh", StringComparison.OrdinalIgnoreCase ) || target.EndsWith( ".slang", StringComparison.OrdinalIgnoreCase ) || target.EndsWith( ".h", StringComparison.OrdinalIgnoreCase ) )
				File( target );
		}

		static string Uncommented( string line, ref bool comment )
		{
			var text = new System.Text.StringBuilder();
			for ( int i = 0; i < line.Length; i++ )
			{
				if ( comment )
				{
					if ( line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/' ) { comment = false; i++; }
					continue;
				}

				if ( line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/' ) break;
				if ( line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*' ) { comment = true; i++; continue; }
				text.Append( line[i] );
			}

			return text.ToString().TrimEnd();
		}

		bool? Defined( string name )
		{
			name = name.Trim();
			if ( unsure.Contains( name ) ) return null;
			return macros.ContainsKey( name );
		}

		static bool? Not( bool? value ) => value is null ? null : !value;
		static bool? And( bool? a, bool? b ) => a is false || b is false ? false : a is true && b is true ? true : null;
		static bool? Or( bool? a, bool? b ) => a is true || b is true ? true : a is false && b is false ? false : null;

		/// <summary>An #if condition: defined(), !, &amp;&amp;, || and parentheses over known macros; anything else is unknown.</summary>
		bool? Evaluate( string expression )
		{
			var tokens = Regex.Matches( expression, @"&&|\|\||[!()]|defined|\w+|\S" ).Select( m => m.Value ).ToList();
			var at = 0;
			bool? Expression()
			{
				var value = Term();
				while ( at < tokens.Count && tokens[at] == "||" ) { at++; value = Or( value, Term() ); }
				return value;
			}

			bool? Term()
			{
				var value = Factor();
				while ( at < tokens.Count && tokens[at] == "&&" ) { at++; value = And( value, Factor() ); }
				return value;
			}

			bool? Factor()
			{
				if ( at >= tokens.Count ) return null;
				var token = tokens[at++];
				if ( token == "!" ) return Not( Factor() );
				if ( token == "defined" )
				{
					var parenthesised = at < tokens.Count && tokens[at] == "(";
					if ( parenthesised ) at++;
					var value = at < tokens.Count ? Defined( tokens[at++] ) : null;
					if ( parenthesised && at < tokens.Count && tokens[at] == ")" ) at++;
					return value;
				}

				if ( token == "(" )
				{
					var value = Expression();
					if ( at < tokens.Count && tokens[at] == ")" ) at++;
					else return null;
					return value;
				}

				// A comparison or arithmetic: unknown, and the rest of this operand with it
				bool? result = token == "0" ? false : token == "1" ? true : null;
				while ( at < tokens.Count && tokens[at] is not ("&&" or "||" or ")") ) { at++; result = null; }
				return result;
			}

			var result = Expression();
			return at == tokens.Count ? result : null;
		}
	}
}
