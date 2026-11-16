using Sandbox;

namespace RenderTests;

/// <summary>
/// A native attribute set keeping a managed copy (a scene object's under <c>-borealisgpu</c>) merges into a managed set, as a
/// custom object's <c>Attributes.MergeTo( Graphics.Attributes )</c> does on a backend that lowers attributes itself.
/// </summary>
[TestClass]
public class RenderAttributeShadowTest
{
	[TestMethod]
	public void NativeSetWithManagedCopyMergesIntoManagedSet()
	{
		var source = new RenderAttributes();
		source.KeepManagedCopy();
		source.Set( "Height", 2.5f );
		source.Set( "Offset", new Vector2( 3, 4 ) );
		source.SetCombo( "D_GRID", 1 );

		// Still a native set: it reads back natively
		Assert.IsFalse( source.IsManaged );
		Assert.AreEqual( 2.5f, source.GetFloat( "Height" ) );

		var target = RenderAttributes.CreateManaged();
		source.MergeTo( target );
		CollectionAssert.AreEqual( new[] { 2.5f }, target.Managed.GetFloats( "Height" ) );
		CollectionAssert.AreEqual( new[] { 3f, 4f }, target.Managed.GetFloats( "Offset" ) );
		Assert.AreEqual( 1, target.Managed.GetCombo( "D_GRID" ) );

		// Without the copy a native set has nothing to give a managed one
		var plain = new RenderAttributes();
		plain.Set( "Height", 2.5f );
		var empty = RenderAttributes.CreateManaged();
		plain.MergeTo( empty );
		Assert.IsNull( empty.Managed.GetFloats( "Height" ) );
	}

	/// <summary>
	/// The managed copy as a set of its own (what a dynamic object draws with on Borealis.Gpu) is managed, reads what's set
	/// on the native set after it's made, and is the same set every time.
	/// </summary>
	[TestMethod]
	public void ShadowSetIsTheManagedCopy()
	{
		var source = new RenderAttributes();
		Assert.IsNull( source.ShadowSet, "no copy, no set" );

		source.KeepManagedCopy();
		var set = source.ShadowSet;
		Assert.IsTrue( set.IsManaged );
		source.Set( "LineThickness", 3.0f );
		source.SetCombo( "D_NO_ZTEST", 1 );
		CollectionAssert.AreEqual( new[] { 3f }, set.Managed.GetFloats( "LineThickness" ) );
		Assert.AreEqual( 1, set.Managed.GetCombo( "D_NO_ZTEST" ) );
		Assert.AreSame( set, source.ShadowSet );
	}
}
