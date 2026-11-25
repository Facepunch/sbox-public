namespace MathTests;

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

	[TestMethod]
	public void ForwardMatchesTheRotationAndDoesNotAllocate()
	{
		foreach ( var angles in new[] { new Angles( 0, 0, 0 ), new Angles( 30, 45, 0 ), new Angles( -80, 170, 10 ), new Angles( 89, -90, 0 ) } )
		{
			var forward = angles.Forward;
			var expected = angles.ToRotation().Forward;
			Assert.IsTrue( forward.Distance( expected ) < 0.0001f, $"{angles}: {forward} vs {expected}" );
		}

		var a = new Angles( 12, 34, 0 );
		_ = a.Forward;
		var before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ ) _ = a.Forward;
		Assert.AreEqual( 0, System.GC.GetAllocatedBytesForCurrentThread() - before );
	}
}
