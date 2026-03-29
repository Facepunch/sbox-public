using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

[Expose]
[JsonConverter( typeof( ParticleGradientConverter ) )]
public struct ParticleGradient
{
	public ParticleGradient()
	{
	}

	public ValueType Type { readonly get; set; }
	public EvaluationType Evaluation { readonly get; set; }

	public Gradient GradientA { readonly get; set; } = Color.White;
	public Gradient GradientB { readonly get; set; } = Color.White;
	public Color ConstantA { readonly get; set; } = Color.White;
	public Color ConstantB { readonly get; set; } = Color.White;

	public Color ConstantValue
	{
		readonly get => ConstantA;
		set => ConstantA = value;
	}


	public static implicit operator ParticleGradient( Color color )
	{
		return new ParticleGradient { Type = ValueType.Constant, Evaluation = EvaluationType.Life, ConstantValue = color };
	}

	[Expose]
	public enum ValueType
	{
		Constant,
		Range,
		Gradient
	}

	[Expose]
	public enum EvaluationType
	{
		Life,
		Frame,
		Particle
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Color Evaluate( in float delta, in float randomFixed )
	{
		var d = Evaluation switch
		{
			EvaluationType.Life => delta,
			EvaluationType.Frame => Random.Shared.Float( 0, 1 ),
			EvaluationType.Particle => randomFixed,
			_ => delta,
		};

		switch ( Type )
		{
			case ValueType.Constant:
				{
					return ConstantValue;
				}

			case ValueType.Range:
				{
					return Color.Lerp( ConstantA, ConstantB, d );
				}

			case ValueType.Gradient:
				{
					return GradientA.Evaluate( d );
				}
		}

		return ConstantValue;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Color Evaluate( Particle p, int seed, [CallerLineNumber] int line = 0 )
	{
		return Evaluate( p.LifeDelta, p.Rand( seed, line ) );
	}
}

/// <summary>
/// <see cref="JsonConverter"/> for <see cref="ParticleGradient"/> that omits unused properties.
/// Will serialize as just a <see cref="Color"/> string for <see cref="ParticleGradient.ValueType.Constant"/>.
/// </summary>
file sealed class ParticleGradientConverter : JsonConverter<ParticleGradient>
{
	private struct Model
	{
		public ParticleGradient.ValueType Type { get; init; }
		public ParticleGradient.EvaluationType Evaluation { get; init; }

		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Color? ConstantA { get; set; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Color? ConstantB { get; set; }

		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Gradient? GradientA { get; set; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public Gradient? GradientB { get; set; }
	}

	public override ParticleGradient Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		// If a string, read as a Constant

		if ( reader.TokenType == JsonTokenType.String )
		{
			return JsonSerializer.Deserialize<Color>( ref reader, options );
		}

		var model = JsonSerializer.Deserialize<Model>( ref reader, options );

		return new ParticleGradient
		{
			Type = model.Type,
			Evaluation = model.Evaluation,
			ConstantA = model.ConstantA ?? Color.White,
			ConstantB = model.ConstantB ?? Color.White,
			GradientA = model.GradientA ?? Color.White,
			GradientB = model.GradientB ?? Color.White
		};
	}

	public override void Write( Utf8JsonWriter writer, ParticleGradient value, JsonSerializerOptions options )
	{
		// If just a Constant, write as a color string

		if ( value.Type == ParticleGradient.ValueType.Constant )
		{
			JsonSerializer.Serialize( writer, value.ConstantValue, options );
			return;
		}

		var model = new Model { Type = value.Type, Evaluation = value.Evaluation };

		switch ( value.Type )
		{
			case ParticleGradient.ValueType.Range:
				model.ConstantA = value.ConstantA;
				model.ConstantB = value.ConstantB;
				break;

			case ParticleGradient.ValueType.Gradient:
				model.GradientA = value.GradientA;
				model.GradientB = value.GradientB;
				break;

			default:
				throw new NotImplementedException( $"Serializing {value.Type} not implemented yet." );
		}

		JsonSerializer.Serialize( writer, model, options );
	}
}
