using System;
using System.Threading;
using Sandbox.Internal;

namespace SystemTests;

[TestClass]
public class StringBuilderRaceTest
{
	[TestMethod]
	public void SafeStringBuilderCorrectUsage()
	{
		var sb = new SafeStringBuilder( 16 );
		sb.Append( "Hello" ).Append( ", " ).Append( "World" ).Append( '!' );
		Assert.AreEqual( "Hello, World!", sb.ToString() );
		Assert.AreEqual( 13, sb.Length );

		sb.Length = 5;
		Assert.AreEqual( "Hello", sb.ToString() );

		sb.Clear();
		Assert.AreEqual( 0, sb.Length );

		sb.AppendLine( "Line1" );
		sb.AppendLine( "Line2" );
		Assert.AreEqual( $"Line1{Environment.NewLine}Line2{Environment.NewLine}", sb.ToString() );

		var sb2 = new SafeStringBuilder();
		sb2.Append( "AB" );
		sb.Clear().Append( sb2 );
		Assert.AreEqual( "AB", sb.ToString() );
	}

	[TestMethod]
	public void SafeStringBuilderRaceDoesNotCorruptState()
	{
		const int capacity = 84;
		const int iterations = 2_000;

		for ( int i = 0; i < iterations; i++ )
		{
			var sb = new SafeStringBuilder( capacity );
			sb.Append( 'A', capacity );

			using var barrier = new ManualResetEventSlim( false );

			var t0 = new Thread( () => { barrier.Wait(); sb.Append( "XY" ); } ) { IsBackground = true };
			var t1 = new Thread( () => { barrier.Wait(); sb.Length = capacity - 4; } ) { IsBackground = true };

			t0.Start();
			t1.Start();
			barrier.Set();
			t0.Join( 5000 );
			t1.Join( 5000 );

			int len = sb.Length;
			Assert.IsTrue( len >= 0, $"Length went negative ({len}) on iteration {i}" );
			string str = sb.ToString();
			Assert.AreEqual( len, str.Length, $"ToString().Length != Length on iteration {i}" );
		}
	}

}
