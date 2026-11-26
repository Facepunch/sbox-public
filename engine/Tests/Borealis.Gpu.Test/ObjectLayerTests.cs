using Borealis.Backend;

namespace BorealisGpuTests;

/// <summary>An object's combos as a key, which layers and records are told apart by.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectLayerTests
{
	/// <summary>
	/// Combos switching back and forth (a component setting one every frame) give the keys made before, the same strings,
	/// and make none.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void SwitchingCombosMakeNoKeys()
	{
		var on = new Dictionary<uint, int> { [12] = 1, [7] = 0 };
		var off = new Dictionary<uint, int> { [12] = 0, [7] = 0 };
		var first = ObjectLayer.ComboKey( on, "" );
		var second = ObjectLayer.ComboKey( off, first );
		Assert.AreEqual( "7=0;12=1;", first, "sorted by combo" );
		Assert.AreEqual( "7=0;12=0;", second );
		Assert.AreSame( first, ObjectLayer.ComboKey( on, second ), "the key made before" );

		var key = second;
		Allocations.AssertSettles( "switching combos", () =>
		{
			key = ObjectLayer.ComboKey( on, key );
			key = ObjectLayer.ComboKey( off, key );
		} );
		Assert.AreSame( second, key );
	}
}
