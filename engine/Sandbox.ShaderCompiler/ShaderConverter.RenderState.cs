using System.Globalization;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A <c>.shader</c>'s <c>RenderState( ... )</c> lines as a surface's render state: what's the same for every material
/// becomes the struct's attributes (<c>[Blend( "Alpha" )]</c>), what a feature changes (an expression of an <c>F_</c>
/// feature, or lines under <c>#if</c>s of its static combo) becomes a <c>[RenderState]</c> rule on that feature's field.
/// The shader's state is evaluated for every value of those features, as vfx compiles a combo per value, and
/// vfx's defaults fill what a line doesn't set.
/// </summary>
public static partial class ShaderConverter
{
	/// <summary>The render state of a shader's text: the struct's attributes and each feature's rule, in feature order.</summary>
	public sealed record RenderStates( string Attributes, IReadOnlyList<(string Feature, string Rule)> Rules, IReadOnlyList<string> Notes );

	[GeneratedRegex( @"^\s*RenderState\(\s*(\w+)\s*,\s*(.+?)\s*\)\s*;", RegexOptions.Multiline )]
	private static partial Regex RenderStateLine();

	/// <summary>
	/// Translate the <c>RenderState</c> lines of <paramref name="text"/> (a shader's COMMON and PS, with any includes that
	/// set state); <paramref name="combos"/> maps static combos to their features, <paramref name="features"/> gives each
	/// feature's range.
	/// </summary>
	public static RenderStates TranslateRenderState( string text, IReadOnlyDictionary<string, string> combos, IReadOnlyDictionary<string, (int Min, int Max)> features )
	{
		var notes = new List<string>();
		if ( !RenderStateLine().IsMatch( text ) ) return new RenderStates( "", [], notes );

		// The features the state depends on: features in the lines' expressions and combos in the #ifs around them
		var used = new List<string>();
		foreach ( Match m in Regex.Matches( text, @"\b(F_\w+|S_\w+)\b" ) )
		{
			var name = m.Value;
			var feature = name.StartsWith( "F_", StringComparison.Ordinal ) ? name : combos.TryGetValue( name, out var f ) ? f : null;
			if ( feature is not null && !used.Contains( feature ) && Depends( text, name ) ) used.Add( feature );
		}

		used = used.Where( features.ContainsKey ).OrderBy( f => f == "F_ADDITIVE_BLEND" ? 1 : 0 ).ToList();
		var assignments = Assignments( used, features );
		if ( assignments.Count > 256 )
		{
			notes.Add( "render state depends on too many feature values to translate" );
			return new RenderStates( "", [], notes );
		}

		// Each assignment's state
		var states = assignments.Select( a => Evaluate( text, a, combos, notes ) ).ToList();
		var defaults = states[0];
		var attributes = new List<string>();
		if ( defaults.Blend != SurfaceBlend.Opaque ) attributes.Add( $"Blend( \"{defaults.Blend}\" )" );
		if ( defaults.DepthWrite != (defaults.Blend == SurfaceBlend.Opaque) ) attributes.Add( defaults.DepthWrite ? "DepthWrite" : "NoDepthWrite" );
		if ( !defaults.DepthTest ) attributes.Add( "NoDepthTest" );
		if ( defaults.DepthBias != 0 || defaults.SlopeDepthBias != 0 ) attributes.Add( string.Create( CultureInfo.InvariantCulture, $"DepthBias( {defaults.DepthBias}, {defaults.SlopeDepthBias} )" ) );
		if ( defaults.TwoSided ) attributes.Add( "TwoSided" );
		if ( defaults.AlphaTest ) attributes.Add( "AlphaTest" );

		// Each feature's rule: the states it changes, set alone
		var rules = new List<(string Feature, string Rule)>();
		var layoutFields = new List<SurfaceStateField>();
		for ( int u = 0; u < used.Count; u++ )
		{
			var feature = used[u];
			var (min, max) = features[feature];
			var byValue = new List<SurfaceRenderState>();
			for ( int v = 0; v <= max; v++ )
			{
				var index = assignments.FindIndex( a => a.All( kv => kv.Value == (kv.Key == feature ? Math.Max( v, min ) : 0) ) );
				byValue.Add( states[index] );
			}

			var parts = new List<string>();
			void Rule( SurfaceStateKind kind, Func<SurfaceRenderState, string> name, Func<SurfaceRenderState, int> value )
			{
				if ( byValue.All( s => value( s ) == value( defaults ) ) ) return;
				var single = max - min <= 1 && min == 0;
				parts.Add( single ? $"{kind}: {name( byValue[1] )}" : $"{kind}: {string.Join( ", ", byValue.Select( name ) )}" );
				var values = single ? [value( byValue[1] )] : byValue.Select( value ).ToArray();
				layoutFields.Add( new SurfaceStateField( new SurfaceStateRule( kind, values ), u * 4, false ) );
			}

			static string Flag( bool b ) => b ? "true" : "false";
			Rule( SurfaceStateKind.Blend, s => s.Blend.ToString(), s => (int)s.Blend );
			Rule( SurfaceStateKind.AlphaTest, s => Flag( s.AlphaTest ), s => s.AlphaTest ? 1 : 0 );
			Rule( SurfaceStateKind.TwoSided, s => Flag( s.TwoSided ), s => s.TwoSided ? 1 : 0 );
			Rule( SurfaceStateKind.DepthTest, s => Flag( s.DepthTest ), s => s.DepthTest ? 1 : 0 );

			// Depth write only where it changes and isn't what the blend gives
			if ( byValue.Any( s => s.DepthWrite != defaults.DepthWrite && s.DepthWrite != (s.Blend == SurfaceBlend.Opaque) ) )
				Rule( SurfaceStateKind.DepthWrite, s => Flag( s.DepthWrite ), s => s.DepthWrite ? 1 : 0 );

			if ( parts.Count > 0 ) rules.Add( (feature, string.Join( "; ", parts )) );
		}

		// Features that change state together in a way their rules applied in order don't give
		var layout = new SurfaceStateLayout( Defaults( defaults ), layoutFields );
		for ( int i = 0; i < assignments.Count; i++ )
		{
			var record = new byte[Math.Max( used.Count, 1 ) * 4];
			for ( int u = 0; u < used.Count; u++ ) BitConverter.TryWriteBytes( record.AsSpan( u * 4 ), assignments[i][used[u]] );
			var resolved = layout.Resolve( record );
			if ( resolved != states[i] ) notes.Add( $"render state with {string.Join( ", ", assignments[i].Select( kv => $"{kv.Key} {kv.Value}" ) )} is {states[i]}, not {resolved} as its features' rules give" );
		}

		return new RenderStates( attributes.Count > 0 ? string.Join( ", ", attributes ) : "", rules, notes );
	}

	/// <summary>
	/// The render state of a converted shader in its surface: each rule added to its feature's field (a new bool field for a
	/// feature without one), and the struct's attributes for the <c>[Material]</c> line (empty for none).
	/// </summary>
	static string ApplyRenderStates( System.Text.StringBuilder fields, RenderStates states, List<string> notes )
	{
		notes.AddRange( states.Notes );
		var text = fields.ToString();
		foreach ( var (feature, rule) in states.Rules )
		{
			var source = $"Source( \"{feature}\" )";
			var at = text.IndexOf( source, StringComparison.Ordinal );
			if ( at >= 0 ) text = text.Insert( at + source.Length, $", RenderState( \"{rule}\" )" );
			else text += $"\t[Group( \"Rendering\" ), {source}, RenderState( \"{rule}\" )] bool Use{Pascal( feature[2..] )};\n";
		}

		fields.Clear().Append( text );
		return states.Attributes.Length > 0 ? ", " + states.Attributes : "";
	}

	/// <summary>Every feature a shader's text names, with its declared range or 0..1.</summary>
	static Dictionary<string, (int Min, int Max)> FeatureRanges( string shader, string text )
	{
		var features = FeatureDecl().Matches( Block( shader, "FEATURES" ) ?? "" ).ToDictionary( m => m.Groups[1].Value, m => (int.Parse( m.Groups[2].Value, CultureInfo.InvariantCulture ), int.Parse( m.Groups[3].Value, CultureInfo.InvariantCulture )) );
		foreach ( Match m in Regex.Matches( text, @"\bF_\w+" ) ) features.TryAdd( m.Value, (0, 1) );
		return features;
	}

	/// <summary>
	/// The standard processing's render state (vr_shared_standard_ps_code.fxc, vr_common_ps_code.fxc's culling), for
	/// shaders of the final combiner shape, which include it.
	/// </summary>
	const string StandardRenderState = """
		#if !defined( DEPTH_STATE_ALREADY_SET )
			#if ( S_TRANSLUCENT )
				RenderState( DepthWriteEnable, false );
			#endif
		#endif
		#if !defined( BLEND_MODE_ALREADY_SET )
			#if ( S_TRANSLUCENT )
				RenderState( BlendEnable, true );
				RenderState( SrcBlend, SRC_ALPHA );
				#if ( S_ADDITIVE_BLEND )
					RenderState( DstBlend, ONE );
				#else
					RenderState( DstBlend, INV_SRC_ALPHA );
				#endif
			#elif ( S_ALPHA_TEST )
				RenderState( AlphaToCoverageEnable, true );
			#endif
		#endif
		#if !defined( CULL_MODE_ALREADY_SET )
			#if defined( MATERIAL_CONTROLLED_BACKFACES )
				RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );
			#elif ( S_RENDER_BACKFACES )
				RenderState( CullMode, NONE );
			#endif
		#endif
		""";

	static SurfaceRenderDefaults Defaults( SurfaceRenderState s ) => new( s.Blend, s.DepthWrite == (s.Blend == SurfaceBlend.Opaque) ? null : s.DepthWrite, s.DepthTest, s.DepthBias, s.SlopeDepthBias, s.TwoSided, s.AlphaTest );

	/// <summary>Whether a feature or combo name sits in a RenderState line or an #if around one.</summary>
	static bool Depends( string text, string name )
	{
		var depth = 0;
		var conditions = new List<string>();
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = raw.Trim();
			if ( Regex.IsMatch( line, @"^#\s*if" ) ) { conditions.Add( line ); depth++; }
			else if ( Regex.IsMatch( line, @"^#\s*(elif|else)" ) && conditions.Count > 0 ) conditions[^1] += " " + line;
			else if ( Regex.IsMatch( line, @"^#\s*endif" ) && conditions.Count > 0 ) { conditions.RemoveAt( conditions.Count - 1 ); depth--; }
			else if ( RenderStateLine().IsMatch( line ) )
			{
				if ( Regex.IsMatch( line, $@"\b{name}\b" ) ) return true;
				if ( conditions.Any( c => Regex.IsMatch( c, $@"\b{name}\b" ) ) ) return true;
			}
		}

		return false;
	}

	static List<Dictionary<string, int>> Assignments( List<string> used, IReadOnlyDictionary<string, (int Min, int Max)> features )
	{
		var all = new List<Dictionary<string, int>> { new() };
		foreach ( var feature in used )
		{
			var (min, max) = features[feature];
			all = all.SelectMany( a => Enumerable.Range( min, Math.Max( max - min + 1, 1 ) ).Select( v => new Dictionary<string, int>( a ) { [feature] = v } ) ).ToList();
		}

		// The all-off assignment first: it's the defaults
		return all.OrderBy( a => a.Values.Sum() ).ToList();
	}

	/// <summary>The state the lines set for one assignment of feature values, with vfx's defaults for the rest.</summary>
	static SurfaceRenderState Evaluate( string text, Dictionary<string, int> assignment, IReadOnlyDictionary<string, string> combos, List<string> notes )
	{
		// Defines keep their text and are evaluated where they're read (#define S_ALPHA_TEST ( S_BLEND_MODE == 2 ))
		var defines = new Dictionary<string, string>( StringComparer.Ordinal );
		var depth = 0;
		int Value( string name )
		{
			if ( defines.TryGetValue( name, out var text ) )
			{
				if ( depth > 16 ) return 0;
				depth++;
				var value = Condition( text, Value, defines.ContainsKey );
				depth--;
				return value;
			}

			if ( name.StartsWith( "F_", StringComparison.Ordinal ) ) return assignment.TryGetValue( name, out var v ) ? v : 0;
			return combos.TryGetValue( name, out var feature ) && assignment.TryGetValue( feature, out var c ) ? c : 0;
		}

		var set = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var active = new Stack<(bool Parent, bool Taken, bool Now)>();
		var on = true;
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = Regex.Replace( raw, @"//.*$", "" ).Trim();
			Match m;
			if ( (m = Regex.Match( line, @"^#\s*if(n?def)?\b\s*(.*)$" )).Success )
			{
				var condition = m.Groups[1].Value switch
				{
					"def" => defines.ContainsKey( m.Groups[2].Value.Trim() ),
					"ndef" => !defines.ContainsKey( m.Groups[2].Value.Trim() ),
					_ => Condition( m.Groups[2].Value, Value, defines.ContainsKey ) != 0,
				};
				active.Push( (on, on && condition, on && condition) );
				on = on && condition;
			}
			else if ( (m = Regex.Match( line, @"^#\s*elif\b\s*(.*)$" )).Success && active.Count > 0 )
			{
				var (parent, taken, _) = active.Pop();
				var now = parent && !taken && Condition( m.Groups[1].Value, Value, defines.ContainsKey ) != 0;
				active.Push( (parent, taken || now, now) );
				on = now;
			}
			else if ( Regex.IsMatch( line, @"^#\s*else\b" ) && active.Count > 0 )
			{
				var (parent, taken, _) = active.Pop();
				active.Push( (parent, true, parent && !taken) );
				on = parent && !taken;
			}
			else if ( Regex.IsMatch( line, @"^#\s*endif\b" ) && active.Count > 0 )
			{
				on = active.Pop().Parent;
			}
			else if ( !on )
			{
				continue;
			}
			else if ( (m = Regex.Match( line, @"^#\s*define\s+(\w+)(?:\s+(.+))?$" )).Success )
			{
				defines[m.Groups[1].Value] = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : "1";
			}
			else if ( (m = RenderStateLine().Match( line )).Success )
			{
				var value = m.Groups[2].Value.Trim();
				var ternary = Regex.Match( value, @"^(.+?)\?(.+?):(.+)$" );
				if ( ternary.Success ) value = (Condition( ternary.Groups[1].Value, Value, defines.ContainsKey ) != 0 ? ternary.Groups[2].Value : ternary.Groups[3].Value).Trim();
				set[m.Groups[1].Value] = value.ToUpperInvariant();
			}
		}

		return State( set, notes );
	}

	/// <summary>vfx's render-state values as the vocabulary: blending by its factors, depth, culling, bias.</summary>
	static SurfaceRenderState State( Dictionary<string, string> set, List<string> notes )
	{
		string Get( string name, string otherwise ) => set.TryGetValue( name, out var v ) ? v : otherwise;
		static bool True( string v ) => v is "TRUE" or "1";

		var blend = SurfaceBlend.Opaque;
		if ( True( Get( "BlendEnable", "FALSE" ) ) )
		{
			var source = Get( "SrcBlend", "SRC_ALPHA" );
			var destination = Get( "DstBlend", "INV_SRC_ALPHA" );
			SurfaceBlend? known = $"{source} {destination}" switch
			{
				"SRC_ALPHA INV_SRC_ALPHA" => SurfaceBlend.Alpha,
				"ONE INV_SRC_ALPHA" => SurfaceBlend.Premultiplied,
				"SRC_ALPHA ONE" => SurfaceBlend.Additive,
				"ZERO SRC_COLOR" or "DEST_COLOR ZERO" => SurfaceBlend.Multiply,
				"DEST_COLOR SRC_COLOR" => SurfaceBlend.Mod2x,
				"DEST_COLOR ONE" => SurfaceBlend.ModulateAdd,
				_ => null,
			};

			if ( known is null ) notes.Add( $"blending {source}, {destination} isn't a blend mode: Alpha" );
			blend = known ?? SurfaceBlend.Alpha;
		}

		if ( Get( "BlendOp", "ADD" ) != "ADD" ) notes.Add( $"blend operation {Get( "BlendOp", "ADD" )} isn't converted" );
		if ( Get( "DepthFunc", "GREATER_EQUAL" ) is not ("GREATER_EQUAL" or "GREATEREQUAL") ) notes.Add( $"depth function {Get( "DepthFunc", "" )} isn't converted" );
		if ( Get( "FillMode", "SOLID" ) != "SOLID" ) notes.Add( $"fill mode {Get( "FillMode", "" )} isn't converted" );
		var cull = Get( "CullMode", "DEFAULT" );
		if ( cull == "FRONT" ) notes.Add( "front-face culling isn't converted" );

		// Alpha to coverage is what an alpha-tested shader turns on
		return new SurfaceRenderState(
			blend,
			True( Get( "DepthWriteEnable", "TRUE" ) ),
			True( Get( "DepthEnable", "TRUE" ) ),
			float.TryParse( Get( "DepthBias", "0" ), NumberStyles.Float, CultureInfo.InvariantCulture, out var bias ) ? bias : 0,
			float.TryParse( Get( "SlopeScaleDepthBias", "0" ), NumberStyles.Float, CultureInfo.InvariantCulture, out var slope ) ? slope : 0,
			cull == "NONE",
			True( Get( "AlphaToCoverageEnable", "FALSE" ) ) );
	}

	/// <summary>A preprocessor condition's value: integers, names, defined(), !, comparisons, &amp;&amp; and ||.</summary>
	static int Condition( string expression, Func<string, int> value, Func<string, bool> defined )
	{
		var tokens = Regex.Matches( expression, @"\d+|\w+|&&|\|\||==|!=|<=|>=|[()!<>]" ).Select( m => m.Value ).ToList();
		var at = 0;
		string Peek() => at < tokens.Count ? tokens[at] : null;
		string Next() => at < tokens.Count ? tokens[at++] : null;

		int Primary()
		{
			var t = Next();
			if ( t is null ) return 0;
			if ( t == "(" )
			{
				var v = Or();
				if ( Peek() == ")" ) Next();
				return v;
			}

			if ( t == "!" ) return Primary() == 0 ? 1 : 0;
			if ( t == "defined" )
			{
				var paren = Peek() == "(";
				if ( paren ) Next();
				var name = Next();
				if ( paren && Peek() == ")" ) Next();
				return name is not null && defined( name ) ? 1 : 0;
			}

			if ( int.TryParse( t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n ) ) return n;
			return t is "true" ? 1 : t is "false" ? 0 : value( t );
		}

		int Compare()
		{
			var a = Primary();
			while ( Peek() is "==" or "!=" or "<" or ">" or "<=" or ">=" )
			{
				var op = Next();
				var b = Primary();
				a = op switch { "==" => a == b, "!=" => a != b, "<" => a < b, ">" => a > b, "<=" => a <= b, _ => a >= b } ? 1 : 0;
			}

			return a;
		}

		int And()
		{
			var a = Compare();
			while ( Peek() == "&&" )
			{
				Next();
				var b = Compare();
				a = a != 0 && b != 0 ? 1 : 0;
			}

			return a;
		}

		int Or()
		{
			var a = And();
			while ( Peek() == "||" )
			{
				Next();
				var b = And();
				a = a != 0 || b != 0 ? 1 : 0;
			}

			return a;
		}

		return Or();
	}
}
