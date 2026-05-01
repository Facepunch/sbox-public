using System;
using System.Text.Json;

namespace SystemTest;

[TestClass]
public class ParticleGradientTest
{
	/// <summary>
	/// Tests serializing a <see cref="ParticleGradient.ValueType.Constant"/>,
	/// which should be serialized as a plain <see cref="Color"/> string.
	/// </summary>
	[TestMethod]
	public void SerializeConstant()
	{
		ParticleGradient gradient = Color.Red;

		var json = JsonSerializer.Serialize( gradient, Json.options );

		Assert.AreEqual( "\"1,0,0,1\"", json );
	}

	/// <summary>
	/// Tests deserializing a <see cref="ParticleGradient.ValueType.Constant"/>,
	/// from a <see cref="Color"/> string.
	/// </summary>
	[TestMethod]
	public void DeserializeConstant()
	{
		const string json = "\"1,0,0,1\"";

		var deserialized = JsonSerializer.Deserialize<ParticleGradient>( json, Json.options );

		Assert.AreEqual( ParticleGradient.ValueType.Constant, deserialized.Type );
		Assert.AreEqual( Color.Red, deserialized.ConstantA );
	}

	/// <summary>
	/// Ranges have ConstantA and ConstantB, with no GradientA or GradientB.
	/// </summary>
	[TestMethod]
	public void SerializeRange()
	{
		var gradient = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Range,
			Evaluation = ParticleGradient.EvaluationType.Particle,
			ConstantA = "1,0,0,1",
			ConstantB = "0,1,0,1"
		};

		var json = JsonSerializer.Serialize( gradient, Json.options );

		Console.WriteLine( json );

		Assert.IsFalse( json.Contains( "GradientA" ) );
		Assert.IsFalse( json.Contains( "GradientB" ) );

		var deserialized = JsonSerializer.Deserialize<ParticleGradient>( json, Json.options );

		Assert.AreEqual( ParticleGradient.ValueType.Range, deserialized.Type );
		Assert.AreEqual( ParticleGradient.EvaluationType.Particle, deserialized.Evaluation );
		Assert.AreEqual( Color.Red, deserialized.ConstantA );
		Assert.AreEqual( Color.Green, deserialized.ConstantB );
	}

	/// <summary>
	/// Gradients have GradientA and GradientB, with no ConstantA or ConstantB.
	/// </summary>
	[TestMethod]
	public void SerializeGradient()
	{
		var gradient = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			Evaluation = ParticleGradient.EvaluationType.Life,
			GradientA = new Gradient(
				new Gradient.ColorFrame( 0f, Color.Red ),
				new Gradient.ColorFrame( 1f, Color.Green ) ),
			GradientB = new Gradient(
				new Gradient.ColorFrame( 0.5f, Color.Blue ) )
		};

		var json = JsonSerializer.Serialize( gradient, Json.options );

		Console.WriteLine( json );

		Assert.IsFalse( json.Contains( "ConstantA" ) );
		Assert.IsFalse( json.Contains( "ConstantB" ) );

		var deserialized = JsonSerializer.Deserialize<ParticleGradient>( json, Json.options );

		Assert.AreEqual( ParticleGradient.ValueType.Gradient, deserialized.Type );
		Assert.AreEqual( ParticleGradient.EvaluationType.Life, deserialized.Evaluation );
		Assert.AreEqual( Color.Red, deserialized.GradientA.Colors[0].Value );
		Assert.AreEqual( Color.Green, deserialized.GradientA.Colors[1].Value );
		Assert.AreEqual( Color.Blue, deserialized.GradientB.Colors[0].Value );
	}

	[TestMethod]
	public void DeserializeLegacyConstant()
	{
		const string json =
			"""
			{
			  "Type": "Constant",
			  "Evaluation": "Particle",
			  "GradientA": {
			    "blend": "Linear",
			    "color": [
			      {
			        "t": 0.5,
			        "c": "1,1,1,1"
			      }
			    ],
			    "alpha": []
			  },
			  "GradientB": {
			    "blend": "Linear",
			    "color": [
			      {
			        "t": 0.5,
			        "c": "1,1,1,1"
			      }
			    ],
			    "alpha": []
			  },
			  "ConstantA": "1,0,0,1",
			  "ConstantB": "1,1,1,1"
			}
			""";

		var deserialized = JsonSerializer.Deserialize<ParticleGradient>( json, Json.options );

		Assert.AreEqual( ParticleGradient.ValueType.Constant, deserialized.Type );
		Assert.AreEqual( Color.Red, deserialized.ConstantA );
	}
}
