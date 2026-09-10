using System;

namespace MathTests;

[TestClass]
public class Vector2Test
{
	[TestMethod]
	public void Parse()
	{
		{
			Vector2 v = Vector2.Parse( "1.1,2.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}

		{
			Vector2 v = Vector2.Parse( "1.1, 2.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}

		{
			Vector2 v = Vector2.Parse( "1.1 2.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}
	}

	[TestMethod]
	public void TryParse()
	{
		{
			Assert.IsTrue( Vector2.TryParse( "1.1,2.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}

		{
			Assert.IsTrue( Vector2.TryParse( "1.1, 2.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}

		{
			Assert.IsTrue( Vector2.TryParse( "1.1 2.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}

		{
			Assert.IsFalse( Vector2.TryParse( "1", out _ ) );
			Assert.IsFalse( Vector2.TryParse( "abcdef", out _ ) );
			Assert.IsFalse( Vector2.TryParse( "1.1, 2.2, 3.3", out _ ) );
		}
	}

	[TestMethod]
	public void ParseJson()
	{
		{
			Vector2 v = System.Text.Json.JsonSerializer.Deserialize<Vector2>( "\"1.1, 2.1\"" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}

		{
			Vector2 v = System.Text.Json.JsonSerializer.Deserialize<Vector2>( "[ 1.1, 2.1 ]" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
		}
	}

	/// <summary>
	/// AddClamped should add the vector freely while we're under the max length
	/// in that direction, then clamp so we never exceed it.
	/// </summary>
	[TestMethod]
	public void AddClamped()
	{
		{
			Vector2 v = new Vector2( 1, 1 );

			v = v.AddClamped( new Vector2( 0, 1 ), 5.0f );
			Assert.AreEqual( v, new Vector2( 1, 2 ) );

			v = v.AddClamped( new Vector2( 0, 2 ), 5.0f );
			Assert.AreEqual( v, new Vector2( 1, 4 ) );

			v = v.AddClamped( new Vector2( 0, 2 ), 5.0f );
			Assert.AreEqual( v, new Vector2( 1, 5 ) );
		}

	}

	[TestMethod]
	public void Degrees()
	{
		{
			var d = new Vector2( 0, -1 ).Degrees;
			Assert.AreEqual( 0.0f, MathF.Round( d ) );
		}

		{
			var d = new Vector2( 0, -100 ).Degrees;
			Assert.AreEqual( 0.0f, MathF.Round( d ) );
		}

		{
			var d = new Vector2( 0, 1 ).Degrees;
			Assert.AreEqual( 180.0f, MathF.Round( d ) );
		}

		{
			var d = new Vector2( 0, 100 ).Degrees;
			Assert.AreEqual( 180.0f, MathF.Round( d ) );
		}

		{
			var d = new Vector2( 1, 0 ).Degrees;
			Assert.AreEqual( 90.0f, MathF.Round( d ) );
		}

		{
			var d = new Vector2( -1, 0 ).Degrees;
			Assert.AreEqual( 270.0f, MathF.Round( d ) );
		}

		{
			var d = new Vector2( 1, -1 ).Degrees;
			Assert.AreEqual( 45.0f, MathF.Round( d ) );
		}

	}

	[TestMethod]
	public void FromDegrees()
	{
		{
			var v = Vector2.FromDegrees( 0 );
			Assert.AreEqual( new Vector2( 0, -1 ).ToString(), v.ToString() );
		}

		{
			var v = Vector2.FromDegrees( 90 );
			Assert.AreEqual( new Vector2( 1, 0 ).ToString(), v.ToString() );
		}

		{
			var v = Vector2.FromDegrees( 180 );
			Assert.AreEqual( new Vector2( 0, 1 ).ToString(), v.ToString() );
		}

		{
			var v = Vector2.FromDegrees( 270 );
			Assert.AreEqual( new Vector2( -1, 0 ).ToString(), v.ToString() );
		}
	}
}
