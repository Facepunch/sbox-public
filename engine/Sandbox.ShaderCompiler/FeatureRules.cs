using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A module's feature rules (<c>FeatureRule( Allow1( F_A, F_B ), "..." )</c>, <c>Requires1</c>, <c>ChildOf</c>), read from its
/// <c>MATERIAL_INPUTS</c> block's FEATURES lines, and applied to a material's feature values as vfx applies a shader's
/// (<c>CVfx::ApplyRules</c>): in order, again until nothing changes. An AllowN keeps the first N of its arguments that are set
/// and clears the rest; a RequiresN or ChildOf clears its first argument when fewer than N of the others are set. A rule may
/// name features the block doesn't declare (the features a surface doesn't do): they're read from the material's values
/// like any other, so a material setting one keeps the rule's effect on the rest. The material compiler applies them before
/// it reads a material's inputs, and Borealis before a surface takes a material's values, as native's compiler and
/// material system apply a <c>.shader</c>'s.
/// </summary>
public sealed partial class FeatureRules
{
	/// <summary>
	/// The define a module's block is read with for the rules of one of the shaders a surface stands in for: the shader's
	/// place in the surface's <c>[Source( "Shader: ..." )]</c> list (standard's: 0 complex, 1 simple, 2 generic), 0 by default.
	/// </summary>
	public const string ShaderDefine = "MATERIAL_SHADER";

	/// <summary>One rule: what it does, how many it allows or requires, and its arguments (a feature, and the value it matches, -1 for any but 0).</summary>
	public sealed record Rule( string Kind, int Count, IReadOnlyList<(string Feature, int Value)> Arguments, string Text );

	/// <summary>The rules, in the block's order.</summary>
	public IReadOnlyList<Rule> Rules { get; }

	/// <summary>No rules.</summary>
	public static FeatureRules None { get; } = new( [] );

	FeatureRules( IReadOnlyList<Rule> rules ) => Rules = rules;

	/// <summary>Whether there are none.</summary>
	public bool IsEmpty => Rules.Count == 0;

	/// <summary>Every feature a rule names.</summary>
	public IEnumerable<string> Features => Rules.SelectMany( r => r.Arguments.Select( a => a.Feature ) ).Distinct( StringComparer.OrdinalIgnoreCase );

	/// <summary>The rules in a text of FEATURES lines, in order (lines that aren't rules are skipped).</summary>
	public static FeatureRules Parse( string text )
	{
		var rules = new List<Rule>();
		foreach ( Match m in RuleLine().Matches( text ) )
		{
			var kind = m.Groups["kind"].Value;
			var count = m.Groups["count"].Success ? int.Parse( m.Groups["count"].Value, CultureInfo.InvariantCulture ) : 1;
			var arguments = new List<(string, int)>();
			foreach ( var argument in m.Groups["args"].Value.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
			{
				var equals = argument.IndexOf( "==", StringComparison.Ordinal );
				arguments.Add( equals < 0 ? (argument, -1) : (argument[..equals].Trim(), int.Parse( argument[(equals + 2)..].Trim(), CultureInfo.InvariantCulture )) );
			}

			if ( arguments.Count > 0 ) rules.Add( new Rule( kind, count, arguments, m.Value.Trim() ) );
		}

		return rules.Count == 0 ? None : new FeatureRules( rules );
	}

	static readonly ConditionalWeakTable<PassModule, Dictionary<int, FeatureRules>> read = new();

	/// <summary>
	/// The rules of <paramref name="module"/>'s <c>MATERIAL_INPUTS</c> block, read with <see cref="ShaderDefine"/> set to
	/// <paramref name="shader"/> (which of the shaders it stands in for), once per module. None when it declares none.
	/// </summary>
	public static FeatureRules Read( PassModule module, int shader = 0 )
	{
		var known = read.GetValue( module, _ => new Dictionary<int, FeatureRules>() );
		lock ( known )
		{
			if ( known.TryGetValue( shader, out var rules ) ) return rules;
		}

		var values = new Dictionary<string, int>( StringComparer.Ordinal ) { ["MATERIAL_INPUTS"] = 1, [ShaderDefine] = shader };
		var surface = MaterialInputs.IsSurface( module );
		var text = new System.Text.StringBuilder();
		foreach ( var stage in surface ? [StageKind.Pixel] : new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
		{
			if ( !surface && !module.Has( stage ) ) continue;
			text.Append( MaterialInputs.Code( module.CompiledLines( stage, values ) ) ).Append( '\n' );
		}

		// A rule an include gives every stage counts once
		var parsed = Parse( text.ToString() );
		if ( !surface && !parsed.IsEmpty ) parsed = new FeatureRules( parsed.Rules.DistinctBy( r => r.Text ).ToArray() );
		lock ( known ) known[shader] = parsed;
		return parsed;
	}

	/// <summary>
	/// <paramref name="values"/> through the rules: a copy holding every feature of the values and of the rules (those it
	/// doesn't set at 0). The values themselves when there are no rules.
	/// </summary>
	public IReadOnlyDictionary<string, int> Apply( IReadOnlyDictionary<string, int> values )
	{
		if ( IsEmpty ) return values;
		var result = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, value) in values ) result[name] = value;
		foreach ( var feature in Features ) result.TryAdd( feature, 0 );
		ApplyInPlace( result );
		return result;
	}

	/// <summary>Apply the rules to <paramref name="values"/> in place (a feature missing from them is 0). Whether anything changed.</summary>
	public bool ApplyInPlace( IDictionary<string, int> values )
	{
		var any = false;
		bool Set( (string Feature, int Value) argument ) => values.TryGetValue( argument.Feature, out var v ) && (argument.Value == -1 ? v != 0 : v == argument.Value);
		void Clear( string feature )
		{
			values[feature] = 0;
			any = true;
		}

		for ( int loop = 0; loop < 100; loop++ )
		{
			var changed = false;
			foreach ( var rule in Rules )
			{
				if ( rule.Kind == "Allow" )
				{
					var found = 0;
					foreach ( var argument in rule.Arguments )
					{
						if ( !Set( argument ) ) continue;
						if ( found < rule.Count ) found++;
						else
						{
							Clear( argument.Feature );
							changed = true;
						}
					}
				}
				else if ( Set( rule.Arguments[0] ) && rule.Arguments.Skip( 1 ).Count( Set ) < rule.Count )
				{
					// Requires and ChildOf: the first only with enough of the rest
					Clear( rule.Arguments[0].Feature );
					changed = true;
				}
			}

			if ( !changed ) break;
		}

		return any;
	}

	[GeneratedRegex( @"\bFeatureRule\s*\(\s*(?<kind>Allow|Requires|ChildOf)(?<count>\d+)?\s*\((?<args>[^()]*)\)" )]
	private static partial Regex RuleLine();
}
