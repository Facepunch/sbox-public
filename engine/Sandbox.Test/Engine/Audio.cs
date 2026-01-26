using Sandbox.Audio;

namespace Engine;

[TestClass]
public class Audio
{
	[TestMethod]
	public void Silence()
	{
		using var buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreNotEqual( 0, buffer.LevelMax );
		buffer.Silence();
		Assert.AreEqual( 0, buffer.LevelMax );
	}

	[TestMethod]
	public void LevelMax()
	{
		using var buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreEqual( buffer.LevelMax, buffer.Buffer.ToArray().Max() );
	}

	[TestMethod]
	public void LevelAvg()
	{
		using var buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreEqual( buffer.LevelAvg, buffer.Buffer.ToArray().Average(), 0.001f );
	}

	[TestMethod]
	public void Copy()
	{
		using var buffer = new MixBuffer();
		using var bufferTarget = new MixBuffer();
		bufferTarget.Silence();
		Assert.IsTrue( bufferTarget.LevelAvg == 0 );

		buffer.RandomFill();

		Assert.IsFalse( buffer.LevelAvg == 0 );

		bufferTarget.CopyFrom( buffer );

		Assert.AreEqual( buffer.LevelAvg, bufferTarget.LevelAvg, 0.001f );
	}


	[TestMethod]
	public void MixFrom()
	{
		using var buffer = new MixBuffer();
		using var bufferTarget = new MixBuffer();
		bufferTarget.Silence();
		Assert.IsTrue( bufferTarget.LevelAvg == 0 );

		buffer.RandomFill();

		Assert.IsFalse( buffer.LevelAvg == 0 );

		bufferTarget.MixFrom( buffer, 0.5f );
		Assert.AreEqual( buffer.LevelAvg * 0.5f, bufferTarget.LevelAvg, 0.001f );
	}

}
