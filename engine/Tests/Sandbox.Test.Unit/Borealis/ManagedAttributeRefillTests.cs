namespace BorealisTests;

/// <summary>
/// A managed attribute set filled again (<see cref="ManagedRenderAttributes.BeginRefill"/>, a custom object's set in its run
/// under -borealisgpu): filled with the same as before it keeps its version, so what a backend lowered from it stays good;
/// whatever isn't set again is gone before anything reads it, as if the set had been cleared first.
/// </summary>
[TestClass]
public class ManagedAttributeRefillTests
{
	static void Fill( RenderAttributes set, bool withExtra )
	{
		set.Set( "Count", 12 );
		set.Set( "Colour", new Vector4( 1, 2, 3, 4 ) );
		set.SetCombo( "D_REFILL", 1 );
		if ( withExtra ) set.Set( "Extra", 5.0f );
	}

	[TestMethod]
	public void TheSameFillKeepsTheVersion()
	{
		var set = RenderAttributes.CreateManaged();
		var managed = set.Managed;
		managed.BeginRefill();
		Fill( set, true );
		managed.EndRefill();
		var (version, removed) = (managed.Version, managed.RemovedAt);

		managed.BeginRefill();
		Fill( set, true );
		managed.EndRefill();
		Assert.AreEqual( version, managed.Version );
		Assert.AreEqual( removed, managed.RemovedAt );

		managed.BeginRefill();
		Fill( set, true );
		set.Set( "Count", 13 );
		managed.EndRefill();
		Assert.AreNotEqual( version, managed.Version, "a changed value moves it" );
		Assert.AreEqual( removed, managed.RemovedAt, "and removes nothing" );
	}

	[TestMethod]
	public void WhatIsNotSetAgainIsGoneBeforeItIsRead()
	{
		var set = RenderAttributes.CreateManaged();
		var managed = set.Managed;
		managed.BeginRefill();
		Fill( set, true );
		managed.EndRefill();
		var version = managed.Version;

		// Read part way through: only what was set since the refill began
		managed.BeginRefill();
		set.Set( "Count", 12 );
		Assert.AreEqual( 1, managed.ValueCount );
		Assert.IsNull( managed.Get( "Extra" ) );
		Assert.IsNull( managed.GetCombo( "D_REFILL" ) );
		Assert.IsTrue( managed.RemovedAt > version, "a reader that copies what changed copies everything again" );
		Fill( set, false );
		managed.EndRefill();
		Assert.AreEqual( 2, managed.ValueCount );
		Assert.AreEqual( 1, managed.GetCombo( "D_REFILL" ) );

		// Not read until the end: what wasn't set again goes then
		var before = managed.RemovedAt;
		managed.BeginRefill();
		set.Set( "Count", 12 );
		managed.EndRefill();
		Assert.AreEqual( 1, managed.ValueCount );
		Assert.IsNull( managed.GetCombo( "D_REFILL" ) );
		Assert.IsTrue( managed.RemovedAt > before );
	}
}
