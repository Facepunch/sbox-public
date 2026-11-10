using Sandbox.Rendering;
using System;

namespace SceneTests;

/// <summary>
/// Checks fog settings and constant-buffer packing independently of scene components.
/// </summary>
[TestClass]
public class FogSetupTests
{
	/// <summary>
	/// Sun lighting defaults on, has a finite half-intensity width, and clears the next buffer when disabled.
	/// </summary>
	[TestMethod]
	[DataRow( 0.1f )]
	[DataRow( 30f )]
	[DataRow( 90f )]
	[DataRow( 180f )]
	public void SunLightingUploadsAndClears( float lobeWidth )
	{
		var fog = new FogSetup();
		var constants = fog.ToConstants();
		Assert.AreEqual( 1f, constants.Sun.x );
		Assert.AreEqual( new Vector4( 0, 1, 0, 0 ), constants.SunSettings );

		fog.SunLightingEnabled = true;
		fog.SunIntensity = 12;
		fog.SunLobeWidth = lobeWidth;
		fog.SunStartDistance = 160000;
		fog.SunFadeDistance = 320000;
		fog.SunSaturation = 8;
		constants = fog.ToConstants( worldUnitsPerUnit: 16 );
		Assert.AreEqual( 12f, constants.Sun.x );
		Assert.AreEqual( new Vector4( 10000, 8, 20000, 0 ), constants.SunSettings );
		var halfIntensity = Math.Pow( 1 + constants.Sun.y * (1 - Math.Cos( lobeWidth * Math.PI / 180 )), -1.5 );
		Assert.AreEqual( 0.5, halfIntensity, 0.0001 );

		fog.SunIntensity = -1;
		fog.SunLobeWidth = 0;
		fog.SunStartDistance = -100;
		fog.SunFadeDistance = -1024;
		fog.SunSaturation = -1;
		constants = fog.ToConstants();
		Assert.AreEqual( -1f, constants.Sun.x );
		Assert.IsTrue( float.IsFinite( constants.Sun.y ) );
		Assert.AreEqual( new Vector4( -100, -1, -1024, 0 ), constants.SunSettings );
		fog.SunLightingEnabled = false;
		fog.SunIntensity = 2;
		constants = fog.ToConstants();
		Assert.AreEqual( 0f, constants.Sun.x );
	}

	/// <summary>
	/// Sky ramps clamp both mip endpoints, preserve encodings and scale with skybox views; disabling restores fixed blur.
	/// </summary>
	[TestMethod]
	[DataRow( 2f, -1f, FogSkyEncoding.Rgb, 4 )]
	[DataRow( -1f, 2f, FogSkyEncoding.YCoCg, 4 )]
	[DataRow( 2f, -1f, FogSkyEncoding.Rgbm, 4 )]
	[DataRow( 2f, -1f, FogSkyEncoding.Rgbm, 1 )]
	public void SkyBlurRampUploadsValidMips( float near, float far, FogSkyEncoding encoding, int mips )
	{
		using var cube = Texture.CreateCube( 8, 8 ).WithMips( mips ).WithData( new byte[mips == 1 ? 1536 : 2040] ).Finish();
		var fog = new FogSetup
		{
			SkyTexture = cube,
			HeightSkyTexture = cube,
			SkyEncoding = encoding,
			HeightSkyEncoding = encoding,
			SkyBlur = near,
			HeightSkyBlur = near,
			SkyBlurFar = far,
			SkyBlurDistanceEnabled = true,
			SkyBlurDistanceRange = new( 160, 480 )
		};
		var constants = fog.ToConstants( worldUnitsPerUnit: 16 );
		var maxMip = mips - 1;
		Assert.AreEqual( near.Clamp( 0, 1 ) * maxMip, constants.Sky.y );
		Assert.AreEqual( new Vector4( 10, 0.05f, far.Clamp( 0, 1 ) * maxMip, maxMip ), constants.SkyBlurRamp );
		Assert.AreEqual( (float)encoding, constants.SkyFormat.x );
		Assert.AreEqual( mips == 1 ? 1f : 0f, constants.HeightSkyShared );

		fog.SkyBlurDistanceRange = new( 160, 160 );
		constants = fog.ToConstants();
		Assert.AreEqual( 1000f, constants.SkyBlurRamp.y, 0.001f );

		fog.SkyBlurDistanceEnabled = false;
		constants = fog.ToConstants();
		Assert.AreEqual( constants.Sky.y, constants.SkyBlurRamp.z );
		Assert.AreEqual( 1f, constants.HeightSkyShared, "Fixed blur restores the shared height sample" );
	}

	/// <summary>
	/// Every positive skybox scale uses the same conversion as the native child view.
	/// </summary>
	[TestMethod]
	[DataRow( 16.0f )]
	[DataRow( 0.00001f )]
	public void PositiveSkyboxScalesMatchNativeConversion( float scale )
	{
		var fog = new FogSetup { StartDistance = 2, EndDistance = 4, AttenuationDistance = 8, BaseHeight = 3, HeightFalloff = 6, MaxDistance = 12 };
		var main = fog.ToConstants();
		var child = fog.ToConstants( scale, 100 );

		var distance = main.Distance;
		var height = main.Height;
		Assert.AreEqual( new Vector4( distance.x / scale, distance.y / scale, distance.z, distance.w / scale ), child.Distance );
		Assert.AreEqual( new Vector4( height.x / scale + 100, height.y / scale, height.z / scale, height.w ), child.Height );
	}

	/// <summary>
	/// A released cubemap must fall back to constant color even when its cached descriptor still identifies a cube.
	/// </summary>
	[TestMethod]
	public void DisposedSkyTextureDisablesSkyContribution()
	{
		using var texture = Texture.CreateCube( 1, 1 ).WithData( new byte[24] ).Finish();
		Assert.IsTrue( texture.Desc.IsCube );
		texture.Dispose();
		var constants = new FogSetup { SkyTexture = texture, SkyContribution = 1 }.ToConstants();
		var fallback = new FogSetup { SkyContribution = 1 }.ToConstants();

		Assert.AreEqual( fallback, constants, "Disposed cubemaps must produce the same constants as an absent cubemap" );
		Assert.AreEqual( -1, constants.SkyTextureIndex );
	}

	/// <summary>
	/// Directional sky rotation is uploaded in the shader's column-major convention.
	/// </summary>
	[TestMethod]
	public void SkyRotationUsesShaderMatrixConvention()
	{
		var rotation = Rotation.From( 20, 65, -15 );
		var constants = new FogSetup { SkyRotation = rotation }.ToConstants();

		var expected = Matrix.FromTransform( new Transform( Vector3.Zero, rotation, 1 ) ).Transpose();
		Assert.AreEqual( expected, constants.SkyTransform );
	}
	/// <summary>
	/// Packed constants distinguish explicit suppression from inheritance and scale all world distances consistently.
	/// </summary>
	[TestMethod]
	public void FogConstantsResetAndTransformWithSkyboxSpace()
	{
		var fog = new FogSetup
		{
			Enabled = true,
			Weight = 0.75f,
			MaximumOpacity = 0.8f,
			Mode = FogMode.Exponential,
			StartDistance = 160,
			EndDistance = 1600,
			AttenuationDistance = 800,
			FalloffExponent = 2,
			HeightEnabled = true,
			BaseHeight = 320,
			HeightFalloff = 480,
			MaxDistance = 3200,
			SkyAffect = 0.25f,
			Color = new Color( 0.2f, 0.4f, 0.6f, 0.1f ),
			SkyContribution = 1
		};

		var constants = fog.ToConstants( worldUnitsPerUnit: 16, heightOffset: 100 );

		Assert.AreEqual( new Vector4( 10, 100, 2, 50 ), constants.Distance );
		Assert.AreEqual( new Vector4( 120, 30, 200, 0.25f ), constants.Height );
		Assert.AreEqual( new Vector3( 0.2f, 0.4f, 0.6f ), constants.Color, "Color alpha must not multiply density or tint" );

		fog.Mode = FogMode.Distance;
		fog.DistanceMetric = FogDistanceMetric.ViewDepth;
		fog.StartDistance = -160;
		fog.Color = new Color( 2, 0.5f, 0.25f );
		fog.SecondaryColor = new Color( 0.25f, 1, 4 );
		fog.ColorScale = 2;
		fog.DirectionalColorEnabled = true;
		fog.ColorDirection = Vector3.Forward;
		constants = fog.ToConstants( 16, 100, Vector3.Forward );
		Assert.AreEqual( -10f, constants.Distance.x );
		Assert.AreEqual( 1f, constants.DistanceMetric );
		Assert.AreEqual( new Vector3( 4, 1, 0.5f ), constants.Color );
		constants = fog.ToConstants( viewDirection: Vector3.Backward );
		Assert.AreEqual( new Vector3( 0.5f, 2, 8 ), constants.Color );
		constants = fog.ToConstants( viewDirection: Vector3.Left );
		Assert.AreEqual( new Vector3( 2.25f, 1.5f, 4.25f ), constants.Color, "Directional colors blend and scale in linear space, preserving HDR values" );

		fog.EndDistance = -320;
		constants = fog.ToConstants( 16 );
		Assert.AreEqual( new Vector2( -10, -20 ), new Vector2( constants.Distance.x, constants.Distance.y ), "Distance ramps retain signed and reversed endpoints" );
		fog.Mode = FogMode.Exponential;
		constants = fog.ToConstants( viewDirection: Vector3.Left );
		Assert.AreEqual( 0f, constants.Distance.x, "Exponential fog still clamps its clear distance" );
		Assert.AreEqual( new Vector3( 2.25f, 1.5f, 4.25f ), constants.Color, "Color blending is independent of attenuation" );
		fog.DirectionalColorEnabled = false;
		constants = fog.ToConstants();
		Assert.AreEqual( new Vector3( 4, 1, 0.5f ), constants.Color, "Linear color scaling retains HDR values" );

		fog.Enabled = false;
		constants = fog.ToConstants();
		var suppression = constants.Control;
		Assert.AreEqual( 0.75f, suppression.x );
		Assert.AreEqual( 0.0f, suppression.y );

		constants = default( FogSetup ).ToConstants();
		Assert.AreEqual( 0.0f, constants.Control.x, "The next camera inherits legacy fog instead of leaking the preceding override" );
	}
	/// <summary>
	/// Both a plain cube and an actual two-cube array use their matching bindless resource dimension.
	/// </summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void ProceduralSkyUsesMatchingResourceDimension( int cubeCount )
	{
		var builder = Texture.CreateCube( 1, 1 );
		builder.Depth = cubeCount;
		using var texture = builder.WithData( new byte[cubeCount * 24] ).Finish();
		Assert.IsTrue( texture.Desc.IsCube );
		Assert.AreEqual( cubeCount > 1, texture.Desc.IsArray, "The fixture must allocate an actual cube array rather than rely on the format of a shipped asset" );

		var constants = new FogSetup { Enabled = true, Weight = 1, SkyTexture = texture, SkyContribution = 1 }.ToConstants();

		var expectedIndex = g_pRenderDevice.GetTextureViewIndex( texture.native,
			(byte)NativeEngine.RenderColorSpace.RENDER_SRGB, NativeEngine.RenderTextureDimension.RENDER_TEXTURE_DIMENSION_INVALID );
		Assert.AreEqual( expectedIndex, constants.SkyTextureIndex );
		Assert.AreEqual( cubeCount > 1 ? 1f : 0f, constants.SkyFormat.y );
	}
	/// <summary>
	/// Public construction creates a usable override while default values and explicit zero weight preserve legacy fog.
	/// </summary>
	[TestMethod]
	public void PublicFogSetupDefaultsDifferFromLegacySentinel()
	{
		var fog = new FogSetup { Color = Color.Red };
		var constants = fog.ToConstants();
		var control = constants.Control;
		Assert.IsTrue( fog.Enabled );
		Assert.AreEqual( 1.0f, control.x, "A caller providing only a color receives a full-weight override" );
		Assert.AreEqual( 1.0f, control.y );
		Assert.AreEqual( (float)FogMode.Exponential, control.z );
		Assert.AreEqual( FogDistanceMetric.Radial, fog.DistanceMetric );
		Assert.AreEqual( 0f, constants.DistanceMetric );
		Assert.AreEqual( 2000.0f, constants.Distance.w );
		Assert.AreEqual( new Vector3( 1, 0, 0 ), constants.Color );

		fog.Weight = 0;
		constants = fog.ToConstants();
		Assert.AreEqual( 0.0f, constants.Control.x, "Explicit zero influence must not be coerced to full weight" );

		constants = default( FogSetup ).ToConstants();
		Assert.AreEqual( 0.0f, constants.Control.x, "The zero-initialized render sentinel continues to inherit legacy fog" );
		Assert.AreEqual( 0.0f, constants.Control.y );
	}
	/// <summary>
	/// Sanitizing tiny world distances before conversion keeps managed skybox constants equivalent to native scaled constants.
	/// </summary>
	[TestMethod]
	[DataRow( 0.0f )]
	[DataRow( 0.001f )]
	[DataRow( 0.01f )]
	public void SmallFogDistancesScaleAfterSanitizing( float distance )
	{
		var fog = new FogSetup
		{
			Enabled = true,
			Weight = 1,
			AttenuationDistance = distance,
			HeightFalloff = distance,
			MaxDistance = distance
		};
		var main = fog.ToConstants();
		var skybox = fog.ToConstants( worldUnitsPerUnit: 16 );

		Assert.AreEqual( main.Distance.w / 16, skybox.Distance.w, 0.0000001f );
		Assert.AreEqual( main.Height.y / 16, skybox.Height.y, 0.0000001f );
		Assert.AreEqual( main.Height.z / 16, skybox.Height.z, 0.0000001f );
	}
}
