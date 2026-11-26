using System.Collections.Generic;
using System.Text.Json;

namespace ServicesTests;

/// <summary>
/// Exercises the performance samples sent with activity heartbeats.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PerformanceStatsTest
{
	/// <summary>
	/// Every submitted batch belongs to exactly one heartbeat, including when a snapshot races with a writer.
	/// </summary>
	[TestMethod]
	public void ConcurrentSnapshotsPreserveSamples()
	{
		const int samplesPerWriter = 50_000;
		var snapshots = new List<object>();

		static void Collect( double value )
		{
			for ( int i = 0; i < samplesPerWriter; i++ )
			{
				Api.Performance.CollectStats( [("First", value), ("Second", value)] );
			}
		}

		Api.Performance.Flip();
		Parallel.Invoke( () => Collect( 1 ), () => Collect( 3 ), () =>
		{
			for ( int i = 0; i < 64; i++ )
			{
				snapshots.Add( Api.Performance.Flip() );
			}
		} );
		snapshots.Add( Api.Performance.Flip() );

		int count = 0;
		double sum = 0;

		foreach ( var snapshot in snapshots )
		{
			var stats = JsonSerializer.SerializeToElement( snapshot ).GetProperty( "Stats" );
			var hasFirst = stats.TryGetProperty( "First", out var first );
			Assert.AreEqual( hasFirst, stats.TryGetProperty( "Second", out var second ) );
			if ( !hasFirst ) continue;

			var batchCount = first.GetProperty( "Cnt" ).GetInt32();
			var batchSum = first.GetProperty( "Sum" ).GetDouble();
			Assert.AreEqual( batchCount, second.GetProperty( "Cnt" ).GetInt32() );
			Assert.AreEqual( batchSum, second.GetProperty( "Sum" ).GetDouble() );

			count += batchCount;
			sum += batchSum;
		}

		Assert.AreEqual( samplesPerWriter * 2, count );
		Assert.AreEqual( samplesPerWriter * 4.0, sum );
	}
}
