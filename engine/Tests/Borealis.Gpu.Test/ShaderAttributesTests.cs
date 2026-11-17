using Borealis.Backend;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// The packed form of a draw's dynamic combos that <see cref="ShaderDrawer.Variant"/> finds variants by
/// (<see cref="ShaderAttributes.ComboBits"/>): it tells apart what <see cref="ShaderAttributes.ComboKey"/> tells apart.
/// </summary>
[TestClass]
public class ShaderAttributesTests
{
	/// <summary>
	/// complex.shader's dynamic combos: none set packs to zero, each set alone packs differently, the same values pack the
	/// same, and a value past a byte doesn't pack (the caller keys by name).
	/// </summary>
	[TestMethod]
	public void ComboBitsTellCombosApart()
	{
		var shader = ShaderFile.Read( ShaderFileTests.Files.Read( "shaders/complex.shader" ), "complex" );
		var names = new[] { ShaderProgramType.Vertex, ShaderProgramType.Pixel }.SelectMany( t => shader[t]?.DynamicCombos ?? [] ).Select( c => c.Name ).Distinct().ToArray();
		if ( names.Length == 0 ) Assert.Inconclusive( "complex.shader has no dynamic combos" );

		Assert.IsTrue( new ShaderAttributes().ComboBits( shader, out var none ) );
		Assert.AreEqual( 0UL, none );

		var seen = new Dictionary<ulong, string> { [none] = "" };
		foreach ( var name in names )
		{
			var attributes = new ShaderAttributes();
			attributes.SetCombo( name, 1 );
			Assert.IsTrue( attributes.ComboBits( shader, out var bits ), name );
			Assert.IsFalse( seen.ContainsKey( bits ), $"{name} packs as {seen.GetValueOrDefault( bits )} does" );
			seen[bits] = name;

			var again = new ShaderAttributes();
			again.SetCombo( name.ToLowerInvariant(), 1 );
			Assert.IsTrue( again.ComboBits( shader, out var same ) );
			Assert.AreEqual( bits, same, "combo names compare ignoring case" );
			Assert.AreEqual( attributes.ComboKey( shader ), again.ComboKey( shader ) );

			var wide = new ShaderAttributes();
			wide.SetCombo( name, 256 );
			Assert.IsFalse( wide.ComboBits( shader, out _ ), $"{name} = 256 is keyed by name" );
		}
	}
}
