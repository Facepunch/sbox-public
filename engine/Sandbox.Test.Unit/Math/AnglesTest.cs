namespace MathTest;

[TestClass]
public class AnglesTest
{
	[TestMethod]
	public void Parse()
	{
		{
			Angles a = Angles.Parse( "1.1,2.1,3.1" );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Angles a = Angles.Parse( "1.1, 2.1, 3.1" );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Angles a = Angles.Parse( "[1.1,2.1,3.1]" );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Angles a = Angles.Parse( "[	1.1,	2.1,	3.1]	" );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Angles a = Angles.Parse( "[	1.1,\n\r   2.1,\n\r   3.1\n\r		]	" );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}
	}

	[TestMethod]
	public void TryParse()
	{
		{
			Assert.IsTrue( Angles.TryParse( "1.1,2.1,3.1", out var a ) );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Assert.IsTrue( Angles.TryParse( "1.1, 2.1, 3.1", out var a ) );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Assert.IsTrue( Angles.TryParse( "[1.1,2.1,3.1]", out var a ) );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Assert.IsTrue( Angles.TryParse( "[	1.1,	2.1,	3.1]	", out var a ) );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Assert.IsTrue( Angles.TryParse( "[	1.1,\n\r   2.1,\n\r   3.1\n\r		]	", out var a ) );

			Assert.AreEqual( a.pitch, 1.1f );
			Assert.AreEqual( a.yaw, 2.1f );
			Assert.AreEqual( a.roll, 3.1f );
		}

		{
			Assert.IsFalse( Angles.TryParse( "1", out _ ) );
			Assert.IsFalse( Angles.TryParse( "abcdef", out _ ) );
			Assert.IsFalse( Angles.TryParse( "1.1, 2.2", out _ ) );
		}
	}
}
