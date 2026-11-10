using System;
using System.Collections.Generic;
using System.Text;

namespace AudioTests;

/// <summary>
/// Pins the WAV loop parse. Sound editors mark a loop with a "cue " chunk rather than any
/// per-sound flag, so SoundData has to read it or those sounds never loop.
/// </summary>
[TestClass]
public class SoundDataWavTest
{
	/// <summary>
	/// A minimal 8-bit mono WAV, optionally carrying the cue (and cue + mark) chunks that declare
	/// a loop. Laid out the way real WAVs are: fmt, data, LIST, cue, LIST.
	/// </summary>
	private static byte[] Wav( int sampleCount, int? cuePoint = null, int? markLength = null )
	{
		var body = new List<byte>();

		void Chunk( string id, Action<List<byte>> write )
		{
			var payload = new List<byte>();
			write( payload );

			body.AddRange( Encoding.ASCII.GetBytes( id ) );
			body.AddRange( BitConverter.GetBytes( payload.Count ) );
			body.AddRange( payload );

			if ( payload.Count % 2 != 0 )
				body.Add( 0 );
		}

		Chunk( "fmt ", p =>
		{
			p.AddRange( BitConverter.GetBytes( (ushort)1 ) );    // PCM
			p.AddRange( BitConverter.GetBytes( (ushort)1 ) );    // mono
			p.AddRange( BitConverter.GetBytes( 22050 ) );        // rate
			p.AddRange( BitConverter.GetBytes( 22050 ) );        // bytes/sec
			p.AddRange( BitConverter.GetBytes( (ushort)1 ) );    // block align
			p.AddRange( BitConverter.GetBytes( (ushort)8 ) );    // bits
		} );

		Chunk( "data", p => p.AddRange( new byte[sampleCount] ) );

		// An INFO list often precedes the cue; the parse has to skip it and only
		// treat a LIST *after* the cue as the loop marker.
		Chunk( "LIST", p =>
		{
			p.AddRange( Encoding.ASCII.GetBytes( "INFO" ) );
			p.AddRange( new byte[24] );
		} );

		if ( cuePoint is null )
			return Riff( body );

		Chunk( "cue ", p =>
		{
			p.AddRange( BitConverter.GetBytes( 1 ) );            // one cue point
			p.AddRange( new byte[20] );                          // id, position, chunk id/start
			p.AddRange( BitConverter.GetBytes( cuePoint.Value ) );
		} );

		if ( markLength is not null )
		{
			Chunk( "LIST", p =>
			{
				p.AddRange( Encoding.ASCII.GetBytes( "adtl" ) );
				p.AddRange( new byte[12] );
				p.AddRange( BitConverter.GetBytes( markLength.Value ) );  // +24
				p.AddRange( Encoding.ASCII.GetBytes( "mark" ) );          // +28
			} );
		}

		return Riff( body );
	}

	private static byte[] Riff( List<byte> body )
	{
		var wav = new List<byte>();
		wav.AddRange( Encoding.ASCII.GetBytes( "RIFF" ) );
		wav.AddRange( BitConverter.GetBytes( body.Count + 4 ) );
		wav.AddRange( Encoding.ASCII.GetBytes( "WAVE" ) );
		wav.AddRange( body );
		return wav.ToArray();
	}

	[TestMethod]
	public void NoCueChunkMeansNoLoop()
	{
		var data = SoundData.FromWav( Wav( 1000 ) );

		Assert.AreEqual( 1000u, data.SampleCount );
		Assert.AreEqual( -1, data.LoopStart );
		Assert.AreEqual( 0, data.LoopEnd );
	}

	[TestMethod]
	public void CueChunkStartsALoop()
	{
		var data = SoundData.FromWav( Wav( 1000, cuePoint: 128 ) );

		Assert.AreEqual( 128, data.LoopStart );

		// No mark, so the loop runs to the end of the sound.
		Assert.AreEqual( 0, data.LoopEnd );
	}

	[TestMethod]
	public void MarkerEndsTheLoopEarly()
	{
		var data = SoundData.FromWav( Wav( 1000, cuePoint: 100, markLength: 400 ) );

		Assert.AreEqual( 100, data.LoopStart );
		Assert.AreEqual( 500, data.LoopEnd );
	}
}
