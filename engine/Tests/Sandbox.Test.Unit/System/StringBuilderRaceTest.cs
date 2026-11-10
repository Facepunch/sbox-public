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

		// Two long-lived threads released together by a barrier each iteration, instead of two new threads per iteration
		SafeStringBuilder sb = null;
		bool stop = false;
		Exception failure = null;
		using var barrier = new Barrier( 3 );

		Thread StartWorker( Action race )
		{
			var thread = new Thread( () =>
			{
				while ( true )
				{
					barrier.SignalAndWait();
					if ( stop ) return;
					try { race(); }
					catch ( Exception e ) { failure = e; }
					barrier.SignalAndWait();
				}
			} )
			{ IsBackground = true };
			thread.Start();
			return thread;
		}

		var t0 = StartWorker( () => sb.Append( "XY" ) );
		var t1 = StartWorker( () => sb.Length = capacity - 4 );

		try
		{
			for ( int i = 0; i < iterations; i++ )
			{
				sb = new SafeStringBuilder( capacity );
				sb.Append( 'A', capacity );

				// Release both workers, then wait for them to finish
				barrier.SignalAndWait();
				barrier.SignalAndWait();

				Assert.IsNull( failure, $"Worker threw on iteration {i}: {failure}" );
				int len = sb.Length;
				Assert.IsTrue( len >= 0, $"Length went negative ({len}) on iteration {i}" );
				string str = sb.ToString();
				Assert.AreEqual( len, str.Length, $"ToString().Length != Length on iteration {i}" );
			}
		}
		finally
		{
			stop = true;
			barrier.SignalAndWait();
			t0.Join( 5000 );
			t1.Join( 5000 );
		}
	}

}
