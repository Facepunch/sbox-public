using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace BytePackTests;

public partial class RoundTripTest
{
	[TestMethod] public void Vector2() => DoRoundTrip( new Vector2( 1, 2 ) );
	[TestMethod] public void Vector3() => DoRoundTrip( new Vector3( 1, 2, 3 ) );
	[TestMethod] public void Vector4() => DoRoundTrip( new Vector4( 1, 2, 3, 4 ) );
	[TestMethod] public void Rotation() => DoRoundTrip( global::Rotation.LookAt( global::Vector3.Up ) );
	[TestMethod] public void Angles() => DoRoundTrip( new Angles( 0, 45, 0 ) );
	[TestMethod] public void Transform() => DoRoundTrip( new Transform( global::Vector3.One, global::Rotation.LookAt( global::Vector3.Up ), 34.0f ) );
	[TestMethod] public void Color() => DoRoundTrip( global::Color.Red );
	[TestMethod] public void Color32() => DoRoundTrip( global::Color.Red.ToColor32() );
	[TestMethod] public void Plane() => DoRoundTrip( new Plane( global::Vector3.Random, global::Vector3.Down ) );
	[TestMethod] public void Sphere() => DoRoundTrip( new Sphere( global::Vector3.Random, 100.0f ) );
	[TestMethod] public void BBox() => DoRoundTrip( global::BBox.FromPositionAndSize( global::Vector3.Random, 100.0f ) );
	[TestMethod] public void Rect() => DoRoundTrip( new Rect( 10, 10, 10, 10 ) );
	[TestMethod] public void Ray() => DoRoundTrip( new Ray( global::Vector3.Random, global::Vector3.Random ) );
	[TestMethod] public void TimeSince() => DoRoundTrip( (global::Sandbox.TimeSince)10.0f );
	[TestMethod] public void TimeUntil() => DoRoundTrip( (global::Sandbox.TimeUntil)10.0f );

	[TestMethod]
	public void ParticleFloatCurvesRoundTrip()
	{
		var a = Curve.Linear;
		a.TimeRange = new Vector2( -1, 2 );
		a.ValueRange = new Vector2( -3, 4 );
		var frame = a[0];
		frame.Mode = Curve.HandleMode.Stepped;
		a[0] = frame;
		var value = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.CurveRange,
			Evaluation = ParticleFloat.EvaluationType.Frame,
			Constants = new Vector4( 1, 2, 3, 4 ),
			CurveA = a,
			CurveB = Curve.Ease
		};

		var result = typeLibrary.FromBytes<ParticleFloat>( typeLibrary.ToBytes( value ) );
		Assert.AreEqual( value.Type, result.Type );
		Assert.AreEqual( value.Evaluation, result.Evaluation );
		Assert.AreEqual( value.Constants, result.Constants );
		Assert.AreEqual( a.TimeRange, result.CurveA.TimeRange );
		Assert.AreEqual( a.ValueRange, result.CurveA.ValueRange );
		CollectionAssert.AreEqual( a.Frames.ToArray(), result.CurveA.Frames.ToArray() );
		CollectionAssert.AreEqual( value.CurveB.Frames.ToArray(), result.CurveB.Frames.ToArray() );

		ParticleFloat constant = 42;
		var constantResult = typeLibrary.FromBytes<ParticleFloat>( typeLibrary.ToBytes( constant ) );
		Assert.AreEqual( 42f, constantResult.ConstantValue );
		Assert.IsTrue( constantResult.CurveA.Frames.IsDefault );
		Assert.IsTrue( constantResult.CurveB.Frames.IsDefault );
	}

}
