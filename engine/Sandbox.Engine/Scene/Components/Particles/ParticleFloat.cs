using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

public interface IDynamicFloatContext
{
	/// <summary>
	/// Should return the lifetime delta we're going to use to evaluate
	/// </summary>
	float LifetimeDelta { get; }

	/// <summary>
	/// Should return the seed we're using for randomness
	/// </summary>
	int RandomSeed { get; }
}

/// <summary>
/// Represents a floating-point value that can change over time with support for various evaluation modes.
/// </summary>
[Expose]
public struct ParticleFloat : IJsonConvert, BytePack.ISerializer
{
	public ValueType Type { readonly get; set; }
	public EvaluationType Evaluation { readonly get; set; }

	[JsonInclude]
	public Vector4 Constants;

	/// <summary>
	/// Null until a curve is assigned. Immutable, so struct copies may share it.
	/// </summary>
	CurvePair _curves;

	sealed class CurvePair
	{
		public readonly Curve A;
		public readonly Curve B;

		public CurvePair( in Curve a, in Curve b )
		{
			A = a;
			B = b;
		}
	}

	public Curve CurveA
	{
		readonly get => _curves?.A ?? default;
		set => _curves = new CurvePair( value, CurveB );
	}

	public Curve CurveB
	{
		readonly get => _curves?.B ?? default;
		set => _curves = new CurvePair( CurveA, value );
	}

	[JsonIgnore]
	public float ConstantValue
	{
		readonly get => Constants.x;
		set => Constants.x = value;
	}

	[JsonIgnore]
	public float ConstantA
	{
		readonly get => Constants.x;
		set => Constants.x = value;
	}

	[JsonIgnore]
	public float ConstantB
	{
		readonly get => Constants.y;
		set => Constants.y = value;
	}

	[JsonIgnore]
	public CurveRange CurveRange
	{
		readonly get => new CurveRange( CurveA, CurveB );
		set => _curves = new CurvePair( value.A, value.B );
	}

	/// <summary>
	/// True when the value is a single constant, ignoring life delta and randomness.
	/// </summary>
	[JsonIgnore]
	public readonly bool IsConstant => Type == ValueType.Constant;

	public static implicit operator ParticleFloat( float v )
	{
		return new ParticleFloat { Type = ValueType.Constant, ConstantValue = v };
	}

	public ParticleFloat()
	{

	}

	public ParticleFloat( float a, float b )
	{
		Type = ValueType.Range;
		Evaluation = EvaluationType.Seed;
		ConstantA = a;
		ConstantB = b;
	}

	public enum ValueType
	{
		/// <summary>
		/// A value that doesn't change over time.
		/// </summary>
		Constant,

		/// <summary>
		/// The value is interpolated between two fixed floats.
		/// </summary>
		Range,

		/// <summary>
		/// A curve that defines how the value changes over time or based on an evaluation factor.
		/// </summary>
		Curve,

		/// <summary>
		/// Two curves where the value is interpolated between them.
		/// </summary>
		CurveRange
	}

	public enum EvaluationType
	{
		/// <summary>
		/// Evaluates the value based on the lifetime using its normalized age.
		/// </summary>
		Life,

		/// <summary>
		/// Evaluates the value based on the current frame, introducing randomness for dynamic effects.
		/// </summary>
		Frame,

		/// <summary>
		/// Evaluates the value based on a random seed. This means that in most situations, it's random per context.
		/// Like if this is on a particle, the value will be random per particle.
		/// </summary>
		Seed,

		[Obsolete( "This is moved to seed. This struct won't be particle specific in the future" )]
		Particle = Seed
	}

	/// <summary>
	/// Evaluates the value based on the given delta and random seed, optimized for performance.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Evaluate( in float delta, in float randomFixed )
	{
		float d = Evaluation switch
		{
			EvaluationType.Life => delta,
			EvaluationType.Frame => Random.Shared.Float( 0, 1 ),
			EvaluationType.Seed => randomFixed,
			_ => delta,
		};

		return Type switch
		{
			ValueType.Constant => ConstantValue,
			ValueType.Range => MathX.Lerp( ConstantA, ConstantB, d ),
			ValueType.Curve => CurveA.Evaluate( d ),
			ValueType.CurveRange => CurveRange.Evaluate( d, randomFixed ),
			_ => ConstantValue,
		};
	}

	/// <summary>
	/// Evaluates the value using a dynamic context and seed, optimized for clarity and functionality.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Evaluate( IDynamicFloatContext context, int seed, [CallerLineNumber] int line = 0 )
	{
		int randomFloatIndex = unchecked(context.RandomSeed ^ (line * 73856093) ^ seed);
		return Evaluate( context.LifetimeDelta, Game.Random.FloatDeterministic( randomFloatIndex ) );
	}

	/// <summary>
	/// Checks if the value is nearly zero.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly bool IsNearlyZero()
	{
		if ( Type == ParticleFloat.ValueType.Constant && ConstantValue.AlmostEqual( 0.0f ) )
			return true;

		return false;
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		var particleFloat = (ParticleFloat)value;
		bs.Write( (int)particleFloat.Type );
		bs.Write( (int)particleFloat.Evaluation );
		bs.Write( particleFloat.Constants );
		WriteCurve( ref bs, particleFloat.CurveA );
		WriteCurve( ref bs, particleFloat.CurveB );
	}

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var value = new ParticleFloat
		{
			Type = (ValueType)bs.Read<int>(),
			Evaluation = (EvaluationType)bs.Read<int>(),
			Constants = bs.Read<Vector4>()
		};
		var a = ReadCurve( ref bs );
		var b = ReadCurve( ref bs );
		if ( !a.Frames.IsDefault || !b.Frames.IsDefault || a.TimeRange != default || a.ValueRange != default || b.TimeRange != default || b.ValueRange != default )
			value._curves = new CurvePair( a, b );
		return value;
	}

	static void WriteCurve( ref ByteStream bs, Curve curve )
	{
		bs.Write( curve.TimeRange );
		bs.Write( curve.ValueRange );
		bs.Write( curve.Frames.IsDefault );
		if ( !curve.Frames.IsDefault )
			bs.WriteArray<Curve.Frame>( curve.Frames.AsSpan() );
	}

	static Curve ReadCurve( ref ByteStream bs )
	{
		var curve = default( Curve );
		curve.TimeRange = bs.Read<Vector2>();
		curve.ValueRange = bs.Read<Vector2>();
		if ( !bs.Read<bool>() )
			curve = curve.WithFrames( bs.ReadArray<Curve.Frame>() );
		return curve;
	}

	/// <summary>
	/// Reads a ParticleFloat instance from JSON, refactored for modularity.
	/// </summary>
	public static object JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
	{
		ParticleFloat value = new ParticleFloat();

		if ( reader.TokenType == JsonTokenType.Number )
		{
			value = (float)reader.GetDouble();
			return value;
		}

		if ( reader.TokenType != JsonTokenType.StartObject )
		{
			Log.Info( $"Unknown Token: {reader.TokenType}" );
			return value;
		}

		while ( reader.Read() && reader.TokenType != JsonTokenType.EndObject )
		{
			if ( reader.TokenType == JsonTokenType.PropertyName )
			{
				var name = reader.GetString();
				reader.Read();

				switch ( name )
				{
					case "Type":
						value.Type = JsonSerializer.Deserialize<ValueType>( ref reader, Json.options );
						break;
					case "Evaluation":
						value.Evaluation = JsonSerializer.Deserialize<EvaluationType>( ref reader, Json.options );
						break;
					case "CurveA":
						value.CurveA = JsonSerializer.Deserialize<Curve>( ref reader, Json.options );
						break;
					case "CurveB":
						value.CurveB = JsonSerializer.Deserialize<Curve>( ref reader, Json.options );
						break;
					case "Constants":
						value.Constants = JsonSerializer.Deserialize<Vector4>( ref reader, Json.options );
						break;
					default:
						reader.Skip();
						break;
				}
			}
		}

		return value;
	}

	/// <summary>
	/// Writes a ParticleFloat instance to JSON, refactored for modularity.
	/// </summary>
	public static void JsonWrite( object value, Utf8JsonWriter writer )
	{
		var target = (ParticleFloat)value;

		if ( target.Type == ValueType.Constant )
		{
			writer.WriteNumberValue( target.ConstantValue );
			return;
		}

		writer.WriteStartObject();

		writer.WritePropertyName( "Type" );
		JsonSerializer.Serialize( writer, target.Type, Json.options );

		writer.WritePropertyName( "Evaluation" );
		JsonSerializer.Serialize( writer, target.Evaluation, Json.options );

		// We only need to write this if it's not a constant
		if ( target.Type == ValueType.Curve || target.Type == ValueType.CurveRange )
		{
			writer.WritePropertyName( "CurveA" );
			JsonSerializer.Serialize( writer, target.CurveA, Json.options );

			writer.WritePropertyName( "CurveB" );
			JsonSerializer.Serialize( writer, target.CurveB, Json.options );
		}

		if ( target.Type == ValueType.Constant || target.Type == ValueType.Range )
		{
			writer.WritePropertyName( "Constants" );
			JsonSerializer.Serialize( writer, target.Constants, Json.options );
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// This is only here to remain "compatible" with RangedFloat
	/// </summary>
	[Obsolete, EditorBrowsable( EditorBrowsableState.Never )]
	public float GetValue()
	{
		return Evaluate( Random.Shared.Float(), 3 );
	}
}

[Expose]
public struct ParticleVector3
{
	[JsonInclude]
	public ParticleFloat X;

	[JsonInclude]
	public ParticleFloat Y;

	[JsonInclude]
	public ParticleFloat Z;

	public static implicit operator ParticleVector3( Vector3 v )
	{
		return new ParticleVector3 { X = v.x, Y = v.y, Z = v.z };
	}

	public readonly Vector3 Evaluate( float delta, float a, float b, float c )
	{
		var x = X.Evaluate( delta, a );
		var y = Y.Evaluate( delta, b );
		var z = Z.Evaluate( delta, c );

		return new Vector3( x, y, z );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Vector3 Evaluate( Particle p, int seed, [CallerLineNumber] int line = 0 )
	{
		return Evaluate( p.LifeDelta, p.Rand( seed, line ), p.Rand( seed + 1, line ), p.Rand( seed + 2, line ) );
	}

	public readonly bool IsNearlyZero()
	{
		return X.IsNearlyZero() && Y.IsNearlyZero() && Z.IsNearlyZero();
	}

	/// <summary>
	/// True when every component is a constant.
	/// </summary>
	[JsonIgnore]
	public readonly bool IsConstant => X.IsConstant && Y.IsConstant && Z.IsConstant;
}
