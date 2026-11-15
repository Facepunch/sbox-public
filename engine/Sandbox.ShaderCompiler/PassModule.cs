using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A pass module: a fullscreen, UI, tools or compute shader on the raw tier, with no <c>[Material]</c> record. Its entry
/// points are <c>MainVs</c> and <c>MainPs</c>, or <c>MainCs</c>; it keeps the declarations of the <c>.shader</c> it was
/// converted from (<c>DynamicCombo</c>, <c>StaticCombo</c>, <c>Feature</c>, <c>RenderState</c>), which
/// <c>sbox/pass.slangh</c> compiles to nothing and the compiler reads here. Each combo is a define: a module compiles
/// once per stage and combo values, as vfx compiles each combo. See docs/managed/shader-format-replacement.md.
/// </summary>
public sealed partial class PassModule
{
	/// <summary>The module's file.</summary>
	public string Path { get; }

	/// <summary>Its text.</summary>
	public string Text { get; }

	/// <summary>Its features (<c>Feature( F_X, 0..1 )</c>), which static combos can follow.</summary>
	public IReadOnlyList<PassCombo> Features { get; }

	readonly Dictionary<StageKind, IReadOnlyList<PassCombo>> combos = new();
	readonly Dictionary<StageKind, List<(string Kind, string[] Arguments)>> rules = new();
	readonly HashSet<StageKind> stages = new();

	readonly IReadOnlyList<string> searchPaths;

	PassModule( string path, string text, IReadOnlyList<string> searchPaths )
	{
		Path = path;
		Text = text;
		this.searchPaths = searchPaths ?? [];

		// Each stage's lines, its includes' with them: what declares its combos, features and entry point
		var lines = new Dictionary<StageKind, List<string>>();
		foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } ) lines[stage] = PassPreprocessor.ActiveLines( path, text, ProgramValues( stage ), Include );

		var features = new List<PassCombo>();
		foreach ( var line in lines.Values.SelectMany( l => l ) )
		{
			var m = FeatureLine().Match( line );
			if ( !m.Success || features.Exists( f => f.Name == m.Groups["name"].Value ) ) continue;
			var (min, max) = Range( m.Groups["range"].Value );
			features.Add( new PassCombo( m.Groups["name"].Value, min, max, true, null ) );
		}

		Features = features;

		foreach ( var (stage, active) in lines )
		{
			var found = new List<PassCombo>();
			var stageRules = new List<(string, string[])>();
			foreach ( var line in active )
			{
				if ( RuleLine().Match( line ) is { Success: true } rule )
				{
					stageRules.Add( (rule.Groups["kind"].Value, rule.Groups["args"].Value.Split( ',', StringSplitOptions.TrimEntries )) );
					continue;
				}

				// An entry point marked [shader], or one an include defines (UI shaders' MainVs comes from ui/vertex.hlsl)
				var comment = line.IndexOf( "//", StringComparison.Ordinal );
				var code = comment >= 0 ? line[..comment] : line;
				if ( EntryLine( stage ).IsMatch( code ) || EntryDefinition( stage ).IsMatch( code ) ) stages.Add( stage );
				var m = ComboLine().Match( line );
				if ( !m.Success ) continue;

				var name = m.Groups["name"].Value;
				if ( found.Exists( c => c.Name == name ) ) continue;
				var range = m.Groups["range"].Value.Trim();
				var feature = m.Groups["feature"].Success ? m.Groups["feature"].Value : range.StartsWith( "F_", StringComparison.Ordinal ) ? range : null;
				int min, max;
				if ( range.StartsWith( "F_", StringComparison.Ordinal ) )
				{
					var f = features.Find( x => x.Name == feature );
					(min, max) = f is null ? (0, 1) : (f.Min, f.Max);
				}
				else
				{
					(min, max) = Range( range );
				}

				found.Add( new PassCombo( name, min, max, m.Groups["kind"].Value == "Static", feature ) );
			}

			combos[stage] = found;
			rules[stage] = stageRules;
		}
	}

	/// <summary>Read the module at <paramref name="path"/>, finding its includes beside it and in <paramref name="searchPaths"/>.</summary>
	public static PassModule Read( string path, IReadOnlyList<string> searchPaths = null ) => new( System.IO.Path.GetFullPath( path ), File.ReadAllText( path ), searchPaths );

	/// <summary>Read a module from text, reported as <paramref name="path"/>.</summary>
	public static PassModule FromText( string path, string text, IReadOnlyList<string> searchPaths = null ) => new( path, text, searchPaths );

	static readonly Dictionary<string, (DateTime Time, string Text)> includes = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>An include's path and text, from the including file's folder or the search paths; null when there's none.</summary>
	(string Path, string Text)? Include( string from, string name )
	{
		name = name.Replace( '\\', '/' );
		var folders = new List<string>();
		if ( System.IO.Path.GetDirectoryName( from ) is { Length: > 0 } here ) folders.Add( here );
		folders.AddRange( searchPaths );
		foreach ( var folder in folders )
		{
			var full = System.IO.Path.GetFullPath( System.IO.Path.Combine( folder, name ) );
			var info = new FileInfo( full );
			if ( !info.Exists ) continue;
			lock ( includes )
			{
				if ( !includes.TryGetValue( full, out var known ) || known.Time != info.LastWriteTimeUtc ) includes[full] = known = (info.LastWriteTimeUtc, File.ReadAllText( full ));
				return (full, known.Text);
			}
		}

		return null;
	}

	/// <summary>Whether it has an entry point for the stage (<c>MainVs</c>, <c>MainPs</c>, <c>MainCs</c>).</summary>
	public bool Has( StageKind stage ) => stages.Contains( stage );

	/// <summary>
	/// Whether the stage has its entry point with these combo values: a material's depth pass can have no pixel stage
	/// (<c>#if</c> round its <c>MainPs</c>).
	/// </summary>
	public bool Has( StageKind stage, IReadOnlyDictionary<string, int> values )
	{
		if ( !Has( stage ) ) return false;
		var all = ProgramValues( stage );
		foreach ( var (name, value) in values ) all[name] = value;
		foreach ( var line in PassPreprocessor.ActiveLines( Path, Text, all, Include ) )
		{
			var comment = line.IndexOf( "//", StringComparison.Ordinal );
			var code = comment >= 0 ? line[..comment] : line;
			if ( EntryLine( stage ).IsMatch( code ) || EntryDefinition( stage ).IsMatch( code ) ) return true;
		}

		return false;
	}

	/// <summary>The combos a stage's code declares: those of its own block and the common code.</summary>
	public IReadOnlyList<PassCombo> Combos( StageKind stage ) => combos.TryGetValue( stage, out var list ) ? list : [];

	/// <summary>
	/// Whether a stage's <c>DynamicComboRule</c>s allow these combo values: <c>Requires1( D_X, a, b )</c> (with D_X set, each
	/// of the rest is true) and <c>Allow1( a, b )</c> (at most one is set). vfx compiles no other combination; drawing one
	/// takes the dynamic combo with every value at its first, as a compiled program's lookup does.
	/// </summary>
	public bool Allows( StageKind stage, IReadOnlyDictionary<string, int> values )
	{
		if ( !rules.TryGetValue( stage, out var list ) ) return true;
		bool True( string expression ) => PassPreprocessor.Evaluate( expression, values ) is { } v && v != "0";
		foreach ( var (kind, arguments) in list )
		{
			if ( kind == "Requires1" && True( arguments[0] ) && !arguments.Skip( 1 ).All( True ) ) return false;
			if ( kind == "Allow1" && arguments.Count( True ) > 1 ) return false;
		}

		return true;
	}

	/// <summary>The entry point a stage compiles.</summary>
	public static string Entry( StageKind stage ) => stage switch { StageKind.Vertex => "MainVs", StageKind.Compute => "MainCs", _ => "MainPs" };

	/// <summary>
	/// The render states the pixel stage sets with these combo and feature values, in order: each state's name and its
	/// value, evaluated (a number, or a constant's name such as <c>INV_SRC_ALPHA</c>). States whose value doesn't evaluate
	/// are left out.
	/// </summary>
	public IReadOnlyList<(string State, string Value)> RenderStates( IReadOnlyDictionary<string, int> values )
	{
		var all = ProgramValues( StageKind.Pixel );
		foreach ( var (name, value) in values ) all[name] = value;

		var states = new List<(string, string)>();
		foreach ( var line in PassPreprocessor.ActiveLines( Path, Text, all, Include ) )
		{
			var m = RenderStateLine().Match( line );
			if ( !m.Success ) continue;
			if ( PassPreprocessor.Evaluate( m.Groups["value"].Value, all ) is { } value ) states.Add( (m.Groups["state"].Value, value) );
		}

		return states;
	}

	/// <summary>What every stage's preprocessing starts with: <c>PROGRAM</c> and vfx's program numbers.</summary>
	internal static Dictionary<string, int> ProgramValues( StageKind stage ) => new( StringComparer.Ordinal )
	{
		["PROGRAM"] = stage switch { StageKind.Vertex => 1, StageKind.Compute => 6, _ => 2 },
		["VFX_PROGRAM_VS"] = 1,
		["VFX_PROGRAM_PS"] = 2,
		["VFX_PROGRAM_GS"] = 3,
		["VFX_PROGRAM_HS"] = 4,
		["VFX_PROGRAM_DS"] = 5,
		["VFX_PROGRAM_CS"] = 6,
		["VFX_PROGRAM_RS"] = 7,
		["VFX_PROGRAM_RTX"] = 8,
	};

	static (int, int) Range( string text )
	{
		var parts = text.Split( "..", StringSplitOptions.TrimEntries );
		if ( parts.Length == 2 && int.TryParse( parts[0], CultureInfo.InvariantCulture, out var min ) && int.TryParse( parts[1], CultureInfo.InvariantCulture, out var max ) ) return (min, max);
		return (0, 1);
	}

	[GeneratedRegex( @"^\s*(?<kind>Dynamic|Static)Combo(?:FromFeature)?\(\s*(?<name>\w+)\s*,\s*(?<range>[^,)]+?)\s*(?:,\s*(?<feature>F_\w+))?\s*[,)]" )]
	private static partial Regex ComboLine();

	[GeneratedRegex( @"^\s*DynamicComboRule\(\s*(?<kind>Requires1|Allow1)\(\s*(?<args>.*?)\s*\)\s*\)\s*;" )]
	private static partial Regex RuleLine();

	[GeneratedRegex( @"^\s*Feature\(\s*(?<name>\w+)\s*,\s*(?<range>\d+\s*\.\.\s*\d+)" )]
	private static partial Regex FeatureLine();

	[GeneratedRegex( @"^\s*RenderState\(\s*(?<state>\w+)\s*,\s*(?<value>.*?)\s*\)\s*;" )]
	private static partial Regex RenderStateLine();

	static Regex EntryLine( StageKind stage ) => stage switch
	{
		StageKind.Vertex => VertexEntry(),
		StageKind.Compute => ComputeEntry(),
		_ => PixelEntry(),
	};

	/// <summary>A definition of the stage's entry point by name: a return type, the name and its parameters, not a call.</summary>
	static Regex EntryDefinition( StageKind stage ) => stage switch
	{
		StageKind.Vertex => VertexDefinition(),
		StageKind.Compute => ComputeDefinition(),
		_ => PixelDefinition(),
	};

	[GeneratedRegex( @"^\s*(?:\[[^\]]*\]\s*)*[A-Za-z_][\w<>:]*\s+MainVs\s*\([^;]*$" )]
	private static partial Regex VertexDefinition();

	[GeneratedRegex( @"^\s*(?:\[[^\]]*\]\s*)*[A-Za-z_][\w<>:]*\s+MainPs\s*\([^;]*$" )]
	private static partial Regex PixelDefinition();

	[GeneratedRegex( @"^\s*(?:\[[^\]]*\]\s*)*[A-Za-z_][\w<>:]*\s+MainCs\s*\([^;]*$" )]
	private static partial Regex ComputeDefinition();

	/// <summary>
	/// The module's own lines a stage's compile sees with these values, includes left out: what its globals' annotations
	/// are read from, so a global declared in two <c>#if</c> branches takes the active one's.
	/// </summary>
	public string ActiveText( StageKind stage, IReadOnlyDictionary<string, int> values )
	{
		var all = ProgramValues( stage );
		foreach ( var (name, value) in values ) all[name] = value;
		return string.Join( '\n', PassPreprocessor.ActiveLines( Path, Text, all ) );
	}

	[GeneratedRegex( @"\[\s*shader\s*\(\s*""vertex""\s*\)\s*\]" )]
	private static partial Regex VertexEntry();

	[GeneratedRegex( @"\[\s*shader\s*\(\s*""pixel""\s*\)\s*\]" )]
	private static partial Regex PixelEntry();

	[GeneratedRegex( @"\[\s*shader\s*\(\s*""compute""\s*\)\s*\]" )]
	private static partial Regex ComputeEntry();
}

/// <summary>A combo or feature: its name, range, whether it's static, and the feature a static combo follows.</summary>
public sealed record PassCombo( string Name, int Min, int Max, bool Static, string Feature );

/// <summary>
/// Just enough of the C preprocessor to tell which lines of a pass module a compile with given defines sees: <c>#if</c>,
/// <c>#ifdef</c>, <c>#ifndef</c>, <c>#elif</c>, <c>#else</c>, <c>#endif</c>, object-like <c>#define</c>s, and the
/// <c>#include</c>s it can find (with their include guards). An identifier nothing defines is 0, as in vfx's preprocessor.
/// </summary>
internal static class PassPreprocessor
{
	/// <summary>An include's path and text, given the including file and the name it includes; null when it isn't found.</summary>
	public delegate (string Path, string Text)? Includer( string from, string name );

	public static List<string> ActiveLines( string path, string text, IReadOnlyDictionary<string, int> values, Includer include = null )
	{
		var defines = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, value) in values ) defines[name] = value.ToString( CultureInfo.InvariantCulture );
		var lines = new List<string>();
		Process( path, text, defines, include, lines, 0 );
		return lines;
	}

	static void Process( string path, string text, Dictionary<string, string> defines, Includer include, List<string> lines, int depth )
	{
		if ( depth > 32 ) return;

		// (this branch is active, some branch of this #if was taken, the enclosing block is active)
		var stack = new Stack<(bool Active, bool Taken, bool Parent)>();
		var active = true;
		var all = text.Replace( "\r\n", "\n" ).Split( '\n' );
		for ( int i = 0; i < all.Length; i++ )
		{
			var line = all[i];
			while ( line.EndsWith( '\\' ) && i + 1 < all.Length ) line = line[..^1] + all[++i];

			var trimmed = line.TrimStart();
			if ( !trimmed.StartsWith( '#' ) )
			{
				if ( active ) lines.Add( line );
				continue;
			}

			var directive = trimmed[1..].TrimStart();
			var word = new string( directive.TakeWhile( char.IsLetter ).ToArray() );
			var rest = StripComment( directive[word.Length..] ).Trim();
			switch ( word )
			{
				case "if":
				case "ifdef":
				case "ifndef":
					{
						var condition = word switch
						{
							"ifdef" => defines.ContainsKey( rest ),
							"ifndef" => !defines.ContainsKey( rest ),
							_ => Truth( rest, defines ),
						};
						stack.Push( (active && condition, condition, active) );
						active = active && condition;
						break;
					}
				case "elif":
					{
						if ( stack.Count == 0 ) break;
						var top = stack.Pop();
						var condition = !top.Taken && Truth( rest, defines );
						stack.Push( (top.Parent && condition, top.Taken || condition, top.Parent) );
						active = top.Parent && condition;
						break;
					}
				case "else":
					{
						if ( stack.Count == 0 ) break;
						var top = stack.Pop();
						stack.Push( (top.Parent && !top.Taken, true, top.Parent) );
						active = top.Parent && !top.Taken;
						break;
					}
				case "endif":
					{
						if ( stack.Count == 0 ) break;
						active = stack.Pop().Parent;
						break;
					}
				case "define" when active:
					{
						var m = Regex.Match( rest, @"^(\w+)(\s+(.*))?$" );
						if ( m.Success ) defines[m.Groups[1].Value] = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "1";
						else if ( Regex.Match( rest, @"^(\w+)\(" ) is { Success: true } function ) defines[function.Groups[1].Value] = "1";
						break;
					}
				case "undef" when active:
					defines.Remove( rest );
					break;
				case "include" when active && include is not null:
					{
						var m = Regex.Match( rest, @"^[""<]([^"">]+)["">]" );
						if ( m.Success && include( path, m.Groups[1].Value ) is { } found ) Process( found.Path, found.Text, defines, include, lines, depth + 1 );
						break;
					}
			}
		}
	}

	static string StripComment( string text )
	{
		var comment = text.IndexOf( "//", StringComparison.Ordinal );
		if ( comment >= 0 ) text = text[..comment];
		var block = text.IndexOf( "/*", StringComparison.Ordinal );
		return block >= 0 ? text[..block] : text;
	}

	static bool Truth( string expression, Dictionary<string, string> defines )
	{
		var value = new Parser( expression, defines, symbols: false ).Run();
		return value.Number is double d && d != 0;
	}

	/// <summary>
	/// A render state's value with combo and feature values: a number, or the name of the constant it picks
	/// (<c>F_X ? NONE : BACK</c>); null when it doesn't evaluate.
	/// </summary>
	public static string Evaluate( string expression, IReadOnlyDictionary<string, int> values )
	{
		var defines = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, value) in values ) defines[name] = value.ToString( CultureInfo.InvariantCulture );

		try
		{
			var result = new Parser( expression, defines, symbols: true ).Run();
			return result.Symbol ?? result.Number?.ToString( "R", CultureInfo.InvariantCulture );
		}
		catch ( FormatException )
		{
			return null;
		}
	}

	/// <summary>A value: a number, or (render-state values) a constant's name.</summary>
	readonly record struct Value( double? Number, string Symbol )
	{
		public bool IsTrue => Number is double d ? d != 0 : Symbol is not null;
		public static Value Of( double d ) => new( d, null );
	}

	/// <summary>Precedence climbing over C's operators. Unknown identifiers are 0, or names when <c>symbols</c>.</summary>
	sealed class Parser( string text, Dictionary<string, string> defines, bool symbols )
	{
		readonly List<string> tokens = Tokenize( text );
		int position;
		int depth;

		public Value Run()
		{
			var value = Ternary();
			if ( position != tokens.Count ) throw new FormatException( $"unexpected {tokens[position]} in {text}" );
			return value;
		}

		string Peek => position < tokens.Count ? tokens[position] : null;

		string Next() => position < tokens.Count ? tokens[position++] : throw new FormatException( $"{text} ends early" );

		static readonly string[][] Levels =
		[
			["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>"], ["+", "-"], ["*", "/", "%"],
		];

		Value Ternary()
		{
			var condition = Binary( 0 );
			if ( Peek != "?" ) return condition;
			Next();
			var a = Ternary();
			if ( Next() != ":" ) throw new FormatException( $"no : in {text}" );
			var b = Ternary();
			return condition.IsTrue ? a : b;
		}

		Value Binary( int level )
		{
			if ( level == Levels.Length ) return Unary();
			var left = Binary( level + 1 );
			while ( Peek is { } op && Array.IndexOf( Levels[level], op ) >= 0 )
			{
				Next();
				var right = Binary( level + 1 );
				left = Apply( op, left, right );
			}

			return left;
		}

		static Value Apply( string op, Value a, Value b )
		{
			if ( op == "&&" ) return Value.Of( a.IsTrue && b.IsTrue ? 1 : 0 );
			if ( op == "||" ) return Value.Of( a.IsTrue || b.IsTrue ? 1 : 0 );
			if ( op is "==" or "!=" && (a.Symbol is not null || b.Symbol is not null) ) return Value.Of( (a.Symbol == b.Symbol && a.Number == b.Number) == (op == "==") ? 1 : 0 );
			var x = a.Number ?? 0;
			var y = b.Number ?? 0;
			return Value.Of( op switch
			{
				"|" => (long)x | (long)y,
				"^" => (long)x ^ (long)y,
				"&" => (long)x & (long)y,
				"==" => x == y ? 1 : 0,
				"!=" => x != y ? 1 : 0,
				"<" => x < y ? 1 : 0,
				">" => x > y ? 1 : 0,
				"<=" => x <= y ? 1 : 0,
				">=" => x >= y ? 1 : 0,
				"<<" => (long)x << (int)y,
				">>" => (long)x >> (int)y,
				"+" => x + y,
				"-" => x - y,
				"*" => x * y,
				"/" => y == 0 ? 0 : x / y,
				"%" => y == 0 ? 0 : x % y,
				_ => throw new FormatException( op ),
			} );
		}

		Value Unary()
		{
			var token = Next();
			switch ( token )
			{
				case "!": return Value.Of( Unary().IsTrue ? 0 : 1 );
				case "-": return Value.Of( -(Unary().Number ?? 0) );
				case "+": return Unary();
				case "~": return Value.Of( ~(long)(Unary().Number ?? 0) );
				case "(":
					{
						var inner = Ternary();
						if ( Next() != ")" ) throw new FormatException( $"unbalanced ( in {text}" );
						return inner;
					}
				case "defined":
					{
						var parenthesized = Peek == "(";
						if ( parenthesized ) Next();
						var name = Next();
						if ( parenthesized && Next() != ")" ) throw new FormatException( $"unbalanced defined( in {text}" );
						return Value.Of( defines.ContainsKey( name ) ? 1 : 0 );
					}
			}

			if ( char.IsDigit( token[0] ) || token[0] == '.' ) return Value.Of( Number( token ) );
			if ( token == "true" ) return Value.Of( 1 );
			if ( token == "false" ) return Value.Of( 0 );

			if ( defines.TryGetValue( token, out var definition ) )
			{
				if ( ++depth > 32 ) throw new FormatException( $"{token} expands without end" );
				var expanded = new Parser( definition.Length == 0 ? "1" : definition, defines, symbols ).Run();
				depth--;
				return expanded;
			}

			// A combo or feature that isn't set is 0; any other name in a render state's value is one of its constants
			var combo = token.Length > 2 && token[1] == '_' && token[0] is 'D' or 'S' or 'F';
			return symbols && !combo ? new Value( null, token ) : Value.Of( 0 );
		}

		static double Number( string token )
		{
			var t = token.TrimEnd( 'u', 'U', 'l', 'L', 'f', 'F' );
			if ( t.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) ) return long.Parse( t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture );
			return double.Parse( t, NumberStyles.Float, CultureInfo.InvariantCulture );
		}

		static List<string> Tokenize( string text )
		{
			var tokens = new List<string>();
			int i = 0;
			while ( i < text.Length )
			{
				var c = text[i];
				if ( char.IsWhiteSpace( c ) ) { i++; continue; }
				if ( char.IsLetter( c ) || c == '_' )
				{
					var start = i;
					while ( i < text.Length && (char.IsLetterOrDigit( text[i] ) || text[i] == '_') ) i++;
					tokens.Add( text[start..i] );
					continue;
				}

				if ( char.IsDigit( c ) || (c == '.' && i + 1 < text.Length && char.IsDigit( text[i + 1] )) )
				{
					var start = i;
					while ( i < text.Length && (char.IsLetterOrDigit( text[i] ) || text[i] == '.') ) i++;
					tokens.Add( text[start..i] );
					continue;
				}

				var two = i + 1 < text.Length ? text.Substring( i, 2 ) : null;
				if ( two is "&&" or "||" or "==" or "!=" or "<=" or ">=" or "<<" or ">>" )
				{
					tokens.Add( two );
					i += 2;
					continue;
				}

				tokens.Add( c.ToString() );
				i++;
			}

			return tokens;
		}
	}
}
