using System.Buffers.Binary;
using System.Numerics;

namespace Borealis.Source2.Shaders;

/// <summary>
/// What a compiled vfx expression can read (<c>CVfxExpressionEvalContext</c>): feature values by feature index, the
/// stage's static and dynamic combo values by combo index, and render attributes and material variables by name hash
/// (<c>CUtlStringToken</c>). A lookup with nothing to answer it fails the evaluation, as native's does.
/// </summary>
internal sealed class ShaderExpressionContext
{
	public int[] Features { get; init; } = [];
	public int[] StaticCombos { get; init; }
	public int[] DynamicCombos { get; init; }
	public Func<uint, Vector4?> Attributes { get; init; }
	public Func<uint, Vector4?> Variables { get; init; }
	public float Time { get; init; }
}

/// <summary>
/// Evaluates a compiled vfx expression (<c>CVfxStackMachineBuilder</c>'s program, <c>utlstackmachine.h</c> and
/// <c>vfx_expression_parser.h</c> opcodes) as <c>VfxEval</c> does: a stack of float4s, control flow by byte offset,
/// and the builtin functions in its table order. Texture values aren't supported; an expression that loads one fails.
/// </summary>
internal static class ShaderExpression
{
	enum Op : byte
	{
		Stop, Nop, Jump, Call, ConditionalJump, Return, Function,
		Literal, Store, LoadTemp,
		Or, And, Not, Eq, NotEq, Greater, GreaterEq, Less, LessEq,
		Add, Sub, Mul, Div, Mod, Neg,
		LoadAttribute, LoadFeature, LoadStaticCombo, LoadDynamicCombo, LoadVariable, Swizzle, AttributeExists, LoadVariableIndex, LoadVector4,
	}

	const int StackSize = 32;

	/// <summary>The result, or false when the program is malformed or reads something the context can't answer.</summary>
	public static bool TryEvaluate( ReadOnlySpan<byte> code, ShaderExpressionContext context, out Vector4 result )
	{
		result = default;
		Span<Vector4> stack = stackalloc Vector4[StackSize];
		Span<Vector4> temps = stackalloc Vector4[16];
		Span<int> returns = stackalloc int[StackSize];
		int top = 0, returnTop = 0, pc = 0;

		while ( pc < code.Length )
		{
			var op = (Op)code[pc];
			if ( op == Op.Stop ) break;
			pc++;

			switch ( op )
			{
				case Op.Nop:
					break;

				case Op.Jump:
					if ( !U16( code, pc, out var target ) ) return false;
					pc = target;
					break;

				case Op.Call:
					if ( !U16( code, pc, out var call ) || returnTop >= StackSize ) return false;
					returns[returnTop++] = pc + 2;
					pc = call;
					break;

				case Op.ConditionalJump:
					{
						// Two targets: the first when the popped value's x is non-zero, else the second
						if ( top < 1 ) return false;
						var taken = stack[--top].X != 0;
						if ( !U16( code, taken ? pc : pc + 2, out var branch ) ) return false;
						pc = branch;
						break;
					}

				case Op.Return:
					if ( returnTop < 1 ) return false;
					pc = returns[--returnTop];
					break;

				case Op.Function:
					if ( !U16( code, pc, out var function ) ) return false;
					pc += 2;
					if ( !Call( function, stack, ref top, context ) ) return false;
					break;

				case Op.Literal:
					if ( pc + 4 > code.Length || top >= StackSize ) return false;
					stack[top++] = new Vector4( BinaryPrimitives.ReadSingleLittleEndian( code[pc..] ) );
					pc += 4;
					break;

				case Op.Store:
					if ( pc >= code.Length || top < 1 || code[pc] >= temps.Length ) return false;
					temps[code[pc++]] = stack[--top];
					break;

				case Op.LoadTemp:
					if ( pc >= code.Length || top >= StackSize || code[pc] >= temps.Length ) return false;
					stack[top++] = temps[code[pc++]];
					break;

				case Op.Not:
				case Op.Neg:
					if ( top < 1 ) return false;
					stack[top - 1] = op == Op.Neg ? -stack[top - 1] : Map( stack[top - 1], x => x != 0 ? 0 : 1 );
					break;

				case >= Op.Or and <= Op.Mod:
					{
						if ( top < 2 ) return false;
						var a = stack[top - 2];
						var b = stack[top - 1];
						stack[top - 2] = op switch
						{
							Op.Or => Map( a, b, ( x, y ) => x != 0 || y != 0 ),
							Op.And => Map( a, b, ( x, y ) => x != 0 && y != 0 ),
							Op.Eq => Map( a, b, ( x, y ) => x == y ),
							Op.NotEq => Map( a, b, ( x, y ) => x != y ),
							Op.Greater => Map( a, b, ( x, y ) => x > y ),
							Op.GreaterEq => Map( a, b, ( x, y ) => x >= y ),
							Op.Less => Map( a, b, ( x, y ) => x < y ),
							Op.LessEq => Map( a, b, ( x, y ) => x <= y ),
							Op.Add => a + b,
							Op.Sub => a - b,
							Op.Mul => a * b,
							Op.Div => a / b,
							_ => Map( a, b, ( x, y ) => Frac( x / y ) * y ),
						};
						top--;
						break;
					}

				case Op.LoadAttribute:
				case Op.LoadVariable:
				case Op.AttributeExists:
					{
						if ( pc + 4 > code.Length || top >= StackSize ) return false;
						var hash = BinaryPrimitives.ReadUInt32LittleEndian( code[pc..] );
						pc += 4;
						var value = (op == Op.LoadVariable ? context.Variables : context.Attributes)?.Invoke( hash );
						if ( op == Op.AttributeExists ) value = new Vector4( value is null ? 0 : 1 );
						if ( value is null ) return false;
						stack[top++] = value.Value;
						break;
					}

				case Op.LoadFeature:
				case Op.LoadStaticCombo:
				case Op.LoadDynamicCombo:
					{
						if ( pc >= code.Length || top >= StackSize ) return false;
						var values = op switch
						{
							Op.LoadFeature => context.Features,
							Op.LoadStaticCombo => context.StaticCombos,
							_ => context.DynamicCombos,
						};
						int index = code[pc++];

						// Features a material doesn't set are 0; combo values must be known
						if ( values is null || (op != Op.LoadFeature && index >= values.Length) ) return false;
						stack[top++] = new Vector4( index < values.Length ? values[index] : 0 );
						break;
					}

				case Op.Swizzle:
					{
						if ( pc >= code.Length || top < 1 ) return false;
						int packed = code[pc++];
						var v = stack[top - 1];
						stack[top - 1] = new Vector4( v[packed & 3], v[(packed >> 2) & 3], v[(packed >> 4) & 3], v[(packed >> 6) & 3] );
						break;
					}

				case Op.LoadVector4:
					if ( pc + 16 > code.Length || top >= StackSize ) return false;
					stack[top++] = new Vector4(
						BinaryPrimitives.ReadSingleLittleEndian( code[pc..] ), BinaryPrimitives.ReadSingleLittleEndian( code[(pc + 4)..] ),
						BinaryPrimitives.ReadSingleLittleEndian( code[(pc + 8)..] ), BinaryPrimitives.ReadSingleLittleEndian( code[(pc + 12)..] ) );
					pc += 16;
					break;

				default:
					// Variable loads by index (resolved at material load) and unknown opcodes
					return false;
			}
		}

		if ( top != 1 ) return false;
		result = stack[0];
		return true;
	}

	static bool U16( ReadOnlySpan<byte> code, int at, out int value )
	{
		value = at + 2 <= code.Length ? BinaryPrimitives.ReadUInt16LittleEndian( code[at..] ) : -1;
		return value >= 0;
	}

	static float Frac( float x ) => x - MathF.Floor( x );

	static Vector4 Map( Vector4 v, Func<float, float> f ) => new( f( v.X ), f( v.Y ), f( v.Z ), f( v.W ) );

	static Vector4 Map( Vector4 a, Vector4 b, Func<float, float, bool> f ) =>
		new( f( a.X, b.X ) ? 1 : 0, f( a.Y, b.Y ) ? 1 : 0, f( a.Z, b.Z ) ? 1 : 0, f( a.W, b.W ) ? 1 : 0 );

	static Vector4 Map( Vector4 a, Vector4 b, Func<float, float, float> f ) => new( f( a.X, b.X ), f( a.Y, b.Y ), f( a.Z, b.Z ), f( a.W, b.W ) );

	static Vector4 Map( Vector4 a, Vector4 b, Vector4 c, Func<float, float, float, float> f ) =>
		new( f( a.X, b.X, c.X ), f( a.Y, b.Y, c.Y ), f( a.Z, b.Z, c.Z ), f( a.W, b.W, c.W ) );

	static float SrgbToLinear( float x ) => x <= 0.04045f ? x / 12.92f : MathF.Pow( (x + 0.055f) / 1.055f, 2.4f );

	static float LinearToSrgb( float x ) => x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow( x, 1 / 2.4f ) - 0.055f;

	static float SmoothStep( float min, float max, float x )
	{
		x = Math.Clamp( (x - min) / (max - min), 0, 1 );
		return x * x * (3 - 2 * x);
	}

	/// <summary>
	/// A builtin by its index in <c>vfx_expression_parser.cpp</c>'s <c>s_funcInfo</c>, with its quirks (<c>length</c> leaves w,
	/// the sRGB conversions leave w). <c>random</c> and <c>TextureSize</c> fail: neither has a value to burn in.
	/// </summary>
	static bool Call( int function, Span<Vector4> s, ref int top, ShaderExpressionContext context )
	{
		int args = function switch
		{
			27 => 0,
			7 or 8 or 23 or 25 => 3,
			9 or 10 or 11 or 21 or 22 or 26 or 28 or 29 or 32 or 38 => 2,
			24 => 4,
			_ => 1,
		};

		if ( top < args || top - args + 1 > StackSize || function > 39 ) return false;
		var at = top - args;
		var a = args > 0 ? s[at] : default;
		var b = args > 1 ? s[at + 1] : default;
		var c = args > 2 ? s[at + 2] : default;

		Vector4 r;
		switch ( function )
		{
			case 0: r = Map( a, MathF.Sin ); break;
			case 1: r = Map( a, MathF.Cos ); break;
			case 2: r = Map( a, MathF.Tan ); break;
			case 3: r = Map( a, Frac ); break;
			case 4: r = Map( a, MathF.Floor ); break;
			case 5: r = Map( a, MathF.Ceiling ); break;
			case 6: r = Map( a, x => Math.Clamp( x, 0, 1 ) ); break;
			case 7: r = Map( a, b, c, ( x, lo, hi ) => x < lo ? lo : x > hi ? hi : x ); break;
			case 8: r = Map( a, b, c, ( x, y, t ) => (1 - t) * x + t * y ); break;
			case 9: r = new Vector4( Vector4.Dot( a, b ) ); break;
			case 10: r = new Vector4( a.X * b.X + a.Y * b.Y + a.Z * b.Z ); break;
			case 11: r = new Vector4( a.X * b.X + a.Y * b.Y ); break;
			case 12: r = Map( a, MathF.Log ); break;
			case 13: r = Map( a, MathF.Log2 ); break;
			case 14: r = Map( a, MathF.Log10 ); break;
			case 15: r = Map( a, MathF.Exp ); break;
			case 16: r = Map( a, x => MathF.Pow( 2, x ) ); break;
			case 17: r = Map( a, MathF.Sqrt ); break;
			case 18: r = Map( a, x => 1 / MathF.Sqrt( x ) ); break;
			case 19: r = Map( a, x => x == 0 ? 0 : x < 0 ? -1 : 1 ); break;
			case 20: r = Map( a, MathF.Abs ); break;
			case 21: r = Map( a, b, MathF.Pow ); break;
			case 22: r = Map( a, b, ( x, t ) => x >= t ? 1 : 0 ); break;
			case 23: r = Map( a, b, c, SmoothStep ); break;
			case 24: r = new Vector4( a.X, b.X, c.X, s[at + 3].X ); break;
			case 25: r = new Vector4( a.X, b.X, c.X, 0 ); break;
			case 26: r = new Vector4( a.X, b.X, 0, 0 ); break;
			case 27: r = new Vector4( context.Time ); break;
			case 28: r = Map( a, b, ( x, y ) => x < y ? x : y ); break;
			case 29: r = Map( a, b, ( x, y ) => x > y ? x : y ); break;
			case 30: r = new Vector4( LinearToSrgb( a.X ), LinearToSrgb( a.Y ), LinearToSrgb( a.Z ), a.W ); break;
			case 31: r = new Vector4( SrgbToLinear( a.X ), SrgbToLinear( a.Y ), SrgbToLinear( a.Z ), a.W ); break;
			case 33:
				{
					var v = new Vector3( a.X, a.Y, a.Z );
					var length = v.Length();
					v = length > 0 ? v / length : v;
					r = new Vector4( v, a.W );
					break;
				}
			case 34:
				{
					var length = new Vector3( a.X, a.Y, a.Z ).Length();
					r = new Vector4( length, length, length, a.W );
					break;
				}
			case 35: r = Map( a, x => x * x ); break;
			case 37:
				{
					var (sin, cos) = MathF.SinCos( a.X );
					r = new Vector4( cos, -sin, sin, cos );
					break;
				}
			case 38:
				{
					var (sin, cos) = MathF.SinCos( b.X );
					r = new Vector4( a.X * cos - a.Y * sin, a.X * sin + a.Y * cos, a.Z, a.W );
					break;
				}
			case 39:
				{
					var (sin, cos) = MathF.SinCos( a.X );
					r = new Vector4( sin, cos, -sin, -cos );
					break;
				}
			default:
				return false;
		}

		s[at] = r;
		top = at + 1;
		return true;
	}
}
