using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemTests;

[TestClass]
public class ParticleFloatTest
{
	/// <summary>
	/// Effects embed dozens of these and get cloned per impact; curve data must stay behind a reference.
	/// </summary>
	[TestMethod]
	public void SizeIs32Bytes()
	{
		Assert.AreEqual( 32, Unsafe.SizeOf<ParticleFloat>() );
	}

	/// <summary>
	/// Existing content stores range constants as a Vector4 under "Constants"; that must keep round tripping.
	/// </summary>
	[TestMethod]
	public void RangeSerializesConstantsAsVector4()
	{
		var value = new ParticleFloat( 2, 5 );
		value.Constants.z = 7;
		value.Constants.w = 9;
		Assert.IsNotNull( typeof( ParticleFloat ).GetField( nameof( ParticleFloat.Constants ) ) );

		var json = JsonSerializer.Serialize( value, Json.options );
		var node = JsonNode.Parse( json );

		Assert.IsNotNull( node["Constants"], json );
		Assert.AreEqual( new Vector4( 2, 5, 7, 9 ), node["Constants"].Deserialize<Vector4>( Json.options ) );

		var read = JsonSerializer.Deserialize<ParticleFloat>( json, Json.options );

		Assert.AreEqual( ParticleFloat.ValueType.Range, read.Type );
		Assert.AreEqual( ParticleFloat.EvaluationType.Seed, read.Evaluation );
		Assert.AreEqual( 2f, read.ConstantA );
		Assert.AreEqual( 5f, read.ConstantB );
		Assert.AreEqual( 7f, read.Constants.z );
		Assert.AreEqual( 9f, read.Constants.w );
	}

	[TestMethod]
	public void CurveRangeRoundTrips()
	{
		var value = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.CurveRange,
			CurveA = Curve.Linear,
			CurveB = Curve.Ease
		};

		var before = value.Evaluate( 0.5f, 0f );

		var json = JsonSerializer.Serialize( value, Json.options );
		var read = JsonSerializer.Deserialize<ParticleFloat>( json, Json.options );

		Assert.AreEqual( ParticleFloat.ValueType.CurveRange, read.Type );
		Assert.AreEqual( 2, read.CurveA.Length );
		Assert.AreEqual( 2, read.CurveB.Length );
		Assert.IsTrue( before.AlmostEqual( 0.5f ), $"{before}" );
		Assert.IsTrue( read.Evaluate( 0.5f, 0f ).AlmostEqual( before ) );
	}

	/// <summary>
	/// Struct copies share curve storage; assigning a curve on a copy must not leak into the original.
	/// </summary>
	[TestMethod]
	public void CopyIsIndependent()
	{
		var a = new ParticleFloat { Type = ParticleFloat.ValueType.Curve, CurveA = Curve.Linear };
		var b = a;
		b.CurveA = Curve.Ease;
		b.CurveRange = new CurveRange( Curve.Ease, Curve.Ease );

		Assert.AreEqual( Curve.Linear.Evaluate( 0.25f ), a.CurveA.Evaluate( 0.25f ) );
		Assert.AreEqual( Curve.Ease.Evaluate( 0.25f ), b.CurveA.Evaluate( 0.25f ) );
	}

	[TestMethod]
	public void IsConstant()
	{
		ParticleFloat constant = 3;
		ParticleVector3 vector = new Vector3( 1, 2, 3 );
		var mixed = vector;
		mixed.Y = new ParticleFloat( 1, 2 );

		Assert.IsTrue( constant.IsConstant );
		Assert.IsFalse( new ParticleFloat( 1, 2 ).IsConstant );
		Assert.IsTrue( vector.IsConstant );
		Assert.IsFalse( mixed.IsConstant );
	}

	[TestMethod]
	public void CustomJsonOptionsIgnoreConvenienceProperties()
	{
		var value = new ParticleFloat( 2, 5 );
		var json = JsonNode.Parse( JsonSerializer.Serialize( value, new JsonSerializerOptions() ) );
		Assert.IsNotNull( json["Constants"] );
		foreach ( var name in new[] { "ConstantValue", "ConstantA", "ConstantB", "CurveRange", "IsConstant" } )
			Assert.IsNull( json[name], name );
	}

	/// <summary>
	/// ParticleVector3 serializes by reflection, so saved content must only ever contain the components.
	/// </summary>
	[TestMethod]
	public void Vector3SerializesOnlyItsComponents()
	{
		ParticleVector3 vector3 = new Vector3( 1, 2, 3 );

		Assert.AreEqual( "{\"X\":1,\"Y\":2,\"Z\":3}", JsonNode.Parse( JsonSerializer.Serialize( vector3, Json.options ) ).ToJsonString() );
	}
}
