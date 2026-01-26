namespace Timing;

[TestClass]
public class TimeTests
{

	[TestMethod]
	public void TimeUntilBasics()
	{
		Time.Update( 0, 0 );

		TimeUntil tu = 0;

		Assert.AreEqual( 0, (float)tu );
		Assert.AreEqual( 0, tu.Relative );
		Assert.AreEqual( 0, tu.Absolute );
		Assert.AreEqual( (float)tu, tu.Relative );
		Assert.IsTrue( tu < 1 );
		Assert.IsTrue( tu >= 0 );

		tu = 1;

		Assert.AreEqual( 1, (float)tu );
		Assert.AreEqual( 1, tu.Relative );
		Assert.AreEqual( 1, tu.Absolute );
		Assert.AreEqual( (float)tu, tu.Relative );
		Assert.IsTrue( tu < 2.0f );
		Assert.IsTrue( tu < 2 );
		Assert.IsTrue( tu >= 1.0f );
		Assert.IsTrue( tu >= 1 );

		Time.Update( 10, 0 );

		Assert.AreEqual( -9, (float)tu );
		Assert.AreEqual( -9, tu.Relative );
		Assert.AreEqual( 1, tu.Absolute );

		tu = 5;

		Assert.AreEqual( 5, (float)tu );
		Assert.AreEqual( 5, tu.Relative );
		Assert.AreEqual( 15, tu.Absolute );
		Assert.IsTrue( tu < 10.0f );
		Assert.IsTrue( tu > 4.0f );
		Assert.IsTrue( tu >= 5.0f );
		Assert.IsTrue( tu >= 5 );

		Time.Update( 11, 0 );

		Assert.AreEqual( 4, (float)tu );
		Assert.AreEqual( 4, tu.Relative );
		Assert.AreEqual( 15, tu.Absolute );
		Assert.IsTrue( tu < 5.0f );
		Assert.IsTrue( tu > 3.0f );
		Assert.IsTrue( tu >= 4.0f );
		Assert.IsTrue( tu >= 4 );

		Time.Update( 16, 0 );

		Assert.AreEqual( -1, (float)tu );
		Assert.AreEqual( -1, tu.Relative );
		Assert.AreEqual( 15, tu.Absolute );
		Assert.IsTrue( tu < 0 );
		Assert.IsTrue( tu < 0.0f );

		tu = -1;
		bool finished = tu < 0;
		Assert.IsTrue( finished );

	}
}
