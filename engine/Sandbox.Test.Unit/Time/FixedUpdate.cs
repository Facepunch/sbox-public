using System;

namespace Timing;

[TestClass]
[DoNotParallelize]
public class FixedUpdateTests
{
	[TestMethod]
	[DataRow( 1, 5, 11 )]     // Very low frequency
	[DataRow( 5, 5, 59 )]     // Standard case
	[DataRow( 30, 5, 359 )]   // Higher frequency
	[DataRow( 60, 5, 719 )]   // Same as frame rate
	[DataRow( 120, 5, 1439 )] // Higher than frame rate
	[DataRow( 1, 1, 11 )]     // maxSteps = 1, low frequency
	[DataRow( 10, 1, 119 )]   // maxSteps = 1, medium frequency
	[DataRow( 60, 2, 719 )]   // maxSteps = 2, high frequency (maxSteps doesn't limit here)
	public void TestFixedUpdateFrequency( int frequency, int maxSteps, int expectedCalls )
	{
		var fu = new FixedUpdate();
		fu.Frequency = frequency;
		var fixedDelta = fu.Delta;
		int callTimes = 0;
		Action action = () =>
		{
			callTimes++;
			Assert.AreEqual( Time.Delta, fixedDelta, "Fixed delta doesn't match!" );

			// Get the remainder as a number around 0
			float remainder = (Time.Now % fixedDelta);
			if ( remainder > fixedDelta / 2 ) remainder = fixedDelta - remainder;
			Assert.AreEqual( 0, remainder, 0.00001f, "Time.Now doesn't align with step!" );
		};

		float time = 0;
		float fps = 60;
		float frameDelta = 1.0f / fps;
		int loops = 0;

		// Simulate 12 seconds (shorter time for faster test execution)
		while ( time < 12.0f )
		{
			loops++;
			fu.Run( action, time, maxSteps );
			time += frameDelta;
		}

		Console.WriteLine( $"{loops} loops at {fps:N0} FPS with maxSteps={maxSteps} gave {callTimes} fixed updates at {frequency} Hz" );

		// Allow small tolerance for floating point differences at very low frequencies
		if ( frequency <= 1 )
		{
			Assert.IsTrue( Math.Abs( expectedCalls - callTimes ) <= 1,
				$"Expected approximately {expectedCalls} calls, got {callTimes}" );
		}
		else
		{
			Assert.AreEqual( expectedCalls, callTimes );
		}
	}

	[TestMethod]
	public void TestDeltaCalculation()
	{
		var fu = new FixedUpdate();

		// Test various frequencies
		fu.Frequency = 10;
		Assert.AreEqual( 0.1f, fu.Delta, 0.00001f );

		fu.Frequency = 60;
		Assert.AreEqual( 1.0f / 60, fu.Delta, 0.00001f );

		fu.Frequency = 1;
		Assert.AreEqual( 1.0f, fu.Delta, 0.00001f );

		// Test non-integer frequency
		fu.Frequency = 16.7f; // Approx 60fps / 1000 * 16.7ms
		Assert.AreEqual( 1.0f / 16.7f, fu.Delta, 0.00001f );
	}

	[TestMethod]
	public void TestMaxSteps()
	{
		var fu = new FixedUpdate();
		fu.Frequency = 10; // 10Hz = 0.1s per update
		int callCount = 0;
		Action action = () => callCount++;

		// Start at time 0
		fu.Run( action, 0, 5 );
		Assert.AreEqual( 0, callCount, "No updates should occur at start" );

		// Jump ahead by 1 second (10 steps)
		fu.Run( action, 1.0f, 5 );
		Assert.AreEqual( 5, callCount, "Should be limited by maxSteps" );

		// Small increment should continue from where we left off
		fu.Run( action, 1.1f, 5 );
		Assert.AreEqual( 6, callCount, "Should get 1 more update" );
	}

	[TestMethod]
	public void TestTimeNotAdvancing()
	{
		var fu = new FixedUpdate();
		fu.Frequency = 10;
		int callCount = 0;
		Action action = () => callCount++;

		// First call
		fu.Run( action, 1.0f, 5 );
		int firstCallCount = callCount;

		// Call again with same time
		fu.Run( action, 1.0f, 5 );
		Assert.AreEqual( firstCallCount, callCount, "No updates should occur when time doesn't advance" );
	}

	[TestMethod]
	public void TestNegativeTime()
	{
		var fu = new FixedUpdate();
		fu.Frequency = 10;
		int callCount = 0;
		Action action = () => callCount++;

		// First call with negative time
		fu.Run( action, -1.0f, 5 );

		// Then positive time
		fu.Run( action, 0.5f, 5 );

		// We expect calls because we're moving forward in time
		Assert.IsTrue( callCount > 0, "Should get updates when moving from negative to positive time" );
	}

	[TestMethod]
	public void TestNonIntegerFrequency()
	{
		var fu = new FixedUpdate();
		fu.Frequency = 2.5f; // 2.5Hz = 0.4s per update
		int callCount = 0;
		Action action = () => callCount++;

		float time = 0;
		float fps = 60;
		float frameDelta = 1.0f / fps;

		// Simulate 1 second
		while ( time < 1.0f )
		{
			fu.Run( action, time, 5 );
			time += frameDelta;
		}

		// At 2.5Hz, we expect 2 or 3 calls in 1 second
		// (Depends on floating point precision and exact timing)
		Assert.IsTrue( callCount >= 2 && callCount <= 3,
			$"Expected 2-3 updates with 2.5Hz in 1 second, got {callCount}" );
	}

	[TestMethod]
	public void TestLargeTimeJump()
	{
		var fu = new FixedUpdate();
		fu.Frequency = 10; // 10Hz
		int callCount = 0;
		Action action = () => callCount++;

		// Jump ahead by a large amount (20 seconds = 200 updates at 10Hz)
		fu.Run( action, 20.0f, 10 );

		// Should be limited by maxSteps
		Assert.AreEqual( 10, callCount );
	}
}
