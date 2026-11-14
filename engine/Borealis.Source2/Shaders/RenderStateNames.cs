using System.Globalization;
using System.Linq;

namespace Borealis.Source2.Shaders;

/// <summary>
/// vfx's render-state names and their values, as a <c>.shader</c>'s <c>RenderState( Name, VALUE )</c> writes them
/// (<c>src/common/vfx/vfx_render_state_lookup_tables.h</c>): the state's <c>VfxRenderState_t</c> and the value as a number.
/// </summary>
public static class RenderStateNames
{
	enum Kind { Bool, Int, Float, Fill, Cull, Comparison, StencilOp, BlendMode, BlendOp, ColorWrite }

	static readonly Dictionary<string, (int Id, Kind Kind)> states = new( StringComparer.Ordinal )
	{
		["FillMode"] = (0, Kind.Fill),
		["CullMode"] = (1, Kind.Cull),
		["DepthBias"] = (2, Kind.Int),
		["SlopeScaleDepthBias"] = (3, Kind.Float),
		["DepthBiasClamp"] = (4, Kind.Float),
		["DepthClipEnable"] = (5, Kind.Bool),
		["MultisampleEnable"] = (6, Kind.Bool),
		["SampleMask"] = (7, Kind.Int),
		["DepthEnable"] = (16, Kind.Bool),
		["DepthWriteEnable"] = (17, Kind.Bool),
		["DepthFunc"] = (18, Kind.Comparison),
		["StencilEnable"] = (21, Kind.Bool),
		["StencilReadMask"] = (22, Kind.Int),
		["StencilWriteMask"] = (23, Kind.Int),
		["StencilFailOp"] = (24, Kind.StencilOp),
		["StencilDepthFailOp"] = (25, Kind.StencilOp),
		["StencilPassOp"] = (26, Kind.StencilOp),
		["StencilFunc"] = (27, Kind.Comparison),
		["BackStencilFailOp"] = (28, Kind.StencilOp),
		["BackStencilDepthFailOp"] = (29, Kind.StencilOp),
		["BackStencilPassOp"] = (30, Kind.StencilOp),
		["BackStencilFunc"] = (31, Kind.Comparison),
		["StencilRef"] = (32, Kind.Int),
		["AlphaToCoverageEnable"] = (37, Kind.Bool),
		["BlendEnable"] = (41, Kind.Bool),
		["SrcBlend"] = (43, Kind.BlendMode),
		["DstBlend"] = (44, Kind.BlendMode),
		["BlendOp"] = (45, Kind.BlendOp),
		["SrcBlendAlpha"] = (46, Kind.BlendMode),
		["DstBlendAlpha"] = (47, Kind.BlendMode),
		["BlendOpAlpha"] = (48, Kind.BlendOp),
		["ColorWriteEnable0"] = (49, Kind.ColorWrite),
	};

	static readonly string[] Comparisons = ["NEVER", "LESS", "EQUAL", "LESS_EQUAL", "GREATER", "NOT_EQUAL", "GREATER_EQUAL", "ALWAYS"];
	static readonly string[] StencilOps = ["KEEP", "ZERO", "REPLACE", "INCR_SAT", "DECR_SAT", "INVERT", "INCR", "DECR"];
	static readonly string[] BlendOps = ["ADD", "SUBTRACT", "REV_SUBTRACT", "MIN", "MAX"];
	static readonly string[] BlendModes = ["ZERO", "ONE", "SRC_COLOR", "INV_SRC_COLOR", "SRC_ALPHA", "INV_SRC_ALPHA", "DEST_ALPHA", "INV_DEST_ALPHA", "DEST_COLOR", "INV_DEST_COLOR", "SRC_ALPHA_SAT", "BLEND_FACTOR", "SRC1_COLOR", "INV_SRC1_COLOR", "SRC1_ALPHA", "INV_SRC1_ALPHA"];

	/// <summary>
	/// The render-state literal for <c>RenderState( <paramref name="state"/>, <paramref name="value"/> )</c>: its id and a
	/// variable holding the value. False for a state Borealis doesn't take, or a value that isn't one of the state's
	/// (<c>DEFAULT</c> leaves the state as it was).
	/// </summary>
	public static bool TryParse( string state, string value, out int id, out ShaderVariable variable )
	{
		id = -1;
		variable = null;
		if ( !states.TryGetValue( state, out var info ) ) return false;
		id = info.Id;

		float number;
		switch ( info.Kind )
		{
			case Kind.Bool:
				if ( value is "true" or "false" ) number = value == "true" ? 1 : 0;
				else if ( !Number( value, out number ) ) return false;
				number = number != 0 ? 1 : 0;
				break;
			case Kind.Int:
			case Kind.Float:
				if ( !Number( value, out number ) ) return false;
				break;
			case Kind.Fill:
				if ( !Named( value, ["SOLID", "WIREFRAME"], out number ) ) return false;
				break;
			case Kind.Cull:
				if ( !Named( value, ["NONE", "BACK", "FRONT"], out number ) ) return false;
				break;
			case Kind.Comparison:
				if ( !Named( value, Comparisons, out number ) ) return false;
				break;
			case Kind.StencilOp:
				if ( !Named( value, StencilOps, out number ) ) return false;
				break;
			case Kind.BlendOp:
				if ( !Named( value, BlendOps, out number ) ) return false;
				break;
			case Kind.BlendMode:
				if ( !Named( value, BlendModes, out number ) ) return false;
				break;
			case Kind.ColorWrite:
				if ( value == "false" ) number = 0;
				else if ( value.Length > 0 && value.All( c => c is 'R' or 'G' or 'B' or 'A' ) ) number = (value.Contains( 'R' ) ? 1 : 0) | (value.Contains( 'G' ) ? 2 : 0) | (value.Contains( 'B' ) ? 4 : 0) | (value.Contains( 'A' ) ? 8 : 0);
				else if ( !Number( value, out number ) ) return false;
				break;
			default:
				return false;
		}

		variable = new ShaderVariable
		{
			Name = state,
			Attribute = "",
			Source = VariableSource.RenderStateLiteral,
			Register = RegisterType.RenderState,
			RenderStateId = id,
			IntDefault = [(int)number, 0, 0, 0],
			FloatDefault = [number, 0, 0, 0],
		};

		return true;
	}

	static bool Named( string value, string[] names, out float number )
	{
		var index = Array.IndexOf( names, value );
		if ( index >= 0 )
		{
			number = index;
			return true;
		}

		return Number( value, out number );
	}

	static bool Number( string value, out float number ) => float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out number );
}
