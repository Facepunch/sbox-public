using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class Transforms
{
	[TestMethod]
	public void ParseRotationZ()
	{
		{
			var t = new PanelTransform();
			t.Parse( "rotateZ( 10deg )" );
			var tx = t.BuildTransform( 1000, 1000, Vector2.Zero );
			Assert.AreEqual( Matrix.CreateRotationZ( 10 ), tx );
		}

		{
			var t = new PanelTransform();
			t.Parse( "rotateZ( 10 )" );
			var tx = t.BuildTransform( 1000, 1000, Vector2.Zero );
			Assert.AreEqual( Matrix.CreateRotationZ( 10 ), tx );
		}

		{
			var t = new PanelTransform();
			t.Parse( "rotateZ( 0.5turn )" );
			var tx = t.BuildTransform( 1000, 1000, Vector2.Zero );
			Assert.AreEqual( Matrix.CreateRotationZ( 180 ), tx );
		}

		{
			var t = new PanelTransform();
			t.Parse( "rotateZ( -0.5turn )" );
			var tx = t.BuildTransform( 1000, 1000, Vector2.Zero );
			Assert.AreEqual( Matrix.CreateRotationZ( -180 ), tx );
		}
	}

}
