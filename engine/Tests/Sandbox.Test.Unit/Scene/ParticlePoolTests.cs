#pragma warning disable CS0618 // the obsolete public pool is what's being tested

namespace SceneTests;

[TestClass]
[DoNotParallelize]
public class ParticlePoolTests
{
	[TestMethod]
	public void ParticlesPlantedInThePublicPoolAreNeverHandedOut()
	{
		var planted = new Particle();
		Particle.Pool.Enqueue( planted );

		try
		{
			Assert.AreNotSame( planted, Particle.Create() );
			Assert.AreNotSame( planted, Particle.Create( new System.Collections.Generic.Queue<Particle>() ) );
		}
		finally
		{
			Particle.Pool.Clear();
		}
	}
}
