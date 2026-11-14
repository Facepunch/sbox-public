using System.Runtime.InteropServices;

namespace BorealisTests;

/// <summary>
/// The engine's sprite uploads, which Borealis's effects draw.
/// </summary>
[TestClass]
public class SpriteLayoutTests
{
	/// <summary>
	/// Sprite CPU uploads must use the same scalar layout as both sprite shaders, including motion-blur copies.
	/// </summary>
	[TestMethod]
	public void SpriteRefractionGpuLayout()
	{
		Assert.AreEqual( 148, Marshal.SizeOf<Sandbox.Rendering.SpriteBatchSceneObject.SpriteData>() );
		Assert.AreEqual( 140, (int)Marshal.OffsetOf<Sandbox.Rendering.SpriteBatchSceneObject.SpriteData>( "NormalTextureHandle" ) );
		Assert.AreEqual( 144, (int)Marshal.OffsetOf<Sandbox.Rendering.SpriteBatchSceneObject.SpriteData>( "RefractionStrength" ) );
	}
}
