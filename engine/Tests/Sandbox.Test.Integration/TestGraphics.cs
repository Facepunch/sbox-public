/// <summary>
/// Tests that draw run with <c>SBOX_TEST_GRAPHICS=1</c>, which draws on Borealis.Gpu as the game and the editor do
/// (<c>TestAppSystem</c>); headless runs (the default) skip them.
/// </summary>
internal static class TestGraphics
{
	public static void Require()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to draw on Borealis.Gpu." );
	}
}
