using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using Sandbox.Internal;

namespace SystemTest;

[TestClass]
public class StringBuilderRaceTest
{
	private static readonly FieldInfo s_chunkLengthField =
		typeof( StringBuilder ).GetField( "m_ChunkLength", BindingFlags.NonPublic | BindingFlags.Instance )
		?? throw new InvalidOperationException( "Could not find StringBuilder.m_ChunkLength; BCL internals may have changed." );

	private static readonly FieldInfo s_chunkCharsField =
		typeof( StringBuilder ).GetField( "m_ChunkChars", BindingFlags.NonPublic | BindingFlags.Instance )
		?? throw new InvalidOperationException( "Could not find StringBuilder.m_ChunkChars; BCL internals may have changed." );

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
		Assert.AreEqual( "Line1\r\nLine2\r\n", sb.ToString() );

		var sb2 = new SafeStringBuilder();
		sb2.Append( "AB" );
		sb.Clear().Append( sb2 );
		Assert.AreEqual( "AB", sb.ToString() );
	}

	[TestMethod]
	public void BclStringBuilderNegativeLengthExploit()
	{
		const int capacity = 64;
		var sb = new StringBuilder( capacity );
		sb.Append( 'A', capacity );

		const int simulatedChunkLength = -4;
		s_chunkLengthField.SetValue( sb, simulatedChunkLength );
		var chunkChars = (char[])s_chunkCharsField.GetValue( sb )!;

		const uint appendLength = 2u;
		uint passThreshold = unchecked((uint)chunkChars.Length - (uint)simulatedChunkLength);

		Assert.IsTrue( appendLength <= passThreshold );
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

	[TestMethod]
	[Ignore]
	public void BenchmarkStringBuilderVsSafe()
	{
		const int warmup = 10_000;
		const int runs = 500_000;
		const string payload = "Hello, World! ";

		for ( int i = 0; i < warmup; i++ ) { var sb = new StringBuilder( 128 ); sb.Append( payload ); _ = sb.ToString(); }
		var sw = Stopwatch.StartNew();
		for ( int i = 0; i < runs; i++ ) { var sb = new StringBuilder( 128 ); sb.Append( payload ); _ = sb.ToString(); }
		sw.Stop();
		double bclNs = sw.Elapsed.TotalNanoseconds / runs;

		for ( int i = 0; i < warmup; i++ ) { var sb = new SafeStringBuilder( 128 ); sb.Append( payload ); _ = sb.ToString(); }
		sw = Stopwatch.StartNew();
		for ( int i = 0; i < runs; i++ ) { var sb = new SafeStringBuilder( 128 ); sb.Append( payload ); _ = sb.ToString(); }
		sw.Stop();
		double safeNs = sw.Elapsed.TotalNanoseconds / runs;

		Console.WriteLine( $"StringBuilder:     {bclNs:F1} ns/iter" );
		Console.WriteLine( $"SafeStringBuilder: {safeNs:F1} ns/iter  ({safeNs / bclNs:F2}x, +{safeNs - bclNs:F1} ns lock overhead)" );
	}
}
