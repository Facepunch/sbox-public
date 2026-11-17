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

		// The copy says when it changed, so what reads it converts it again only then
		var version = source.Shadow.Version;
		source.Set( "Height", 3.0f );
		Assert.AreNotEqual( version, source.Shadow.Version );
		CollectionAssert.AreEqual( new[] { 3f }, source.Shadow.GetFloats( "Height" ) );

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

	/// <summary>
	/// A managed set filled every frame (a block's, a command list's) reuses its arrays: setting numbers, vectors, matrices,
	/// and constant bytes again, or after a clear, allocates nothing.
	/// </summary>
	[TestMethod]
	public void SettingAgainAllocatesNothing()
	{
		var set = RenderAttributes.CreateManaged();
		void Fill( float f )
		{
			set.Set( "Height", f );
			set.Set( "Offset", new Vector4( f, 2, 3, 4 ) );
			set.Set( "World", Matrix.CreateTranslation( new Vector3( f, 0, 0 ) ) );
			set.SetData( "Block", new Vector4( f, 0, 0, 0 ) );
			set.SetCombo( "D_GRID", 1 );
		}

		Fill( 1 );
		set.Clear();
		Fill( 2 );
		var before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 10; i++ )
		{
			Fill( i );
			if ( i == 5 ) set.Clear();
		}

		Assert.AreEqual( 0, System.GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated setting values again" );
		CollectionAssert.AreEqual( new[] { 9f, 2, 3, 4 }, set.Managed.GetFloats( "Offset" ) );
	}

	/// <summary>
	/// Merged values are the target's own: setting the source again, which writes its arrays in place, doesn't change what
	/// the target holds; and a reader can ask for just what was set after a version.
	/// </summary>
	[TestMethod]
	public void MergedValuesAreCopies()
	{
		var source = RenderAttributes.CreateManaged();
		source.Set( "Height", 2.5f );
		source.SetData( "Block", new Vector4( 1, 2, 3, 4 ) );
		var target = RenderAttributes.CreateManaged();
		source.MergeTo( target );

		source.Set( "Height", 7.0f );
		source.SetData( "Block", new Vector4( 5, 6, 7, 8 ) );
		CollectionAssert.AreEqual( new[] { 2.5f }, target.Managed.GetFloats( "Height" ) );
		CollectionAssert.AreEqual( new[] { 1f, 2, 3, 4 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( (byte[])target.Managed.Get( "Block" ) ).ToArray() );

		var version = target.Managed.Version;
		target.Set( "Height", 2.5f );
		target.SetCombo( "D_GRID", 1 );
		var changed = new System.Collections.Generic.List<uint>();
		foreach ( var (token, _) in target.Managed.ValuesSince( version ) ) changed.Add( token );
		foreach ( var (token, _) in target.Managed.CombosSince( version ) ) changed.Add( token );
		CollectionAssert.AreEquivalent( new[] { new StringToken( "Height" ).Value, new StringToken( "D_GRID" ).Value }, changed );
	}
}
