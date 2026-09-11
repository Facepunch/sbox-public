using System;
using Sandbox.Engine;
using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class PanelLayerMips
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// GenerateMipMaps stops once the short side runs out, so a mip past that is allocated but never
	/// written - and a soft drop-shadow on a thin panel would sample it.
	/// </summary>
	[TestMethod]
	public void ThinLayerMipChainStopsAtShortSide()
	{
		var root = UiTesting.CreateRoot();
		var panel = new Panel( root );
		panel.Style.Set( "position: absolute; width: 300px; height: 20px; filter: drop-shadow( 0 2px 20px black );" );

		root.BuildStyleRules();
		root.Layout();

		var shortSideMips = (int)MathF.Log2( 20 ) + 1;

		Assert.IsTrue( panel.LayerMipCount > 1, "a drop-shadow this soft should want a mip chain" );
		Assert.IsTrue( panel.LayerMipCount <= shortSideMips, $"LayerMipCount {panel.LayerMipCount} goes past the {shortSideMips} mips a 20px side has" );
	}

	/// <summary>
	/// blur.hlsl reads the deepest mip whose own blur stays within 90% of the sigma, and nothing past it. For
	/// blur( 8px ) that's mip 2 (its chain carries a 6.6px sigma, mip 3 would be 13.6px), so three mips.
	/// </summary>
	[TestMethod]
	public void BlurMipChainEndsAtTheLevelBlurReads()
	{
		var root = UiTesting.CreateRoot();
		var panel = new Panel( root );
		panel.Style.Set( "position: absolute; width: 400px; height: 300px; filter: blur( 8px );" );

		root.BuildStyleRules();
		root.Layout();

		Assert.AreEqual( 3, panel.LayerMipCount );
	}

	/// <summary>
	/// Mip 1 already carries a 3px sigma, more than a small blur wants, so blur.hlsl stays on mip 0 and the
	/// layer needs no chain at all.
	/// </summary>
	[TestMethod]
	public void SmallBlurNeedsNoMipChain()
	{
		var root = UiTesting.CreateRoot();
		var panel = new Panel( root );
		panel.Style.Set( "position: absolute; width: 400px; height: 300px; filter: blur( 3px );" );

		root.BuildStyleRules();
		root.Layout();

		Assert.AreEqual( 1, panel.LayerMipCount );
	}
}
