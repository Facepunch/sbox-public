using Borealis.Backend;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// A draw's dynamic combos as the number <see cref="ShaderDrawer"/> looks its variants up by: each value a digit in its
/// combo's range, so lookups allocate nothing and different values never share a key.
/// </summary>
[TestClass]
public class ComboKeyTests
{
	[TestMethod]
	public void DynamicCombosPackWithoutAllocating()
	{
		var shader = ShaderFile.Read( ShaderFileTests.Files.Read( "shaders/morph_composite.shader" ), "morph_composite" );
		var attributes = new ShaderAttributes();

		attributes.SetCombo( "D_COMPOSITETYPE", 0 );
		Assert.IsTrue( attributes.TryPackCombos( shader, out var positions ) );
		attributes.SetCombo( "D_COMPOSITETYPE", 1 );
		Assert.IsTrue( attributes.TryPackCombos( shader, out var normals ) );
		Assert.AreNotEqual( positions, normals );

		var before = GC.GetAllocatedBytesForCurrentThread();
		attributes.TryPackCombos( shader, out _ );
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );

		// Outside its range: the text key instead
		attributes.SetCombo( "D_COMPOSITETYPE", 1000 );
		Assert.IsFalse( attributes.TryPackCombos( shader, out _ ) );
	}
}
