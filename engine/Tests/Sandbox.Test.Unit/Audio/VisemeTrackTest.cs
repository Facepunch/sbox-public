using System;
using System.Collections.Generic;

namespace AudioTests;

[TestClass]
public class VisemeTrackTest
{
	// PP over [0.1, 0.3] - viseme 1 in Visemes.MorphNames order
	static VisemeTrack SingleFrameTrack() => new( new List<VisemeFrame>
	{
		new() { Viseme = 1, StartTime = 0.1f, EndTime = 0.3f },
	} );

	// PP [0.1, 0.2] directly followed by AA (viseme 10) [0.2, 0.3]
	static VisemeTrack AdjacentFramesTrack() => new( new List<VisemeFrame>
	{
		new() { Viseme = 1, StartTime = 0.1f, EndTime = 0.2f },
		new() { Viseme = 10, StartTime = 0.2f, EndTime = 0.3f },
	} );

	[TestMethod]
	public void Properties()
	{
		var track = AdjacentFramesTrack();

		Assert.AreEqual( 2, track.Frames.Count );
		Assert.AreEqual( 0.3f, track.Duration, 0.001f );
	}

	[TestMethod]
	public void FramesGetSortedByStartTime()
	{
		var track = new VisemeTrack( new List<VisemeFrame>
		{
			new() { Viseme = 10, StartTime = 0.5f, EndTime = 0.6f },
			new() { Viseme = 1, StartTime = 0.1f, EndTime = 0.2f },
		} );

		Assert.AreEqual( 1, track.Frames[0].Viseme );
		Assert.AreEqual( 10, track.Frames[1].Viseme );
	}

	[TestMethod]
	public void SampleInsideFrame()
	{
		var track = SingleFrameTrack();
		Span<float> weights = stackalloc float[Visemes.Count];

		// Inside the frame the blend window stretches to the frame length (0.2):
		// window [0.15, 0.35] overlaps [0.1, 0.3] for 75% of its length
		Assert.IsTrue( track.Sample( 0.15f, weights ) );
		Assert.AreEqual( 0.75f, weights[1], 0.001f );

		// Everything else stays zero
		for ( var v = 0; v < Visemes.Count; v++ )
		{
			if ( v == 1 ) continue;
			Assert.AreEqual( 0.0f, weights[v] );
		}
	}

	[TestMethod]
	public void SampleRampsInBeforeFrame()
	{
		var track = SingleFrameTrack();
		Span<float> weights = stackalloc float[Visemes.Count];

		// Approaching the frame: window [0.05, 0.13] overlaps [0.1, 0.3] by 37.5%
		Assert.IsTrue( track.Sample( 0.05f, weights ) );
		Assert.AreEqual( 0.375f, weights[1], 0.001f );

		// Too early - the blend window doesn't reach the frame yet
		Assert.IsFalse( track.Sample( 0.0f, weights ) );
		Assert.AreEqual( 0.0f, weights[1] );
	}

	[TestMethod]
	public void SampleOutsideTrackIsZero()
	{
		var track = SingleFrameTrack();
		Span<float> weights = stackalloc float[Visemes.Count];

		weights[1] = 999;

		Assert.IsFalse( track.Sample( 5.0f, weights ) );
		Assert.AreEqual( 0.0f, weights[1] );

		Assert.IsFalse( track.Sample( -1.0f, weights ) );
		Assert.AreEqual( 0.0f, weights[1] );
	}

	[TestMethod]
	public void SampleCrossfadesAdjacentFrames()
	{
		var track = AdjacentFramesTrack();
		Span<float> weights = stackalloc float[Visemes.Count];

		// Halfway through PP with AA butting up against it - equal parts of each
		Assert.IsTrue( track.Sample( 0.15f, weights ) );
		Assert.AreEqual( 0.5f, weights[1], 0.001f );
		Assert.AreEqual( 0.5f, weights[10], 0.001f );
	}

	[TestMethod]
	public void SampleThrowsOnSmallBuffer()
	{
		var track = SingleFrameTrack();

		Assert.ThrowsException<ArgumentException>( () =>
		{
			var weights = new float[3];
			track.Sample( 0.1f, weights );
		} );
	}
}
