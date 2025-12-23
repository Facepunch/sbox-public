namespace MathTest;

[TestClass]
public class Vector4Test
{
	[TestMethod]
	public void Parse()
	{
		{
			Vector4 v = Vector4.Parse( "1.1,2.1,3.1,4.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Vector4 v = Vector4.Parse( "1.1, 2.1, 3.1, 4.1" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Vector4 v = Vector4.Parse( "[1.1,2.1,3.1,4.1]" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Vector4 v = Vector4.Parse( "[	1.1,	2.1,	3.1,	4.1]	" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Vector4 v = Vector4.Parse( "[	1.1,\n\r   2.1,\n\r   3.1,\n\r		4.1\n\r		]	" );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}
	}

	[TestMethod]
	public void TryParse()
	{
		{
			Assert.IsTrue( Vector4.TryParse( "1.1,2.1,3.1,4.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Assert.IsTrue( Vector4.TryParse( "1.1, 2.1, 3.1, 4.1", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Assert.IsTrue( Vector4.TryParse( "[1.1,2.1,3.1,4.1]", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Assert.IsTrue( Vector4.TryParse( "[	1.1,	2.1,	3.1,	4.1]	", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Assert.IsTrue( Vector4.TryParse( "[	1.1,\n\r   2.1,\n\r   3.1,\n\r		4.1\n\r		]	", out var v ) );

			Assert.AreEqual( v.x, 1.1f );
			Assert.AreEqual( v.y, 2.1f );
			Assert.AreEqual( v.z, 3.1f );
			Assert.AreEqual( v.w, 4.1f );
		}

		{
			Assert.IsFalse( Vector4.TryParse( "1", out _ ) );
			Assert.IsFalse( Vector4.TryParse( "abcdef", out _ ) );
			Assert.IsFalse( Vector4.TryParse( "1.1, 2.2", out _ ) );
			Assert.IsFalse( Vector4.TryParse( "1.1, 2.2, 3.3", out _ ) );
		}
	}
}
