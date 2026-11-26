namespace EngineTests;

/// <summary>
/// Values code sets on a material (<see cref="Material.SetInCode"/>), which devices read over the material's file: a value
/// set again, as a component animating a glow does every frame, changes in place and allocates nothing; a copy keeps its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MaterialSetInCodeTests
{
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void ValuesSetAgainAllocateNothing()
	{
		var material = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
		material.Set( "g_flGlow", 0.0f );
		material.Set( "g_vTint", Color.White );
		var set = material.SetInCode;
		var version = material.SetInCodeVersion;

		var t = 0;
		Allocations.AssertSettles( "values set again", () =>
		{
			t++;
			material.Set( "g_flGlow", t * 0.01f );
			material.Set( "g_vTint", new Color( t * 0.01f, 1, 1 ) );
			material.Set( "g_vTint", new Color( t * 0.01f, 1, 1 ) );
		} );

		Assert.AreSame( set, material.SetInCode, "the same set, its values changed in place" );
		Assert.AreEqual( new Vector4( t * 0.01f, 0, 0, 0 ), (Vector4)material.SetInCode["g_flGlow"] );
		Assert.IsTrue( material.SetInCodeVersion > version, "the version moves with each change" );

		var moved = material.SetInCodeVersion;
		material.Set( "g_flGlow", t * 0.01f );
		Assert.AreEqual( moved, material.SetInCodeVersion, "an unchanged value moves nothing" );

		material.Set( "g_flOther", 1.0f );
		Assert.AreNotSame( set, material.SetInCode, "a new parameter makes the set again" );
	}

	[TestMethod]
	public void ACopyKeepsItsOwnValues()
	{
		var material = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
		material.Set( "g_flGlow", 0.25f );
		var copy = material.CreateCopy();
		material.Set( "g_flGlow", 0.75f );

		Assert.AreEqual( new Vector4( 0.25f, 0, 0, 0 ), (Vector4)copy.SetInCode["g_flGlow"], "the copy keeps the value it was made with" );
		Assert.AreEqual( new Vector4( 0.75f, 0, 0, 0 ), (Vector4)material.SetInCode["g_flGlow"] );
	}
}
