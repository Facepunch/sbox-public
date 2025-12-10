using Sandbox.Audio;

namespace Engine;

[TestClass]
public class Audio
{
	[TestMethod]
	public void Silence()
	{
		MixBuffer buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreNotEqual( 0, buffer.LevelMax );
		buffer.Silence();
		Assert.AreEqual( 0, buffer.LevelMax );
	}

	[TestMethod]
	public void LevelMax()
	{
		MixBuffer buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreEqual( buffer.LevelMax, buffer.Buffer.ToArray().Max() );
	}

	[TestMethod]
	public void LevelAvg()
	{
		MixBuffer buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreEqual( buffer.LevelAvg, buffer.Buffer.ToArray().Average(), 0.001f );
	}

	[TestMethod]
	public void Copy()
	{
		MixBuffer buffer = new MixBuffer();
		MixBuffer bufferTarget = new MixBuffer();
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
		MixBuffer buffer = new MixBuffer();
		MixBuffer bufferTarget = new MixBuffer();
		bufferTarget.Silence();
		Assert.IsTrue( bufferTarget.LevelAvg == 0 );

		buffer.RandomFill();

		Assert.IsFalse( buffer.LevelAvg == 0 );

		bufferTarget.MixFrom( buffer, 0.5f );
		Assert.AreEqual( buffer.LevelAvg * 0.5f, bufferTarget.LevelAvg, 0.001f );
	}

}
