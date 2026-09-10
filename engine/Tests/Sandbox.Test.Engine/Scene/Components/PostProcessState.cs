using Sandbox.Volumes;
using System.Text.Json.Nodes;

namespace SceneTests.Components;

/// <summary>
/// Headless coverage for the post-processing component state that doesn't need
/// a renderer: PostProcessVolume's blending math and VolumeSystem lookups, plus
/// the two effects whose types are free of static shader/material initializers
/// (MotionBlur, BlitOverlay) and the pure HighlightOutline marker component.
/// The other effects (Bloom, Vignette, Sharpen, ...) hold static
/// Material.FromShader fields, so even instantiating them risks loading
/// resources without a booted engine - they are covered in the integration tier.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PostProcessStateTest : SceneTest
{
	/// <summary>
	/// PostProcessVolume's defaults match the source initializers: priority 0,
	/// full blend weight, a 50 unit blend distance, editor preview on, and the
	/// VolumeComponent default of a 100 unit box volume.
	/// </summary>
	[TestMethod]
	public void VolumeDefaultsMatchSource()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();

		Assert.AreEqual( 0, volume.Priority );
		Assert.AreEqual( 1.0f, volume.BlendWeight );
		Assert.AreEqual( 50.0f, volume.BlendDistance );
		Assert.IsTrue( volume.EditorPreview );

		Assert.AreEqual( SceneVolume.VolumeTypes.Box, volume.SceneVolume.Type );
		Assert.AreEqual( BBox.FromPositionAndSize( 0, 100 ), volume.SceneVolume.Box );
		Assert.IsFalse( volume.IsInfinite );
	}

	/// <summary>
	/// GetWeight remaps the distance to the volume's edge into 0..BlendWeight
	/// over BlendDistance: at the center of the default box (50 units from every
	/// face, exactly the default BlendDistance) it returns the full blend weight,
	/// on the surface it returns 0, and an infinite volume always returns the
	/// blend weight no matter the position.
	/// </summary>
	[TestMethod]
	public void VolumeWeightBlending()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();

		Assert.AreEqual( 1.0f, volume.GetWeight( Vector3.Zero ), 0.0001f );
		Assert.AreEqual( 0.0f, volume.GetWeight( new Vector3( 0, 0, 50 ) ), 0.0001f );

		volume.BlendWeight = 0.5f;
		Assert.AreEqual( 0.5f, volume.GetWeight( Vector3.Zero ), 0.0001f );

		volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		Assert.IsTrue( volume.IsInfinite );
		Assert.AreEqual( 0.5f, volume.GetWeight( new Vector3( 99999, 0, 0 ) ), 0.0001f );
	}

	/// <summary>
	/// The scene VolumeSystem finds an enabled PostProcessVolume by position -
	/// inside the default box but not outside it - and stops finding it once the
	/// component is disabled. Ticking a camera-less scene with the volume enabled
	/// is safe because PostProcessSystem only acts on cameras.
	/// </summary>
	[TestMethod]
	public void VolumeSystemLookup()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();

		Assert.AreSame( volume, scene.Volumes.FindSingle<PostProcessVolume>( Vector3.Zero ) );
		Assert.IsNull( scene.Volumes.FindSingle<PostProcessVolume>( new Vector3( 500, 0, 0 ) ) );

		volume.Enabled = false;
		Assert.IsNull( scene.Volumes.FindSingle<PostProcessVolume>( Vector3.Zero ) );

		volume.Enabled = true;
		Assert.AreSame( volume, scene.Volumes.FindSingle<PostProcessVolume>( Vector3.Zero ) );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( volume.IsValid );
	}

	/// <summary>
	/// PostProcessVolume's blending configuration and volume shape survive the
	/// component serialize/deserialize round trip into a new scene.
	/// </summary>
	[TestMethod]
	public void VolumeRoundTrip()
	{
		JsonObject json;

		var sceneA = new Scene();
		using ( sceneA.Push() )
		{
			var go = sceneA.CreateObject();
			var volume = go.Components.Create<PostProcessVolume>();
			volume.Priority = 3;
			volume.BlendWeight = 0.25f;
			volume.BlendDistance = 100.0f;
			volume.EditorPreview = false;
			volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Sphere, Sphere = new Sphere( 0, 200 ) };

			json = (JsonObject)volume.Serialize();
		}

		var sceneB = new Scene();
		using ( sceneB.Push() )
		{
			var go = sceneB.CreateObject();
			var copy = go.Components.Create<PostProcessVolume>( false );
			copy.DeserializeImmediately( json );

			Assert.AreEqual( 3, copy.Priority );
			Assert.AreEqual( 0.25f, copy.BlendWeight );
			Assert.AreEqual( 100.0f, copy.BlendDistance );
			Assert.IsFalse( copy.EditorPreview );
			Assert.AreEqual( SceneVolume.VolumeTypes.Sphere, copy.SceneVolume.Type );
			Assert.AreEqual( 200.0f, copy.SceneVolume.Sphere.Radius );
		}
	}

	/// <summary>
	/// MotionBlur is one of the few effects without static resource fields, so
	/// it can exist headless: the Scale default matches the source, and a
	/// non-default value survives the round trip into a new scene where the
	/// copy comes back enabled.
	/// </summary>
	[TestMethod]
	public void MotionBlurDefaultsAndRoundTrip()
	{
		JsonObject json;

		var sceneA = new Scene();
		using ( sceneA.Push() )
		{
			var go = sceneA.CreateObject();
			var blur = go.Components.Create<MotionBlur>();

			Assert.AreEqual( 0.05f, blur.Scale );

			blur.Scale = 0.4f;
			json = (JsonObject)blur.Serialize();
		}

		var sceneB = new Scene();
		using ( sceneB.Push() )
		{
			var go = sceneB.CreateObject();
			var copy = go.Components.Create<MotionBlur>( false );
			copy.DeserializeImmediately( json );

			Assert.IsTrue( copy.Enabled );
			Assert.AreEqual( 0.4f, copy.Scale );
		}
	}

	/// <summary>
	/// BlitOverlay's defaults match the source initializers (a 0.1 blend, normal
	/// blend mode, no material), and its blend configuration survives the round
	/// trip into a new scene with the material reference staying null.
	/// </summary>
	[TestMethod]
	public void BlitOverlayDefaultsAndRoundTrip()
	{
		JsonObject json;

		var sceneA = new Scene();
		using ( sceneA.Push() )
		{
			var go = sceneA.CreateObject();
			var overlay = go.Components.Create<BlitOverlay>();

			Assert.AreEqual( 0.1f, overlay.Blend );
			Assert.AreEqual( BlendMode.Normal, overlay.BlendMode );
			Assert.IsNull( overlay.Material );
			Assert.AreEqual( 0, overlay.Order );

			overlay.Blend = 0.9f;
			overlay.BlendMode = BlendMode.Multiply;
			overlay.Order = 7;

			json = (JsonObject)overlay.Serialize();
		}

		var sceneB = new Scene();
		using ( sceneB.Push() )
		{
			var go = sceneB.CreateObject();
			var copy = go.Components.Create<BlitOverlay>( false );
			copy.DeserializeImmediately( json );

			Assert.AreEqual( 0.9f, copy.Blend );
			Assert.AreEqual( BlendMode.Multiply, copy.BlendMode );
			Assert.AreEqual( 7, copy.Order );
			Assert.IsNull( copy.Material );
		}
	}

	/// <summary>
	/// Effects register in the scene's object index under their BasePostProcess
	/// base while enabled, so the family lookups see them: the scene-wide
	/// enabled-only query, the FindMode-based component query that also includes
	/// disabled ones, and ticking a camera-less scene leaves them untouched.
	/// </summary>
	[TestMethod]
	public void EffectFamilyLookup()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var blur = go.Components.Create<MotionBlur>();
		var overlay = go.Components.Create<BlitOverlay>();

		Assert.AreEqual( 2, scene.GetAllComponents<BasePostProcess>().Count() );
		Assert.AreEqual( 2, go.Components.GetAll<BasePostProcess>( FindMode.EverythingInSelf ).Count() );

		overlay.Enabled = false;
		Assert.AreSame( blur, scene.GetAllComponents<BasePostProcess>().Single() );
		Assert.AreEqual( 2, go.Components.GetAll<BasePostProcess>( FindMode.EverythingInSelf ).Count() );

		overlay.Enabled = true;

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( blur.IsValid );
		Assert.IsTrue( overlay.IsValid );
		Assert.AreEqual( 2, scene.GetAllComponents<BasePostProcess>().Count() );
	}

	/// <summary>
	/// HighlightOutline is a pure marker component for the Highlight effect: its
	/// color and width defaults match the source, GetOutlineTargets is empty both
	/// with manual override (null target list) and in automatic mode when no
	/// renderers exist, and its configuration survives a round trip.
	/// </summary>
	[TestMethod]
	public void HighlightOutlineStateAndTargets()
	{
		JsonObject json;

		var sceneA = new Scene();
		using ( sceneA.Push() )
		{
			var go = sceneA.CreateObject();
			var outline = go.Components.Create<HighlightOutline>();

			Assert.AreEqual( Color.White, outline.Color );
			Assert.AreEqual( Color.Black * 0.4f, outline.ObscuredColor );
			Assert.AreEqual( Color.Transparent, outline.InsideColor );
			Assert.AreEqual( Color.Transparent, outline.InsideObscuredColor );
			Assert.AreEqual( 0.25f, outline.Width );
			Assert.IsFalse( outline.OverrideTargets );
			Assert.IsNull( outline.Material );

			// Automatic mode with no renderers in the hierarchy
			Assert.AreEqual( 0, outline.GetOutlineTargets().Count() );

			// Manual mode with no target list assigned
			outline.OverrideTargets = true;
			Assert.AreEqual( 0, outline.GetOutlineTargets().Count() );

			outline.Width = 0.5f;
			outline.Color = new Color( 1.0f, 0.5f, 0.0f, 1.0f );

			json = (JsonObject)outline.Serialize();
		}

		var sceneB = new Scene();
		using ( sceneB.Push() )
		{
			var go = sceneB.CreateObject();
			var copy = go.Components.Create<HighlightOutline>( false );
			copy.DeserializeImmediately( json );

			Assert.AreEqual( 0.5f, copy.Width );
			Assert.AreEqual( new Color( 1.0f, 0.5f, 0.0f, 1.0f ), copy.Color );
			Assert.IsTrue( copy.OverrideTargets );
		}
	}
}
