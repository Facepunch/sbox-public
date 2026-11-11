using Basalt.Source2.Resources;
using System.IO;
using System.Linq;

namespace Basalt.Source2.Models;

/// <summary>
/// A mesh's morph targets (<c>CMorphSetData</c>, a mesh's <c>MRPH</c> block): each morph's rectangles of deltas in a
/// texture atlas, and the flex controllers, flex descs and flex rules that turn controller values into morph weights.
/// <see cref="RunFlexRules"/> evaluates the rules as native's <c>CMorphSet::RunFlexRules</c> does.
/// </summary>
public sealed class MorphSet
{
	/// <summary>The size in texels the morphs' deltas cover when put back together: one texel per vertex.</summary>
	public int Width { get; init; }

	/// <summary>The size in texels the morphs' deltas cover when put back together.</summary>
	public int Height { get; init; }

	/// <summary>Each bundle's kind, in order: position and speed, then normal and wrinkle.</summary>
	public IReadOnlyList<MorphBundleType> BundleTypes { get; init; } = [];

	/// <summary>The morphs, by name (a flex desc's FACS name).</summary>
	public IReadOnlyList<MorphData> Morphs { get; init; } = [];

	/// <summary>The compiled texture holding every morph's rectangles (<c>m_pTextureAtlas</c>), or null.</summary>
	public string TextureAtlas { get; init; }

	/// <summary>Flex descs: what a flex rule computes a weight for, named by the morph it weights.</summary>
	public IReadOnlyList<string> FlexDescs { get; init; } = [];

	/// <summary>Flex controllers: the values a game sets, which the rules read.</summary>
	public IReadOnlyList<FlexController> Controllers { get; init; } = [];

	/// <summary>Flex rules, at most one for each flex desc.</summary>
	public IReadOnlyList<FlexRule> Rules { get; init; } = [];

	MorphData[] morphsByDesc;
	FlexRule[] rulesByDesc;

	/// <summary>The morph a flex desc weights, or null (<c>CMorphSet::FindMorphData</c>).</summary>
	public MorphData MorphFor( int desc )
	{
		Map();
		return (uint)desc < (uint)morphsByDesc.Length ? morphsByDesc[desc] : null;
	}

	/// <summary>Flex rules by the desc they set, and morphs by the desc that names them (<c>CMorphSet::Initialize</c>).</summary>
	void Map()
	{
		if ( morphsByDesc is not null ) return;
		var rules = new FlexRule[FlexDescs.Count];
		foreach ( var rule in Rules )
		{
			if ( (uint)rule.Flex < (uint)rules.Length ) rules[rule.Flex] = rule;
		}

		var byName = new Dictionary<string, MorphData>();
		foreach ( var morph in Morphs ) byName.TryAdd( morph.Name, morph );
		rulesByDesc = rules;
		morphsByDesc = FlexDescs.Select( x => byName.GetValueOrDefault( x ) ).ToArray();
	}

	/// <summary>
	/// The flex descs' weights from the controllers' values, <paramref name="controllers"/> indexed as
	/// <see cref="Controllers"/> is, as native's <c>CMorphSet::RunFlexRules</c> computes them. An op whose operands are
	/// out of range does nothing, as native's checks make it.
	/// </summary>
	public void RunFlexRules( ReadOnlySpan<float> controllers, Span<float> descs )
	{
		Map();
		descs[..FlexDescs.Count].Clear();
		Span<float> stack = stackalloc float[StackSize];
		var count = Math.Min( Controllers.Count, controllers.Length );

		for ( int desc = 0; desc < rulesByDesc.Length; desc++ )
		{
			if ( rulesByDesc[desc] is not { } rule ) continue;

			stack.Clear();
			var k = 0;
			foreach ( var op in rule.Ops )
			{
				switch ( op.Code )
				{
					case FlexOpCode.Add when k >= 2: stack[k - 2] += stack[k - 1]; k--; break;
					case FlexOpCode.Sub when k >= 2: stack[k - 2] -= stack[k - 1]; k--; break;
					case FlexOpCode.Mul when k >= 2: stack[k - 2] *= stack[k - 1]; k--; break;
					case FlexOpCode.Div when k >= 2:
						stack[k - 2] = MathF.Abs( stack[k - 1] ) > 0.0001f ? stack[k - 2] / stack[k - 1] : 0;
						k--;
						break;
					case FlexOpCode.Neg when k >= 1: stack[k - 1] = -stack[k - 1]; break;
					case FlexOpCode.Max when k >= 2: stack[k - 2] = MathF.Max( stack[k - 2], stack[k - 1] ); k--; break;
					case FlexOpCode.Min when k >= 2: stack[k - 2] = MathF.Min( stack[k - 2], stack[k - 1] ); k--; break;
					case FlexOpCode.Sqrt when k >= 1: stack[k - 1] = MathF.Sqrt( stack[k - 1] ); break;
					case FlexOpCode.Sin when k >= 1: stack[k - 1] = MathF.Sin( stack[k - 1] ); break;
					case FlexOpCode.Cos when k >= 1: stack[k - 1] = MathF.Cos( stack[k - 1] ); break;
					case FlexOpCode.Abs when k >= 1: stack[k - 1] = MathF.Abs( stack[k - 1] ); break;
					case FlexOpCode.Const when k < StackSize: stack[k++] = op.Value; break;
					case FlexOpCode.Fetch1 when k < StackSize && (uint)op.Index < (uint)count: stack[k++] = controllers[op.Index]; break;
					case FlexOpCode.Fetch2 when k < StackSize && (uint)op.Index < (uint)FlexDescs.Count: stack[k++] = descs[op.Index]; break;
					case FlexOpCode.Combo when k >= op.Index && op.Index > 0:
						{
							var km = k - op.Index;
							for ( int i = km + 1; i < k; i++ ) stack[km] *= stack[i];
							k = km + 1;
							break;
						}

					case FlexOpCode.Dominate when k >= op.Index + 1:
						{
							var km = k - op.Index;
							var dv = stack[km];
							for ( int i = km + 1; i < k; i++ ) dv *= stack[i];
							stack[km - 1] *= 1.0f - dv;
							k = km;
							break;
						}

					case FlexOpCode.TwoWay0 when k < StackSize && (uint)op.Index < (uint)count: stack[k++] = RemapClamped( controllers[op.Index], -1, 0, 1, 0 ); break;
					case FlexOpCode.TwoWay1 when k < StackSize && (uint)op.Index < (uint)count: stack[k++] = RemapClamped( controllers[op.Index], 0, 1, 0, 1 ); break;
					case FlexOpCode.NWay when k >= 5:
						{
							var valueIndex = (int)stack[k - 1];
							if ( (uint)valueIndex >= (uint)count || (uint)op.Index >= (uint)count ) break;
							var value = controllers[valueIndex];
							var (x, y, z, w) = (stack[k - 5], stack[k - 4], stack[k - 3], stack[k - 2]);
							if ( value <= x || value >= w ) value = 0;
							else if ( value < y ) value = RemapClamped( value, x, y, 0, 1 );
							else if ( value > z ) value = RemapClamped( value, z, w, 1, 0 );
							else value = 1;
							stack[k - 5] = value * controllers[op.Index];
							k -= 4;
							break;
						}

					case FlexOpCode.RemapValClamped when k >= 5:
						stack[k - 5] = RemapClamped( stack[k - 5], stack[k - 4], stack[k - 3], stack[k - 2], stack[k - 1] );
						k -= 4;
						break;

					case FlexOpCode.LowerEyelid or FlexOpCode.UpperEyelid when k >= 3:
						Eyelid( op, controllers, stack, ref k );
						break;
				}
			}

			descs[desc] = stack[0];
		}
	}

	/// <summary>
	/// The eyelid ops (<c>FLEX_OP_DME_LOWER_EYELID</c>, <c>FLEX_OP_DME_UPPER_EYELID</c>): the lid's closing from the
	/// close-lid, blink and eye up-down controllers, whose indices are on the stack.
	/// </summary>
	void Eyelid( FlexOp op, ReadOnlySpan<float> controllers, Span<float> stack, ref int k )
	{
		var count = Math.Min( Controllers.Count, controllers.Length );
		if ( (uint)op.Index >= (uint)count ) return;

		var lower = op.Code == FlexOpCode.LowerEyelid;
		var closeLidV = Controllers[op.Index];
		var closeLidVValue = lower
			? RemapClamped( controllers[op.Index], closeLidV.Min, closeLidV.Max, 1, 0 )
			: RemapClamped( controllers[op.Index], closeLidV.Min, closeLidV.Max, 0, 1 );

		var blinkIndex = (int)stack[k - 2];
		var blink = 0.0f;
		if ( blinkIndex >= 0 )
		{
			if ( blinkIndex >= count ) return;
			blink = RemapClamped( controllers[blinkIndex], Controllers[blinkIndex].Min, Controllers[blinkIndex].Max, 0, 1 );
		}

		var closeLidIndex = (int)stack[k - 1];
		if ( (uint)closeLidIndex >= (uint)count ) return;
		var closeLid = MathF.Max( blink, RemapClamped( controllers[closeLidIndex], Controllers[closeLidIndex].Min, Controllers[closeLidIndex].Max, 0, 1 ) );

		var eyeUpDownIndex = (int)stack[k - 3];
		var eyeUpDown = 0.0f;
		if ( eyeUpDownIndex >= 0 )
		{
			if ( eyeUpDownIndex >= count ) return;
			eyeUpDown = RemapClamped( controllers[eyeUpDownIndex], Controllers[eyeUpDownIndex].Min, Controllers[eyeUpDownIndex].Max, -1, 1 );
			eyeUpDown = lower ? MathF.Max( 0, eyeUpDown ) : MathF.Min( 0, eyeUpDown );
		}

		stack[k - 3] = lower ? (1 - eyeUpDown) * closeLidVValue * closeLid : (1 + eyeUpDown) * closeLidVValue * closeLid;
		k -= 2;
	}

	/// <summary>Native's flex stack depth (<c>STUDIO_FLEX_STACK</c>).</summary>
	const int StackSize = 32;

	static float RemapClamped( float value, float a, float b, float c, float d )
	{
		if ( a == b ) return value >= b ? d : c;
		var t = Math.Clamp( (value - a) / (b - a), 0, 1 );
		return c + (d - c) * t;
	}

	/// <summary>Read a mesh's morph set from its <c>MRPH</c> block (KV3).</summary>
	public static MorphSet Read( ReadOnlySpan<byte> kv3 )
	{
		if ( Kv3.Read( kv3 ) is not Dictionary<string, object> root ) throw new InvalidDataException( "Morph set isn't a KV3 object" );

		return new MorphSet
		{
			Width = (int)Kv.Number( root.Get( "m_nWidth" ) ),
			Height = (int)Kv.Number( root.Get( "m_nHeight" ) ),
			BundleTypes = Items( root.Get( "m_bundleTypes" ) ).Select( x => Enum<MorphBundleType>( x, "MORPH_BUNDLE_TYPE_" ) ).ToArray(),
			Morphs = Kv.Objects( root.Get( "m_morphDatas" ) ).Select( MorphData.Read ).ToArray(),
			TextureAtlas = root.Get( "m_pTextureAtlas" ) as string,
			FlexDescs = Kv.Objects( root.Get( "m_FlexDesc" ) ).Select( x => x.Get( "m_szFacs" ) as string ?? "" ).ToArray(),
			Controllers = Kv.Objects( root.Get( "m_FlexControllers" ) ).Select( x => new FlexController( x.Get( "m_szName" ) as string ?? "", x.Get( "m_szType" ) as string ?? "", (float)Kv.Number( x.Get( "min" ) ), (float)Kv.Number( x.Get( "max" ), 1 ) ) ).ToArray(),
			Rules = Kv.Objects( root.Get( "m_FlexRules" ) ).Select( x => new FlexRule( (int)Kv.Number( x.Get( "m_nFlex" ) ), Kv.Objects( x.Get( "m_FlexOps" ) ).Select( o => new FlexOp( Enum<FlexOpCode>( o.Get( "m_OpCode" ), "FLEX_OP_" ), (int)(long)Kv.Number( o.Get( "m_Data" ) ) ) ).ToArray() ) ).ToArray(),
		};
	}

	/// <summary>An array's items, whatever form the KV3 reader gave it.</summary>
	internal static IEnumerable<object> Items( object value ) => value switch
	{
		object[] items => items,
		System.Collections.IEnumerable items and not string => items.Cast<object>(),
		_ => [],
	};

	/// <summary>An enum stored as its number or its name (<c>FLEX_OP_CONST</c>).</summary>
	static T Enum<T>( object value, string prefix ) where T : struct, System.Enum
	{
		if ( value is string name )
		{
			var trimmed = name.StartsWith( prefix, StringComparison.Ordinal ) ? name[prefix.Length..] : name;
			foreach ( var candidate in System.Enum.GetValues<T>() )
			{
				if ( string.Equals( Native( candidate ), trimmed, StringComparison.OrdinalIgnoreCase ) ) return candidate;
			}

			return default;
		}

		return (T)System.Enum.ToObject( typeof( T ), (int)Kv.Number( value ) );
	}

	/// <summary>The suffix of an enum value's native name.</summary>
	static string Native<T>( T value ) where T : struct, System.Enum => value switch
	{
		FlexOpCode.Fetch1 => "FETCH1",
		FlexOpCode.Fetch2 => "FETCH2",
		FlexOpCode.TwoWay0 => "2WAY_0",
		FlexOpCode.TwoWay1 => "2WAY_1",
		FlexOpCode.NWay => "NWAY",
		FlexOpCode.LowerEyelid => "DME_LOWER_EYELID",
		FlexOpCode.UpperEyelid => "DME_UPPER_EYELID",
		FlexOpCode.RemapValClamped => "REMAPVALCLAMPED",
		MorphBundleType.PositionSpeed => "POSITION_SPEED",
		MorphBundleType.NormalWrinkle => "NORMAL_WRINKLE",
		_ => value.ToString().ToUpperInvariant(),
	};
}

/// <summary>A morph bundle's kind (<c>MorphBundleType_t</c>).</summary>
public enum MorphBundleType
{
#pragma warning disable CS1591
	None = 0,
	PositionSpeed = 1,
	NormalWrinkle = 2,
#pragma warning restore CS1591
}

/// <summary>A flex rule op (<c>FlexOpCode_t</c>).</summary>
public enum FlexOpCode
{
#pragma warning disable CS1591
	None = 0,
	Const = 1,
	Fetch1 = 2,
	Fetch2 = 3,
	Add = 4,
	Sub = 5,
	Mul = 6,
	Div = 7,
	Neg = 8,
	Exp = 9,
	Open = 10,
	Close = 11,
	Comma = 12,
	Max = 13,
	Min = 14,
	TwoWay0 = 15,
	TwoWay1 = 16,
	NWay = 17,
	Combo = 18,
	Dominate = 19,
	LowerEyelid = 20,
	UpperEyelid = 21,
	Sqrt = 22,
	RemapValClamped = 23,
	Sin = 24,
	Cos = 25,
	Abs = 26,
#pragma warning restore CS1591
}

/// <summary>One op of a flex rule: its code and its data, an index or a float's bits.</summary>
public readonly record struct FlexOp( FlexOpCode Code, int Data )
{
	/// <summary>The data as an index.</summary>
	public int Index => Data;

	/// <summary>The data as a float.</summary>
	public float Value => BitConverter.Int32BitsToSingle( Data );
}

/// <summary>A flex rule: the ops that compute flex desc <paramref name="Flex"/>'s weight.</summary>
public sealed record FlexRule( int Flex, FlexOp[] Ops );

/// <summary>A flex controller: its name, kind and range.</summary>
public readonly record struct FlexController( string Name, string Type, float Min, float Max );

/// <summary>A morph (<c>CMorphData</c>): its name and the rectangles of its deltas.</summary>
public sealed record MorphData( string Name, MorphRect[] Rects )
{
	internal static MorphData Read( Dictionary<string, object> data ) => new( data.Get( "m_name" ) as string ?? "", Kv.Objects( data.Get( "m_morphRectDatas" ) ).Select( MorphRect.Read ).ToArray() );
}

/// <summary>
/// A rectangle of a morph's deltas (<c>CMorphRectData</c>): where it goes in the morph set's <see cref="MorphSet.Width"/>
/// by <see cref="MorphSet.Height"/> texels, its size in the atlas's uvs, and where each bundle's copy of it is in the atlas.
/// </summary>
public sealed record MorphRect( int XLeftDst, int YTopDst, float UWidthSrc, float VHeightSrc, MorphBundle[] Bundles )
{
	internal static MorphRect Read( Dictionary<string, object> rect ) => new(
		(int)Kv.Number( rect.Get( "m_nXLeftDst" ) ), (int)Kv.Number( rect.Get( "m_nYTopDst" ) ),
		(float)Kv.Number( rect.Get( "m_flUWidthSrc" ) ), (float)Kv.Number( rect.Get( "m_flVHeightSrc" ) ),
		Kv.Objects( rect.Get( "m_bundleDatas" ) ).Select( MorphBundle.Read ).ToArray() );
}

/// <summary>
/// One bundle of a morph rectangle (<c>CMorphBundleData</c>): its corner in the atlas's uvs, and the offset and range
/// each channel is scaled back to.
/// </summary>
public sealed record MorphBundle( float ULeftSrc, float VTopSrc, float[] Offsets, float[] Ranges )
{
	internal static MorphBundle Read( Dictionary<string, object> bundle ) => new(
		(float)Kv.Number( bundle.Get( "m_flULeftSrc" ) ), (float)Kv.Number( bundle.Get( "m_flVTopSrc" ) ),
		Floats( bundle.Get( "m_offsets" ) ), Floats( bundle.Get( "m_ranges" ) ) );

	static float[] Floats( object value ) => MorphSet.Items( value ).Select( x => (float)Kv.Number( x ) ).ToArray();
}
