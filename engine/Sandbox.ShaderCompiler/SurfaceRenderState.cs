using System.Globalization;

namespace Sandbox.ShaderCompiler;

/// <summary>How a surface's colour meets what's behind it (<c>[Blend( "Alpha" )]</c>).</summary>
public enum SurfaceBlend
{
	/// <summary>Replaces it.</summary>
	Opaque,
	/// <summary>Over it by opacity (source alpha, one minus source alpha).</summary>
	Alpha,
	/// <summary>Over it, the colour already multiplied by opacity (one, one minus source alpha).</summary>
	Premultiplied,
	/// <summary>Added to it by opacity (source alpha, one).</summary>
	Additive,
	/// <summary>It times the colour (zero, source colour).</summary>
	Multiply,
	/// <summary>It times twice the colour (destination colour, source colour).</summary>
	Mod2x,
	/// <summary>It times the colour, plus itself (destination colour, one).</summary>
	ModulateAdd,
}

/// <summary>
/// A surface's render state, resolved for one material: its blending, depth test and write, depth bias, two-sidedness
/// and alpha test. A blended surface draws in the translucent layer. Defaults come from the <c>[Material]</c> struct's
/// attributes (<see cref="MaterialLayout.RenderState"/>), and a material changes them through its
/// <c>[RenderState]</c> fields (<see cref="MaterialField.RenderStates"/>).
/// </summary>
public readonly record struct SurfaceRenderState( SurfaceBlend Blend, bool DepthWrite, bool DepthTest, float DepthBias, float SlopeDepthBias, bool TwoSided, bool AlphaTest )
{
	/// <summary>Opaque, depth tested and written, back faces culled.</summary>
	public static SurfaceRenderState Opaque => new( SurfaceBlend.Opaque, true, true, 0, 0, false, false );

	/// <summary>Whether it blends with what's behind it.</summary>
	public bool Blends => Blend != SurfaceBlend.Opaque;
}

/// <summary>
/// What a <c>[Material]</c> struct's attributes say about its render state: the defaults every material starts from.
/// <see cref="DepthWrite"/> null means "unless blended".
/// </summary>
public sealed record SurfaceRenderDefaults( SurfaceBlend Blend, bool? DepthWrite, bool DepthTest, float DepthBias, float SlopeDepthBias, bool TwoSided, bool AlphaTest )
{
	/// <summary>No attributes: opaque.</summary>
	public static readonly SurfaceRenderDefaults None = new( SurfaceBlend.Opaque, null, true, 0, 0, false, false );

	/// <summary>A stable text for layout fingerprints.</summary>
	public override string ToString() => string.Create( CultureInfo.InvariantCulture, $"{Blend},{DepthWrite},{DepthTest},{DepthBias},{SlopeDepthBias},{TwoSided},{AlphaTest}" );
}

/// <summary>The render states a field can set.</summary>
public enum SurfaceStateKind
{
	/// <summary><see cref="SurfaceBlend"/>.</summary>
	Blend,
	/// <summary>Depth write.</summary>
	DepthWrite,
	/// <summary>Depth test.</summary>
	DepthTest,
	/// <summary>Back faces drawn.</summary>
	TwoSided,
	/// <summary>Alpha test.</summary>
	AlphaTest,
}

/// <summary>
/// One rule of a <c>[RenderState]</c> field: the state it sets and how its value maps to it. <see cref="Values"/> null:
/// the value is the state (a <see cref="SurfaceBlend"/> by number, a flag by non-zero); one value: non-zero sets it; more:
/// the field's value picks one (values past the list leave the state).
/// </summary>
public sealed record SurfaceStateRule( SurfaceStateKind State, int[] Values )
{
	/// <summary>The state this rule gives for a field's value, or null to leave it.</summary>
	public int? Apply( int value )
	{
		if ( Values is null ) return State == SurfaceStateKind.Blend ? value : value != 0 ? 1 : 0;
		if ( Values.Length == 1 ) return value != 0 ? Values[0] : null;
		return value >= 0 && value < Values.Length ? Values[value] : null;
	}

	/// <summary>
	/// Parse a <c>[RenderState]</c> argument: <c>"Blend"</c>, <c>"Blend: Alpha"</c>, <c>"Blend: Opaque, Alpha"</c>, several
	/// separated by <c>;</c>.
	/// </summary>
	public static IReadOnlyList<SurfaceStateRule> Parse( string text )
	{
		var rules = new List<SurfaceStateRule>();
		foreach ( var part in text.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var colon = part.IndexOf( ':' );
			var name = (colon < 0 ? part : part[..colon]).Trim();
			if ( !Enum.TryParse<SurfaceStateKind>( name, out var state ) || !Enum.IsDefined( state ) || int.TryParse( name, out _ ) )
				throw new FormatException( $"\"{name}\" isn't a render state a field sets ({string.Join( ", ", Enum.GetNames<SurfaceStateKind>() )})" );

			int[] values = null;
			if ( colon >= 0 )
			{
				var list = part[(colon + 1)..].Split( ',', StringSplitOptions.TrimEntries );
				values = list.Select( v => Value( state, v ) ).ToArray();
			}

			rules.Add( new SurfaceStateRule( state, values ) );
		}

		return rules;
	}

	static int Value( SurfaceStateKind state, string text )
	{
		if ( state == SurfaceStateKind.Blend ) return (int)ParseBlend( text );
		return text switch
		{
			"true" => 1,
			"false" => 0,
			_ => throw new FormatException( $"{state} takes true or false, not \"{text}\"" ),
		};
	}

	/// <summary>A blend mode by name.</summary>
	public static SurfaceBlend ParseBlend( string text ) => Enum.TryParse<SurfaceBlend>( text.Trim(), out var blend ) && Enum.IsDefined( blend ) && !int.TryParse( text, out _ )
		? blend
		: throw new FormatException( $"\"{text}\" isn't a blend mode ({string.Join( ", ", Enum.GetNames<SurfaceBlend>() )})" );

	/// <summary>A stable text for layout fingerprints.</summary>
	public override string ToString() => Values is null ? State.ToString() : $"{State}:{string.Join( ",", Values )}";
}

/// <summary>A <c>[RenderState]</c> field's rule, where the field is in the record and whether it's a float.</summary>
public sealed record SurfaceStateField( SurfaceStateRule Rule, int Offset, bool Float );

/// <summary>
/// What a material's render state is made of: the struct's defaults and the <c>[RenderState]</c> fields' rules in field
/// order. The generated class and a converted material carry it, so the engine resolves state per material from its
/// record without the compiled shader.
/// </summary>
public sealed record SurfaceStateLayout( SurfaceRenderDefaults Defaults, IReadOnlyList<SurfaceStateField> Fields )
{
	/// <summary>No attributes and no fields: opaque.</summary>
	public static readonly SurfaceStateLayout Opaque = new( SurfaceRenderDefaults.None, [] );

	/// <summary>The state a material with this record draws with.</summary>
	public SurfaceRenderState Resolve( ReadOnlySpan<byte> record )
	{
		var d = Defaults;
		var blend = d.Blend;
		bool? depthWrite = d.DepthWrite;
		var depthTest = d.DepthTest;
		var twoSided = d.TwoSided;
		var alphaTest = d.AlphaTest;
		foreach ( var field in Fields )
		{
			var at = record[field.Offset..];
			var value = field.Float ? (int)System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( at ) : System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( at );
			if ( field.Rule.Apply( value ) is not { } set ) continue;
			switch ( field.Rule.State )
			{
				case SurfaceStateKind.Blend: blend = set is >= 0 and <= (int)SurfaceBlend.ModulateAdd ? (SurfaceBlend)set : SurfaceBlend.Opaque; break;
				case SurfaceStateKind.DepthWrite: depthWrite = set != 0; break;
				case SurfaceStateKind.DepthTest: depthTest = set != 0; break;
				case SurfaceStateKind.TwoSided: twoSided = set != 0; break;
				case SurfaceStateKind.AlphaTest: alphaTest = set != 0; break;
			}
		}

		return new SurfaceRenderState( blend, depthWrite ?? blend == SurfaceBlend.Opaque, depthTest, d.DepthBias, d.SlopeDepthBias, twoSided, alphaTest );
	}

	/// <summary>A stable text for layout fingerprints.</summary>
	public override string ToString() => $"{Defaults}|{string.Join( ";", Fields.Select( f => $"{f.Rule}@{f.Offset}" ) )}";
}
