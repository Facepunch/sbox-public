namespace UITests;

/// <summary>
/// Preserves stroke paint value semantics while internal drawing reads paint by reference.
/// </summary>
[TestClass]
public class PainterStrokeFillTest
{
	/// <summary>
	/// Updating paint through an initializer preserves record equality and leaves the source stroke unchanged.
	/// </summary>
	[TestMethod]
	public void FillInitializersPreserveRecordValueSemantics()
	{
		var original = Stroke.Solid( Color.Red, 4 );
		foreach ( var fill in new[] { Fill.Solid( Color.Blue ), Fill.LinearGradient( Color.Red, Color.Blue ) } )
		{
			var updated = original with { Fill = fill };
			var expected = Stroke.Solid( fill, 4 );
			Assert.AreEqual( expected, updated );
			Assert.IsTrue( expected == updated );
			Assert.AreEqual( expected.GetHashCode(), updated.GetHashCode() );
			Assert.AreNotEqual( original, updated );
			Assert.AreEqual( Fill.Solid( Color.Red ), original.Fill );
			Assert.AreEqual( fill, updated.Fill );
			Assert.AreEqual( fill, Stroke.GetFill( in updated ) );
		}

		Assert.AreEqual( Fill.None, default( Stroke ).Fill );
		Assert.AreEqual( Fill.Solid( Color.Green ), new Stroke { Fill = Color.Green }.Fill );
	}
}
