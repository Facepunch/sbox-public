using System;

namespace AudioTests;

[TestClass]
public class PcmLayoutValidationTest
{
	static (int Start, int End) Validate( int dataLength, int channels, int format, uint sampleCount, int loopStart = -1, int loopEnd = 0 )
	{
		SoundFile.ValidatePcmLayout( dataLength, channels, format, sampleCount, ref loopStart, ref loopEnd );
		return (loopStart, loopEnd);
	}

	[TestMethod]
	public void LayoutPastDataIsRejected()
	{
		Assert.AreEqual( (10, 50), Validate( 100, 1, 1, 100, 10, 50 ) );
		Assert.ThrowsException<ArgumentException>( () => Validate( 100, 1, 0, 100 ) );
		Assert.ThrowsException<ArgumentException>( () => Validate( 1024, 256, 0, 2 ) );
	}

	[TestMethod]
	public void LoopPastSamplesIsDropped()
	{
		Assert.AreEqual( (0, 0), Validate( 100, 1, 1, 100, 0, 100000 ) );
		Assert.AreEqual( (-1, 0), Validate( 100, 1, 1, 100, 500 ) );
	}
}
