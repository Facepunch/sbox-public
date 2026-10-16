using NativeEngine;
using System;

namespace SceneTests.Components;

[TestClass]
public class LightProbeVolumeTests
{
	Scene scene;

	[TestInitialize]
	public void Initialize() => scene = new Scene();

	[TestCleanup]
	public void Cleanup() => scene.Destroy();

	static SceneLightProbe FindProbe( Scene scene, LightProbeVolume component )
		=> scene.SceneWorld.SceneObjects.OfType<SceneLightProbe>().SingleOrDefault( x => x.Component == component );

	static void AssertTexture( Texture expected, ITexture handle )
	{
		try
		{
			Assert.AreEqual( expected.native.GetBindingPtr(), handle.GetBindingPtr() );
		}
		finally
		{
			handle.DestroyStrongHandle();
		}
	}

	[TestMethod]
	public void Lifecycle()
	{
		using var scope = scene.Push();
		using var texture = Texture.CreateVolume( 2, 2, 12 ).Finish();
		var go = scene.CreateObject();
		var component = go.Components.Create<LightProbeVolume>();

		Assert.IsNull( FindProbe( scene, component ) );
		component.Texture = texture;
		var first = FindProbe( scene, component );
		Assert.IsNotNull( first );
		Assert.AreEqual( component.Bounds.Mins, ((CSceneLightProbeVolumeObject)first.native).m_vBoxMins );
		Assert.AreEqual( component.Bounds.Maxs, ((CSceneLightProbeVolumeObject)first.native).m_vBoxMaxs );

		component.Enabled = false;
		Assert.IsFalse( first.IsValid() );
		Assert.IsNull( FindProbe( scene, component ) );
		go.WorldPosition = new Vector3( 100, 200, 300 );
		component.Priority = 2;
		Assert.IsNull( FindProbe( scene, component ) );

		component.Enabled = true;
		var second = FindProbe( scene, component );
		Assert.IsNotNull( second );
		Assert.AreEqual( go.WorldPosition, second.Position );
		Assert.AreEqual( 2, ((CSceneLightProbeVolumeObject)second.native).m_nRenderPriority );

		component.Texture = null;
		Assert.IsFalse( second.IsValid() );
		Assert.IsNull( FindProbe( scene, component ) );

		component.Texture = texture;
		var third = FindProbe( scene, component );
		go.Destroy();
		scene.ProcessDeletes();
		Assert.IsFalse( third.IsValid() );
	}

	[TestMethod]
	public void SettingsTransformAndTags()
	{
		using var scope = scene.Push();
		using var texture = Texture.CreateVolume( 2, 2, 12 ).Finish();
		var parent = scene.CreateObject();
		var go = scene.CreateObject();
		go.SetParent( parent );
		var component = go.Components.Create<LightProbeVolume>();
		component.Texture = texture;
		var first = FindProbe( scene, component );

		component.Bounds = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 40, 50, 60 ) );
		component.Priority = -2;
		component.Handshake = 123;
		parent.WorldTransform = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromYaw( 90 ), 2 );
		scene.GameTick();
		go.Tags.Add( "probe_test" );

		var current = FindProbe( scene, component );
		Assert.IsFalse( first.IsValid() );
		Assert.AreEqual( go.WorldTransform.WithScale( 1 ), current.Transform );
		Assert.IsTrue( current.Tags.Has( "probe_test" ) );
		var native = (CSceneLightProbeVolumeObject)current.native;
		Assert.AreEqual( component.Bounds.Mins, native.m_vBoxMins );
		Assert.AreEqual( component.Bounds.Maxs, native.m_vBoxMaxs );
		Assert.AreEqual( -2, native.m_nRenderPriority );
		Assert.AreEqual( 123, native.m_nHandshake );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void OptionalDirectLightTextures( bool indices, bool scalars )
	{
		using var scope = scene.Push();
		using var texture = Texture.CreateVolume( 2, 2, 12 ).Finish();
		using var indicesTexture = Texture.CreateVolume( 2, 2, 2 ).Finish();
		using var scalarsTexture = Texture.CreateVolume( 2, 2, 2 ).Finish();
		var component = scene.CreateObject().Components.Create<LightProbeVolume>();
		component.IndicesTexture = indices ? indicesTexture : null;
		component.ScalarsTexture = scalars ? scalarsTexture : null;
		component.Texture = texture;

		var probe = FindProbe( scene, component );
		Assert.IsNotNull( probe );
		var native = (CSceneLightProbeVolumeObject)probe.native;
		AssertTexture( texture, native.m_hLightProbeTexture );
		if ( indices )
			AssertTexture( indicesTexture, native.m_hLightProbeDirectLightIndicesTexture );
		if ( scalars )
			AssertTexture( scalarsTexture, native.m_hLightProbeDirectLightScalarsTexture );

		component.IndicesTexture = null;
		component.ScalarsTexture = null;
		if ( indices || scalars )
			Assert.IsFalse( probe.IsValid() );
		Assert.IsNotNull( FindProbe( scene, component ) );
	}

	[TestMethod]
	public void InvalidBoundsCanBeCorrected()
	{
		using var scope = scene.Push();
		using var texture = Texture.CreateVolume( 2, 2, 12 ).Finish();
		var component = scene.CreateObject().Components.Create<LightProbeVolume>();
		component.Texture = texture;
		var first = FindProbe( scene, component );
		component.Bounds = new BBox( Vector3.Zero, Vector3.Zero );
		Assert.IsFalse( first.IsValid() );
		Assert.IsNull( FindProbe( scene, component ) );
		component.Bounds = new BBox( new Vector3( -72 ), new Vector3( 72 ) );
		Assert.IsNotNull( FindProbe( scene, component ) );
	}

	[TestMethod]
	public void SerializationRoundTrip()
	{
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var component = go.Components.Create<LightProbeVolume>( false );
		component.Bounds = new BBox( new Vector3( -256 ), new Vector3( 512 ) );
		component.Priority = -1;
		component.Handshake = 456;
		var json = go.Serialize();
		go.Destroy();
		scene.ProcessDeletes();
		SceneUtility.MakeIdGuidsUnique( json );
		var clone = new GameObject( false );
		clone.Deserialize( json );
		clone.Enabled = true;
		var loaded = clone.Components.Get<LightProbeVolume>( true );

		Assert.IsNotNull( loaded );
		Assert.IsFalse( loaded.Enabled );
		Assert.AreEqual( new BBox( new Vector3( -256 ), new Vector3( 512 ) ), loaded.Bounds );
		Assert.AreEqual( -1, loaded.Priority );
		Assert.AreEqual( 456, loaded.Handshake );
		Assert.IsNull( loaded.Texture );
		Assert.IsNull( loaded.IndicesTexture );
		Assert.IsNull( loaded.ScalarsTexture );
		loaded.Enabled = true;
		Assert.IsNull( FindProbe( scene, loaded ) );
	}

	[TestMethod]
	public void NamedTexturesSurviveSerialization()
	{
		using var scope = scene.Push();
		string name = $"test_lightprobe_{Guid.NewGuid():N}";
		using var ambient = Texture.CreateVolume( 2, 3, 24, ImageFormat.RGBA16161616F ).WithName( $"{name}_ambient" ).Finish();
		using var indices = Texture.CreateVolume( 2, 3, 4, ImageFormat.RGBA8888 ).WithName( $"{name}_indices" ).Finish();
		using var scalars = Texture.CreateVolume( 2, 3, 4, ImageFormat.RGBA16161616F ).WithName( $"{name}_scalars" ).Finish();
		var go = scene.CreateObject();
		var component = go.Components.Create<LightProbeVolume>();
		component.Texture = ambient;
		component.IndicesTexture = indices;
		component.ScalarsTexture = scalars;
		var json = go.Serialize();
		go.Destroy();
		scene.ProcessDeletes();
		SceneUtility.MakeIdGuidsUnique( json );
		var clone = new GameObject( false );
		clone.Deserialize( json );
		clone.Enabled = true;
		var loaded = clone.Components.Get<LightProbeVolume>();
		Assert.IsNotNull( loaded );
		var native = (CSceneLightProbeVolumeObject)FindProbe( scene, loaded ).native;
		AssertTexture( ambient, native.m_hLightProbeTexture );
		AssertTexture( indices, native.m_hLightProbeDirectLightIndicesTexture );
		AssertTexture( scalars, native.m_hLightProbeDirectLightScalarsTexture );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void BakedLightIndexLifecycleAndSerialization( bool directional, bool mixed )
	{
		using var scope = scene.Push();
		var go = scene.CreateObject();
		Light light = directional ? go.Components.Create<DirectionalLight>() : go.Components.Create<PointLight>();
		light.MixedShadows = mixed;
		light.BakeLightIndex = 254;
		var native = light.SceneObject.lightNative;
		Assert.AreNotEqual( 0u, native.GetLightFlags() & 32u );
		Assert.AreEqual( mixed ? 16u : 0u, native.GetLightFlags() & 16u );
		light.Enabled = false;
		light.Enabled = true;
		Assert.AreNotEqual( 0u, light.SceneObject.lightNative.GetLightFlags() & 32u );
		Assert.AreEqual( mixed ? 16u : 0u, light.SceneObject.lightNative.GetLightFlags() & 16u );
		var json = go.Serialize();
		go.Destroy();
		scene.ProcessDeletes();
		SceneUtility.MakeIdGuidsUnique( json );
		var clone = new GameObject( false );
		clone.Deserialize( json );
		clone.Enabled = true;
		var loaded = clone.Components.Get<Light>();
		Assert.AreEqual( 254, loaded.BakeLightIndex );
		Assert.AreEqual( mixed, loaded.MixedShadows );
		Assert.AreNotEqual( 0u, loaded.SceneObject.lightNative.GetLightFlags() & 32u );
		Assert.AreEqual( mixed ? 16u : 0u, loaded.SceneObject.lightNative.GetLightFlags() & 16u );
		loaded.MixedShadows = !mixed;
		Assert.AreEqual( mixed ? 0u : 16u, loaded.SceneObject.lightNative.GetLightFlags() & 16u );
		loaded.BakeLightIndex = -1;
		Assert.AreEqual( 0u, loaded.SceneObject.lightNative.GetLightFlags() & 32u );
		Assert.AreEqual( 0u, loaded.SceneObject.lightNative.GetLightFlags() & 16u );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => loaded.BakeLightIndex = 255 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => loaded.BakeLightIndex = -2 );
		Assert.AreEqual( -1, loaded.BakeLightIndex );
	}

	[TestMethod]
	public void SourcePointAttenuationSurvivesSerialization()
	{
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var light = go.Components.Create<PointLight>();
		light.ConstantAttenuation = 1;
		light.LinearAttenuation = 2;
		light.Attenuation = 30000;
		var json = go.Serialize();
		go.Destroy();
		scene.ProcessDeletes();
		SceneUtility.MakeIdGuidsUnique( json );
		var clone = new GameObject( false );
		clone.Deserialize( json );
		clone.Enabled = true;
		var loaded = clone.Components.Get<PointLight>();
		Assert.AreEqual( 1f, loaded.SceneObject.ConstantAttenuation );
		Assert.AreEqual( 2f, loaded.SceneObject.LinearAttenuation );
		Assert.AreEqual( 30000f, loaded.SceneObject.QuadraticAttenuation );
	}
}
