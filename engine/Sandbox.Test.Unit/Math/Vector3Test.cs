namespace MathTest;

[TestClass]
public class Vector3Test
{
	[TestMethod]
	public void Parse()
	{
		{
			Vector3 v = Vector3.Parse( "1.1,2.1,3.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "1.1, 2.1, 3.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "[1.1,2.1,3.1]" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "[	1.1,	2.1,	3.1]	" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = Vector3.Parse( "[	1.1,\n\r   2.1,\n\r   3.1\n\r		]	" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}
	}

	[TestMethod]
	public void TryParse()
	{
		{
			Assert.IsTrue( Vector3.TryParse( "1.1,2.1,3.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "1.1, 2.1, 3.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "[1.1,2.1,3.1]", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "[	1.1,	2.1,	3.1]	", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsTrue( Vector3.TryParse( "[	1.1,\n\r   2.1,\n\r   3.1\n\r		]	", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Assert.IsFalse( Vector2.TryParse( "abcdef", out _ ) );
		}
	}

	[TestMethod]
	public void ParseJson()
	{
		{
			Vector3 v = System.Text.Json.JsonSerializer.Deserialize<Vector3>( "\"1.1, 2.1, 3.1\"" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}

		{
			Vector3 v = System.Text.Json.JsonSerializer.Deserialize<Vector3>( "[ 1.1, 2.1, 3.1 ]" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
		}
	}

	[TestMethod]
	public void AddClamped()
	{
		{
			Vector3 v = new Vector3( 1, 1, 1 );

			v = v.AddClamped( new Vector3( 0, 1, 0 ), 5.0f );
			Assert.AreEqual( v, new Vector3( 1, 2, 1 ) );

			v = v.AddClamped( new Vector3( 0, 2, 0 ), 5.0f );
			Assert.AreEqual( v, new Vector3( 1, 4, 1 ) );

			v = v.AddClamped( new Vector3( 0, 2, 0 ), 5.0f );
			Assert.AreEqual( v, new Vector3( 1, 5, 1 ) );
		}

	}
}
