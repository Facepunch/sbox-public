using System;
using System.Reflection;

namespace SceneTests;

/// <summary>
/// Lights keep their description in managed code and Borealis packs it (<c>LightPacking</c>) exactly as native's light
/// binner packs the native light's (<c>RenderTools.PackSceneLight</c>), for each kind of light and the settings maps use.
/// </summary>
[TestClass]
public class LightPackingTests
{
	static readonly Type Packing = Type.GetType( "Sandbox.Rendering.Bridge.LightPacking, Sandbox.Rendering", true );
	static readonly Type Native = Type.GetType( "Sandbox.Rendering.Bridge.SceneNative, Sandbox.Rendering", true );

	static (object Packed, uint Flags, int BakeIndex) Managed( SceneLight light )
	{
		object[] args = [light, null, 0u, 0];
		var method = Packing.GetMethods( BindingFlags.Static | BindingFlags.Public ).Single( m => m.Name == "Pack" && m.GetParameters()[0].ParameterType == typeof( SceneLight ) );
		method.Invoke( null, args );
		return (args[1], (uint)args[2], (int)args[3]);
	}

	static (object Packed, uint Flags, int BakeIndex) FromNative( SceneLight light )
	{
		var method = Native.GetMethod( "PackSceneLight", BindingFlags.Static | BindingFlags.Public );
		object[] args = [light, null, 0u, 0];
		Assert.IsTrue( (bool)method.Invoke( null, args ) );
		return (args[1], (uint)args[2], (int)args[3]);
	}

	static void AssertSame( SceneLight light, string what )
	{
		var managed = Managed( light );
		var native = FromNative( light );

		// The flags and bake index exactly; the optimisation bits native keeps (attenuation present, derived values) too
		Assert.AreEqual( native.Flags, managed.Flags, $"{what}: flags" );
		Assert.AreEqual( native.BakeIndex, managed.BakeIndex, $"{what}: bake index" );

		foreach ( var field in native.Packed.GetType().GetFields( BindingFlags.Instance | BindingFlags.Public ) )
		{
			var a = field.GetValue( native.Packed );
			var b = field.GetValue( managed.Packed );
			if ( field.Name == "LightCookieTextureIndex" ) continue; // native's is Source 2's bindless slot
			if ( a is float fa ) AssertClose( fa, (float)b, $"{what}: {field.Name}" );
			else if ( a is Vector2 va ) { AssertClose( va.x, ((Vector2)b).x, $"{what}: {field.Name}.x" ); AssertClose( va.y, ((Vector2)b).y, $"{what}: {field.Name}.y" ); }
			else if ( a is Vector3 v3 ) { var m = (Vector3)b; AssertClose( v3.x, m.x, $"{what}: {field.Name}.x" ); AssertClose( v3.y, m.y, $"{what}: {field.Name}.y" ); AssertClose( v3.z, m.z, $"{what}: {field.Name}.z" ); }
			else if ( a is Vector4 v4 ) { var m = (Vector4)b; AssertClose( v4.x, m.x, $"{what}: {field.Name}.x" ); AssertClose( v4.y, m.y, $"{what}: {field.Name}.y" ); AssertClose( v4.z, m.z, $"{what}: {field.Name}.z" ); AssertClose( v4.w, m.w, $"{what}: {field.Name}.w" ); }
			else Assert.AreEqual( a, b, $"{what}: {field.Name}" );
		}
	}

	static void AssertClose( float expected, float actual, string what )
	{
		var tolerance = MathF.Max( 1e-5f, MathF.Abs( expected ) * 1e-5f );
		Assert.IsTrue( MathF.Abs( expected - actual ) <= tolerance || expected == actual, $"{what}: native {expected}, managed {actual}" );
	}

	[TestMethod]
	public void PackingMatchesNative()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = true;
		try
		{
			var point = new ScenePointLight( world, new Vector3( 10, 20, 30 ), 200, Color.Red * 5 );
			AssertSame( point, "new point light" );
			point.Transform = new Transform( new Vector3( -50, 5, 70 ), Rotation.From( 10, 40, 5 ) );
			point.LinearAttenuation = 0.5f;
			point.ConstantAttenuation = 0;
			point.FogLighting = SceneLight.FogLightingMode.Dynamic;
			point.FogStrength = 0.5f;
			point.Shape = SceneLight.LightShape.Capsule;
			point.ShapeSize = new Vector2( 10, 20 );
			point.RenderSpecular = false;
			AssertSame( point, "capsule point light" );

			var spot = new SceneSpotLight( world, new Vector3( 1, 2, 3 ), Color.White * 10 );
			AssertSame( spot, "new spot light" );
			spot.Transform = new Transform( new Vector3( 100, 0, 50 ), Rotation.From( 45, 90, 0 ) );
			spot.ConeInner = 20;
			spot.ConeOuter = 40;
			spot.FallOff = 2;
			spot.Radius = 500;
			spot.ShadowsEnabled = false;
			spot.SetBakedLightIndex( 3, true );
			spot.FogLighting = SceneLight.FogLightingMode.DynamicNoShadows;
			AssertSame( spot, "baked spot light" );

			var sun = new SceneDirectionalLight( world, Rotation.From( 60, 30, 0 ), Color.White * 3 );
			AssertSame( sun, "new directional light" );
			sun.SetBakedLightIndex( 1 );
			sun.ShadowCascadeCount = 2;
			sun.SetWorldDirection( Rotation.From( 50, 120, 0 ) );
			AssertSame( sun, "baked directional light" );

			var legacy = new ScenePointLight( world, Vector3.Zero, 300, Color.Blue );
			legacy.ApplyLegacy( new Light.LegacyLightData
			{
				DirectLight = 3,
				BakeLightIndex = 7,
				BakeLightIndexScale = 2,
				BakedLightIndexing = true,
				FogContributionStrength = 0.25f,
				RenderDiffuse = true,
				RenderSpecular = true,
				FogLighting = 2,
				ConstantAttenuation = 1,
				LinearAttenuation = 0.1f,
				FallOff = 3,
			} );
			AssertSame( legacy, "a map's stationary light" );
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}

	/// <summary>
	/// Without anything reading native lights, changes reach the packed light and leave native's description as it was.
	/// </summary>
	[TestMethod]
	public void ChangesReachThePackWithoutNativeWrites()
	{
		var world = new SceneWorld();
		try
		{
			SceneObject.WritesNativeOverride = true;
			var light = new ScenePointLight( world, Vector3.Zero, 100, Color.White );

			SceneObject.WritesNativeOverride = false;
			light.LightColor = Color.Green;
			light.FogStrength = 0.75f;
			light.Radius = 250;

			var packed = Managed( light ).Packed;
			Assert.AreEqual( (Vector3)Color.Green, (Vector3)packed.GetType().GetField( "Color" ).GetValue( packed ) );
			Assert.AreEqual( 250.0f, (float)packed.GetType().GetField( "Radius" ).GetValue( packed ) );
			Assert.AreEqual( Color.Green, light.LightColor );
			Assert.AreEqual( 0.75f, light.FogStrength );
			Assert.AreEqual( new Vector3( 1, 1, 1 ), light.lightNative.GetColor(), "the native light isn't written" );
			Assert.AreEqual( 250.0f, light.lightNative.GetRadius(), "its range is: its bounds come from it" );
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}
}
