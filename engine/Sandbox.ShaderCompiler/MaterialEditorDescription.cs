using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// The material editor's view of a shader only a module describes (no compiled <c>.shader_c</c>), read from the module's
/// <c>MATERIAL_INPUTS</c> block as the editor reads a <c>.shader</c>'s compiled features: its FEATURES lines, and for a
/// feature state the UI variables that state uses (the input textures of the textures it makes, and its parameters) and
/// its literal attributes. Written in the lines <c>CVfx::DescribeUi</c> writes (src/vfx/vfx_ui_description.cpp), which
/// the editor reads back into its variable descriptions. See docs/reference/borealis-standalone-modules.md ("Material editor").
/// </summary>
public static partial class MaterialEditorDescription
{
	/// <summary>
	/// The description of <paramref name="module"/> for these feature values: a <c>features</c> line holding its FEATURES
	/// lines (tab-free, newlines as <c>\n</c>), then a line per UI variable the state uses and per literal attribute.
	/// </summary>
	public static string Describe( PassModule module, IReadOnlyDictionary<string, int> features )
	{
		var inputs = MaterialInputs.Read( module, features );
		var text = new StringBuilder();
		text.Append( "featurelines\t" ).Append( inputs.FeatureLines.Replace( "\r", "" ).Replace( "\n", "\\n" ).Replace( "\t", " " ) ).Append( '\n' );

		// The inputs of each texture the editor shows in this state, once each, then the parameters; written in name order,
		// as vfx lists them (the first of a group the editor meets names the group's sort key)
		var listed = new HashSet<string>( StringComparer.Ordinal );
		var variables = new SortedDictionary<string, StringBuilder>( StringComparer.Ordinal );
		StringBuilder Line( string name ) => variables[name] = new StringBuilder();
		foreach ( var texture in inputs.ShownTextures )
		{
			foreach ( var name in texture.Channels.SelectMany( c => c.Inputs ) )
			{
				if ( !inputs.Inputs.TryGetValue( name, out var input ) || !listed.Add( name ) ) continue;
				Variable( Line( input.Name ), input.Name, input.Type == "CUBE" ? "samplerCUBE" : input.Type == "3D" ? "sampler3D" : "sampler2D", InputTextureRegister, "Texture", input.Group, 0,
					Pad( input.Default ), null, null, input.FileEnding, input.DefaultFile ?? "", input.Srgb ? SrgbColorSpace : LinearColorSpace, input.MinBits, input.Processor );
			}
		}

		foreach ( var parameter in inputs.Parameters )
		{
			if ( !listed.Add( parameter.Name ) ) continue;
			var a = parameter.Annotations;
			var count = parameter.Type.Length > 0 && char.IsDigit( parameter.Type[^1] ) ? parameter.Type[^1] - '0' : 1;
			var isFloat = parameter.Type.StartsWith( "float", StringComparison.Ordinal );
			var isBool = parameter.Type.StartsWith( "bool", StringComparison.Ordinal );
			var defaults = Pad( Numbers( a, "Default", count ) );

			// A range sets its components over 0 and 1; without one a parameter is unbounded
			float[] min = null, max = null;
			if ( Numbers( a, "Range", count * 2 ) is { } range )
			{
				min = [0, 0, 0, 0];
				max = [1, 1, 1, 1];
				for ( int i = 0; i < count && i < 4; i++ )
				{
					min[i] = range[i];
					max[i] = range[count + i];
				}
			}

			var ui = UiType().Match( a ) is { Success: true } u ? UiTypeName( u.Groups["type"].Value ) : DefaultUi( parameter.Type );
			var step = UiStep().Match( a ) is { Success: true } s && float.TryParse( s.Groups["step"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stepValue ) ? stepValue : 0;
			var group = UiGroup().Match( a ) is { Success: true } g ? g.Groups["group"].Value : "";
			var register = isFloat ? Float4Register : isBool ? BoolRegister : Int4Register;
			Variable( Line( parameter.Name ), parameter.Name, parameter.Type, register, ui, group, step, defaults, min, max, "", "", 0, -1, "" );
		}

		foreach ( var line in variables.Values ) text.Append( line );

		// What the editor maps a face's uvs by: the representative texture's first input
		if ( inputs.RepresentativeTexture is { } representative && inputs.Declared.FirstOrDefault( t => t.Name == representative ) is { Channels: [{ Inputs: [var first, ..] }, ..] } )
			text.Append( "representative\t" ).Append( first ).Append( '\n' );

		foreach ( var (name, kind, value) in inputs.Attributes )
		{
			var type = kind switch { "Bool" => "bool", "Float3" => "float3", "Texture" => "sampler2D", _ => "float" };
			// One the features decide is evaluated for this state (vfx keeps it as an expression)
			text.Append( inputs.ExpressionAttributes.Contains( name ) ? "attributeexpression\t" : "attribute\t" ).Append( name ).Append( "\ttype=" ).Append( type ).Append( "\tvalue=" ).Append( Floats( Pad( value ) ) ).Append( '\n' );
		}

		return text.ToString();
	}

	/// <summary>Feature values from <c>"F_A=1 F_B=2"</c>.</summary>
	public static Dictionary<string, int> ParseFeatureValues( string text )
	{
		var values = new Dictionary<string, int>( StringComparer.Ordinal );
		foreach ( var pair in (text ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = pair.Split( '=' );
			if ( parts.Length == 2 && int.TryParse( parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) ) values[parts[0]] = value;
		}

		return values;
	}

	// VfxRegisterType_t and VfxColorSpace_t (src/common/vfx/vfx_shader_params.h)
	const int BoolRegister = 1, Int4Register = 2, Float4Register = 3, InputTextureRegister = 7;
	const int LinearColorSpace = 0, SrgbColorSpace = 1;

	// vfx's bounds for a variable without a range
	static readonly float[] Lowest = [-1e9f, -1e9f, -1e9f, -1e9f];
	static readonly float[] Highest = [1e9f, 1e9f, 1e9f, 1e9f];
	static readonly int[] LowestInt = [-999999999, -999999999, -999999999, -999999999];
	static readonly int[] HighestInt = [999999999, 999999999, 999999999, 999999999];

	/// <summary>A variable as vfx describes it: the int values are the float ones truncated, or vfx's bounds without a range.</summary>
	static void Variable( StringBuilder text, string name, string type, int register, string ui, string group, float step, float[] fdefault, float[] fmin, float[] fmax,
		string ending, string file, int colorSpace, int bits, string processor )
	{
		var idefault = Truncated( fdefault );
		var imin = fmin is null ? LowestInt : Truncated( fmin );
		var imax = fmax is null ? HighestInt : Truncated( fmax );
		fmin ??= Lowest;
		fmax ??= Highest;
		text.Append( "variable\t" ).Append( name )
			.Append( "\ttype=" ).Append( type ).Append( "\tregister=" ).Append( register ).Append( "\tsource=0\tui=" ).Append( ui )
			.Append( "\tgroup=" ).Append( group ).Append( "\tstep=" ).Append( step.ToString( "G9", CultureInfo.InvariantCulture ) )
			.Append( "\tfdefault=" ).Append( Floats( fdefault ) ).Append( "\tfmin=" ).Append( Floats( fmin ) ).Append( "\tfmax=" ).Append( Floats( fmax ) )
			.Append( "\tidefault=" ).Append( Ints( idefault ) ).Append( "\timin=" ).Append( Ints( imin ) ).Append( "\timax=" ).Append( Ints( imax ) )
			.Append( "\tending=" ).Append( ending ).Append( "\tfile=" ).Append( file ).Append( "\tcolorspace=" ).Append( colorSpace )
			.Append( "\tbits=" ).Append( bits ).Append( "\tprocessor=" ).Append( processor ).Append( '\n' );
	}

	static string Floats( float[] values ) => string.Join( ' ', values.Take( 4 ).Select( v => v.ToString( "G9", CultureInfo.InvariantCulture ) ) );
	static string Ints( int[] values ) => string.Join( ' ', values.Take( 4 ).Select( v => v.ToString( CultureInfo.InvariantCulture ) ) );

	static float[] Pad( float[] values )
	{
		var padded = new float[4];
		if ( values is not null ) Array.Copy( values, padded, Math.Min( 4, values.Length ) );
		return padded;
	}

	static int[] Truncated( float[] values ) => [.. values.Select( v => (int)v )];

	/// <summary>An annotation's numbers: <c>Default( 1 )</c>, <c>Default3( 1, 1, 1 )</c>, <c>Range2( 0, 0, 1, 1 )</c>.</summary>
	static float[] Numbers( string annotations, string name, int count )
	{
		var m = Regex.Match( annotations, $@"\b{name}(?<n>[1-4])?\s*\((?<args>[^()]*)\)" );
		if ( !m.Success ) return null;
		var values = m.Groups["args"].Value.Split( ',' ).Select( v => float.TryParse( v.Trim().TrimEnd( 'f' ), NumberStyles.Float, CultureInfo.InvariantCulture, out var f ) ? f : 0 ).ToArray();
		return values.Length >= count ? values[..count] : [.. values, .. new float[count - values.Length]];
	}

	static string UiTypeName( string annotation ) => annotation switch
	{
		"Color" => "Color",
		"Slider" => "Slider",
		"VectorText" => "VectorText",
		"CheckBox" => "CheckBox",
		"Texture" => "Texture",
		_ => annotation,
	};

	/// <summary>A type's default UI, as vfx's variable type table gives it.</summary>
	static string DefaultUi( string type ) => type switch
	{
		"float" or "int" or "uint" => "Slider",
		"bool" => "CheckBox",
		_ => "VectorText",
	};

	[GeneratedRegex( @"\bUiType\s*\(\s*(?<type>\w+)\s*\)" )]
	private static partial Regex UiType();

	[GeneratedRegex( @"\bUiStep\s*\(\s*(?<step>[^)]*)\)" )]
	private static partial Regex UiStep();

	[GeneratedRegex( @"\bUiGroup\s*\(\s*""(?<group>[^""]*)""\s*\)" )]
	private static partial Regex UiGroup();
}
