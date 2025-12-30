using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace bytePack;

public partial class RoundTrip
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

}
