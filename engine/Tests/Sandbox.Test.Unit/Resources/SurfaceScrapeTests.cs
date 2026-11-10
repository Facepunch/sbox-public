using System.Runtime.InteropServices;

namespace ResourceTests;

[TestClass]
public class SurfaceScrapeTests
{
	[TestMethod]
	[DataRow( 0.0f, true )]
	[DataRow( 0.49f, true )]
	[DataRow( 0.5f, false )]
	[DataRow( 1.0f, false )]
	public void OpposingRoughnessSelectsScrapeSound( float roughness, bool smooth )
	{
		var roughSound = new SoundEvent();
		var smoothSound = new SoundEvent();
		var surface = new Surface
		{
			SoundCollection = new() { ScrapeRough = roughSound, ScrapeSmooth = smoothSound }
		};

		Assert.AreSame( smooth ? smoothSound : roughSound, surface.GetScrapeSound( new Surface
		{
			Friction = smooth ? 1.0f : 0.0f,
			ScrapeEffects = new() { RoughnessFactor = roughness }
		} ) );
	}

	[TestMethod]
	public void MissingOpposingSurfaceUsesRoughSound()
	{
		var roughSound = new SoundEvent();
		var smoothSound = new SoundEvent();
		var surface = new Surface
		{
			SoundCollection = new() { ScrapeRough = roughSound, ScrapeSmooth = smoothSound }
		};

		Assert.AreSame( roughSound, surface.GetScrapeSound( null ) );
		Assert.AreSame( roughSound, surface.GetScrapeSound( int.MaxValue ) );
	}

	[TestMethod]
	public void AuthoredRoughThresholdControlsSelection()
	{
		var roughSound = new SoundEvent();
		var smoothSound = new SoundEvent();
		var surface = new Surface
		{
			ScrapeEffects = new() { RoughThreshold = 0.8f },
			SoundCollection = new() { ScrapeRough = roughSound, ScrapeSmooth = smoothSound }
		};
		Assert.AreSame( smoothSound, surface.GetScrapeSound( new Surface { ScrapeEffects = new() { RoughnessFactor = 0.7f } } ) );
		Assert.AreSame( roughSound, surface.GetScrapeSound( new Surface { ScrapeEffects = new() { RoughnessFactor = 0.8f } } ) );
		Assert.AreEqual( 1.0f, new Surface().ScrapeEffects.RoughnessFactor );
		Assert.AreEqual( 0.5f, new Surface().ScrapeEffects.RoughThreshold );
	}

	[TestMethod]
	[DataRow( "{}", 1.0f, 0.5f )]
	[DataRow( "{\"RoughnessFactor\":0}", 0.0f, 0.5f )]
	[DataRow( "{\"RoughnessFactor\":0,\"RoughThreshold\":0}", 0.0f, 0.0f )]
	public void ScrapeSettingsPreserveSerializedValues( string json, float roughness, float threshold )
	{
		var settings = System.Text.Json.JsonSerializer.Deserialize<Surface.ScrapeEffectData>( json );
		Assert.AreEqual( roughness, settings.RoughnessFactor );
		Assert.AreEqual( threshold, settings.RoughThreshold );
	}

	[TestMethod]
	[DataRow( true, 0.0f )]
	[DataRow( true, 1.0f )]
	[DataRow( false, 0.0f )]
	[DataRow( false, 1.0f )]
	public void MissingScrapeSlotFallsBackToOtherSlot( bool rough, float roughness )
	{
		var sound = new SoundEvent();
		var surface = new Surface
		{
			SoundCollection = rough ? new() { ScrapeRough = sound } : new() { ScrapeSmooth = sound }
		};

		Assert.AreSame( sound, surface.GetScrapeSound( new Surface { ScrapeEffects = new() { RoughnessFactor = roughness } } ) );
		Assert.AreSame( sound, surface.GetScrapeSound( int.MaxValue ) );
	}

	[TestMethod]
	public void SurfaceWithoutScrapeSoundsDoesNotPlay()
	{
		var surface = new Surface();
		Assert.IsFalse( surface.HasScrapeSounds );
		Assert.IsNull( surface.GetScrapeSound( new Surface() ) );
		Assert.IsNull( surface.GetScrapeSound( int.MaxValue ) );
	}

	[TestMethod]
	public void ScrapeEligibilityTracksSoundChanges()
	{
		var surface = new Surface();
		Assert.IsFalse( surface.HasScrapeSounds );
		surface.SoundCollection = new() { ScrapeSmooth = new SoundEvent() };
		Assert.IsTrue( surface.HasScrapeSounds );
		surface.SoundCollection = new() { ScrapeRough = new SoundEvent() };
		Assert.IsTrue( surface.HasScrapeSounds );
		surface.SoundCollection = default;
		Assert.IsFalse( surface.HasScrapeSounds );
	}

	[TestMethod]
	public void ContactLayoutMatchesNative()
	{
		Assert.AreEqual( 40, Marshal.SizeOf<PhysicsBody3d.ScrapeContact>() );
		Assert.AreEqual( 4, Marshal.OffsetOf<PhysicsBody3d.ScrapeContact>( "Point" ).ToInt32() );
		Assert.AreEqual( 16, Marshal.OffsetOf<PhysicsBody3d.ScrapeContact>( "Normal" ).ToInt32() );
		Assert.AreEqual( 28, Marshal.OffsetOf<PhysicsBody3d.ScrapeContact>( "FrictionForce" ).ToInt32() );
		Assert.AreEqual( 32, Marshal.OffsetOf<PhysicsBody3d.ScrapeContact>( "SelfSurfaceIndex" ).ToInt32() );
		Assert.AreEqual( 36, Marshal.OffsetOf<PhysicsBody3d.ScrapeContact>( "OtherSurfaceIndex" ).ToInt32() );
	}

	[TestMethod]
	public void UnassignedContactHasNoBodyOrSurface()
	{
		var contact = new PhysicsBody3d.ScrapeContact { OtherBodyIndex = -1, SelfSurfaceIndex = -1, OtherSurfaceIndex = -1 };
		Assert.IsNull( contact.OtherBody );
		Assert.IsNull( contact.Surface );
		contact.SelfSurfaceIndex = int.MaxValue;
		Assert.IsNull( contact.Surface );
	}
}
