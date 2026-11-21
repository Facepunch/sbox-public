namespace BorealisTests;

/// <summary>
/// An attribute set kept in managed code (a renderer's under -borealisgpu) holds the textures set on it only while they're
/// set: once cleared (a disposed camera's, a command list reset), none of them is kept alive by it, as a native set releases
/// its handles.
/// </summary>
[TestClass]
public class ManagedAttributeLifetimeTests
{
	// A texture with no native side, enough for a managed set, which only keeps a reference to it
	static Texture FakeTexture()
	{
		var texture = (Texture)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Texture ) );
		System.GC.SuppressFinalize( texture );
		return texture;
	}

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
	static (System.WeakReference Whole, System.WeakReference Mip) SetTextures( RenderAttributes set )
	{
		var (whole, mip) = (FakeTexture(), FakeTexture());
		set.Set( "Color", whole );
		set.Set( "History", mip, 2 );
		Assert.AreSame( whole, ((ManagedRenderAttributes.TextureValue)set.Managed.Get( "Color" )).Texture );
		return (new System.WeakReference( whole ), new System.WeakReference( mip ));
	}

	[TestMethod]
	public void ClearedSetHoldsNoTextures()
	{
		var set = RenderAttributes.CreateManaged();
		var (whole, mip) = SetTextures( set );
		set.Clear();

		System.GC.Collect();
		System.GC.WaitForPendingFinalizers();
		System.GC.Collect();

		Assert.IsFalse( whole.IsAlive, "a whole texture set before the clear" );
		Assert.IsFalse( mip.IsAlive, "a mip of a texture set before the clear" );
		System.GC.KeepAlive( set );
	}
}
