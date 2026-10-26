using System.Runtime.InteropServices;

namespace SceneRendererTests;

/// <summary>
/// Broad-phase correctness and the shared CPU/GPU rectangle layout.
/// </summary>
[TestClass]
public class LightModulatorTests
{
	/// <summary>
	/// Thin rectangles must still intersect spheres at their faces, edges and corners.
	/// </summary>
	[TestMethod]
	public void RectangleLightCandidates()
	{
		var rect = new Sandbox.LightModulatorData
		{
			Origin = new( 100, 0, 0 ),
			Normal = Vector3.Forward,
			U = Vector3.Left / 20,
			V = Vector3.Up / 40,
			HalfWidth = 10,
			HalfHeight = 20
		};

		Assert.IsTrue( rect.IntersectsLight( new( 90, 0, 0 ), 10 ) );
		Assert.IsFalse( rect.IntersectsLight( new( 90, 0, 0 ), 9 ) );
		Assert.IsTrue( rect.IntersectsLight( new( 100, 13, 24 ), 5 ) );
		Assert.IsFalse( rect.IntersectsLight( new( 100, 13, 24 ), 4.99f ) );
		Assert.IsTrue( rect.IntersectsLight( new( 100, -10, -20 ), 0 ) );
		Assert.IsFalse( rect.IntersectsLight( new( 0, 0, 0 ), 50 ) );

		rect.Infinite = 1;
		Assert.IsTrue( rect.IntersectsLight( new( 90, 10000, -20000 ), 10 ) );
		Assert.IsFalse( rect.IntersectsLight( new( 90, 10000, -20000 ), 9.99f ) );
		Assert.IsTrue( rect.IntersectsLight( new( 100, -10000, 20000 ), 0 ) );
		rect.Normal = Vector3.Up;
		Assert.IsFalse( rect.IntersectsLight( new( 100, 0, 11 ), 10 ) );
		Assert.IsTrue( rect.IntersectsLight( new( 100, 0, 10 ), 10 ) );
	}

	/// <summary>
	/// Shader buffers use scalar packing, including texture transforms and colour adjustments.
	/// </summary>
	[TestMethod]
	public void RectangleGpuLayout()
	{
		Assert.AreEqual( 168, Marshal.SizeOf<Sandbox.LightModulatorData>() );
		Assert.AreEqual( 12, Marshal.OffsetOf<Sandbox.LightModulatorData>( "TextureIndex" ).ToInt32() );
		Assert.AreEqual( 48, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Normal" ).ToInt32() );
		Assert.AreEqual( 80, Marshal.OffsetOf<Sandbox.LightModulatorData>( "DistanceFeather" ).ToInt32() );
		Assert.AreEqual( 84, Marshal.OffsetOf<Sandbox.LightModulatorData>( "DistanceMipBias" ).ToInt32() );
		Assert.AreEqual( 88, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Tiling" ).ToInt32() );
		Assert.AreEqual( 96, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Offset" ).ToInt32() );
		Assert.AreEqual( 104, Marshal.OffsetOf<Sandbox.LightModulatorData>( "ScrollSpeed" ).ToInt32() );
		Assert.AreEqual( 112, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Rotation" ).ToInt32() );
		Assert.AreEqual( 120, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Contrast" ).ToInt32() );
		Assert.AreEqual( 124, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Brightness" ).ToInt32() );
		Assert.AreEqual( 128, Marshal.OffsetOf<Sandbox.LightModulatorData>( "BlendMode" ).ToInt32() );
		Assert.AreEqual( 132, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Strength" ).ToInt32() );
		Assert.AreEqual( 136, Marshal.OffsetOf<Sandbox.LightModulatorData>( "EdgeFeather" ).ToInt32() );
		Assert.AreEqual( 140, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Effect" ).ToInt32() );
		Assert.AreEqual( 144, Marshal.OffsetOf<Sandbox.LightModulatorData>( "EffectParameters" ).ToInt32() );
		Assert.AreEqual( 160, Marshal.OffsetOf<Sandbox.LightModulatorData>( "WorldSizedUVs" ).ToInt32() );
		Assert.AreEqual( 164, Marshal.OffsetOf<Sandbox.LightModulatorData>( "Infinite" ).ToInt32() );
	}
}
