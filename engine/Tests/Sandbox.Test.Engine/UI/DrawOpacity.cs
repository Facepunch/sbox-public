using Sandbox.UI;
using System;

namespace UITests;

[TestClass]
public class DrawOpacityTest : PainterTestBase
{
	[TestMethod]
	public void OpacityClampsAndScopesRestoreItsValue()
	{
		using var scope = Paint.Scope();
		PaintOpacity = 2;
		Assert.AreEqual( 1f, PaintOpacity );
		PaintOpacity = -1;
		Assert.AreEqual( 0f, PaintOpacity );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintOpacity = float.NaN );
		using ( Paint.Scope() )
		{
			PaintOpacity = 0.5f;
		}
		Assert.AreEqual( 0f, PaintOpacity );
	}
}
