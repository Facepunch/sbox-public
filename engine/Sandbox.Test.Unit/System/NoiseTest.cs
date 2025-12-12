using System;
using System.Collections.Generic;
using Sandbox.Utility;

namespace SystemTest;

[TestClass]
public class NoiseTest
{
	static void TestNoiseFunction( Func<Vector3, float> fn )
	{
		List<float> values = new List<float>();

		for ( int i = 0; i < 10_000; i++ )
		{
			var v = SandboxSystem.Random.VectorInCube() * 200.0f;
			var val = fn( v );

			values.Add( val );
		}

		var avg = values.Average();
		var min = values.Min();
		var max = values.Max();

		Console.WriteLine( $"min: {min:0.00}" );
		Console.WriteLine( $"max: {max:0.00}" );
		Console.WriteLine( $"avg: {avg:0.00}" );

		Assert.IsTrue( min >= 0.0f, $"{min} >= 0" );
		Assert.IsTrue( max <= 1.0f, $"{max} <= 1" );
	}

	[TestMethod]
	public void Perlin1()
	{
		TestNoiseFunction( v => Noise.Perlin( v.x ) );
	}

	[TestMethod]
	public void Perlin2()
	{
		TestNoiseFunction( ( v ) => Noise.Perlin( v.x, v.y ) );
	}

	[TestMethod]
	public void Perlin3()
	{
		TestNoiseFunction( ( v ) => Noise.Perlin( v.x, v.y, v.z ) );
	}

	[TestMethod]
	public void Simplex1()
	{
		TestNoiseFunction( v => Noise.Simplex( v.x ) );
	}

	[TestMethod]
	public void Simplex2()
	{
		TestNoiseFunction( ( v ) => Noise.Simplex( v.x, v.y ) );
	}

	[TestMethod]
	public void Simplex3()
	{
		TestNoiseFunction( ( v ) => Noise.Simplex( v.x, v.y, v.z ) );
	}

	[TestMethod]
	public void Fbm1()
	{
		TestNoiseFunction( v => Noise.Fbm( 5, v.x ) );
	}

	[TestMethod]
	public void Fbm2()
	{
		TestNoiseFunction( ( v ) => Noise.Fbm( 5, v.x, v.y ) );
	}

	[TestMethod]
	public void Fbm3()
	{
		TestNoiseFunction( ( v ) => Noise.Fbm( 5, v.x, v.y, v.z ) );
	}
}
