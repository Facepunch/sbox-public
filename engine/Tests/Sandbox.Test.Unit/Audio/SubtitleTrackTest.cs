using System.Collections.Generic;

namespace AudioTests;

[TestClass]
public class SubtitleTrackTest
{
	// "hello there friend" - a gap between "there" and "friend"
	static SubtitleTrack ThreeWordTrack() => new( new List<SubtitleTrack.Word>
	{
		new() { Text = "hello", StartTime = 0.1f, EndTime = 0.3f },
		new() { Text = "there", StartTime = 0.3f, EndTime = 0.5f },
		new() { Text = "friend", StartTime = 0.8f, EndTime = 1.1f },
	} );

	[TestMethod]
	public void Properties()
	{
		var track = ThreeWordTrack();

		Assert.AreEqual( 3, track.Words.Count );
		Assert.AreEqual( 1.1f, track.Duration, 0.001f );
		Assert.AreEqual( "hello there friend", track.Text );
	}

	[TestMethod]
	public void WordsGetSortedByStartTime()
	{
		var track = new SubtitleTrack( new List<SubtitleTrack.Word>
		{
			new() { Text = "world", StartTime = 0.5f, EndTime = 0.7f },
			new() { Text = "hello", StartTime = 0.1f, EndTime = 0.3f },
		} );

		Assert.AreEqual( "hello", track.Words[0].Text );
		Assert.AreEqual( "world", track.Words[1].Text );
		Assert.AreEqual( "hello world", track.Text );
	}

	[TestMethod]
	public void BlankWordsGetDropped()
	{
		var track = new SubtitleTrack( new List<SubtitleTrack.Word>
		{
			new() { Text = "hello", StartTime = 0.1f, EndTime = 0.3f },
			new() { Text = "  ", StartTime = 0.3f, EndTime = 0.4f },
			new() { Text = null, StartTime = 0.4f, EndTime = 0.5f },
		} );

		Assert.AreEqual( 1, track.Words.Count );
		Assert.AreEqual( "hello", track.Text );
	}

	[TestMethod]
	public void WordIndexAtWalksTheTrack()
	{
		var track = ThreeWordTrack();

		// Before the first word
		Assert.AreEqual( -1, track.WordIndexAt( 0.0f ) );

		// A word counts as started at exactly its start time
		Assert.AreEqual( 0, track.WordIndexAt( 0.1f ) );
		Assert.AreEqual( 0, track.WordIndexAt( 0.2f ) );
		Assert.AreEqual( 1, track.WordIndexAt( 0.4f ) );

		// In the gap the last started word holds
		Assert.AreEqual( 1, track.WordIndexAt( 0.6f ) );

		// And past the end the last word holds
		Assert.AreEqual( 2, track.WordIndexAt( 0.9f ) );
		Assert.AreEqual( 2, track.WordIndexAt( 5.0f ) );
	}

	[TestMethod]
	public void EmptyTrack()
	{
		var track = new SubtitleTrack( new List<SubtitleTrack.Word>() );

		Assert.AreEqual( 0, track.Words.Count );
		Assert.AreEqual( 0.0f, track.Duration );
		Assert.AreEqual( "", track.Text );
		Assert.AreEqual( -1, track.WordIndexAt( 0.5f ) );
	}
}
