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

	/// <summary>
	/// Tests of Source 2's own renderer (its scene system's views, native render contexts), which draws only on Source 2's
	/// device: <c>SBOX_TEST_GRAPHICS=source2</c>.
	/// </summary>
	public static void RequireSource2()
	{
		if ( !Graphics.HasSource2Device || RenderBackend.Default is not Source2RenderBackend )
			Assert.Inconclusive( "Source 2's renderer: set SBOX_TEST_GRAPHICS=source2." );
	}
}
