namespace SystemTest;

[TestClass]
public class TimeParse
{
	[TestMethod]
	public void TimeParseSeconds()
	{
		float time = 0;

		Assert.IsTrue( new Parse( "1s" ).TryReadTime( out time ) );
		Assert.AreEqual( 1000, time );

		Assert.IsTrue( new Parse( "1.0s" ).TryReadTime( out time ) );
		Assert.AreEqual( 1000, time );

		Assert.IsTrue( new Parse( ".1s" ).TryReadTime( out time ) );
		Assert.AreEqual( 100, time );

		Assert.IsTrue( new Parse( "1.234s" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( " 1.234s" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n 1.234s" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n 1.234s " ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n 1.234s \n" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n1.234s\n" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsFalse( new Parse( "1.234 s" ).TryReadTime( out time ) );
		Assert.IsFalse( new Parse( "1,234s " ).TryReadTime( out time ) );

	}

	[TestMethod]
	public void TimeParseMilliSeconds()
	{
		float time = 0;

		Assert.IsTrue( new Parse( "1ms" ).TryReadTime( out time ) );
		Assert.AreEqual( 1, time );

		Assert.IsTrue( new Parse( "10.0ms" ).TryReadTime( out time ) );
		Assert.AreEqual( 10, time );

		Assert.IsTrue( new Parse( "100.0ms" ).TryReadTime( out time ) );
		Assert.AreEqual( 100, time );

		Assert.IsTrue( new Parse( "1234.0ms" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( " 1234ms" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n 1234ms" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n 1234ms " ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n 1234ms \n" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsTrue( new Parse( "\n1234ms\n" ).TryReadTime( out time ) );
		Assert.AreEqual( 1234, time );

		Assert.IsFalse( new Parse( "1.234 ms" ).TryReadTime( out _ ) );
		Assert.IsFalse( new Parse( "1,234s ms" ).TryReadTime( out _ ) );
		Assert.IsFalse( new Parse( "1,234sm s" ).TryReadTime( out _ ) );

	}
}
