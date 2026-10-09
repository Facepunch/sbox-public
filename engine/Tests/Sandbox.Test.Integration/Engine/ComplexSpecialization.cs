using NativeEngine;
using System;
using System.IO;

namespace EngineTests;

/// <summary>Opt-in rendered comparison across two compiler runs; see the experiment README.</summary>
[TestClass]
public class ComplexSpecializationTest
{
	[TestMethod]
	[DataRow( "F_USE_BENT_NORMALS", 25, true )]
	[DataRow( "F_SCALE_NORMAL_MAP", 21, true )]
	[DataRow( "F_CLOTH_SHADING", 3, true )]
	[DataRow( "F_ENABLE_NORMAL_SELF_SHADOW", 3, false )]
	[DataRow( "F_USE_BENT_NORMALS+F_CLOTH_SHADING", 25, true )]
	[DataRow( "F_SCALE_NORMAL_MAP+F_ENABLE_NORMAL_SELF_SHADOW", 21, true )]
	[DataRow( "F_CLOTH_SHADING+F_ENABLE_NORMAL_SELF_SHADOW", 3, true )]
	[DataRow( "F_DETAIL_TEXTURE", 10, true )]
	[DataRow( "F_DETAIL_TEXTURE", 21, true )]
	[DataRow( "F_TRANSMISSIVE_BACKFACE_NDOTL", 0, true )]
	[DataRow( "F_RENDER_BACKFACES", 21, false )]
	[DataRow( "F_RENDER_BACKFACES", -21, true )]
	public void RenderSpecializedVariants( string feature, int debugMode, bool requireVisibleDifference )
	{
		var directory = Environment.GetEnvironmentVariable( "SBOX_COMPLEX_SPECIALIZATION_TEST_DIR" );
		if ( string.IsNullOrEmpty( directory ) ) Assert.Inconclusive( "Set SBOX_COMPLEX_SPECIALIZATION_TEST_DIR to an existing capture directory." );
		Assert.IsTrue( Directory.Exists( directory ) );
		Assert.AreEqual( RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN, g_pRenderDevice.GetRenderDeviceAPI() );
		bool capture = Environment.GetEnvironmentVariable( "SBOX_COMPLEX_SPECIALIZATION_CAPTURE" ) == "1";
		var scene = new Scene();
		using var scope = scene.Push();
		using var bentTexture = Texture.Create( 1, 1 ).WithData( new byte[] { 170, 128, 0, 255 } ).Finish();
		using var normalTexture = Texture.Create( 1, 1 ).WithData( new byte[] { 155, 120, 150, 255 } ).Finish();
		using var detailTexture = Texture.Create( 1, 1 ).WithData( new byte[] { 80, 160, 210, 255 } ).Finish();
		using var detailNormal = Texture.Create( 1, 1 ).WithData( new byte[] { 180, 100, 0, 255 } ).Finish();
		using var colorTexture = Texture.Create( 1, 1 ).WithData( new byte[] { 120, 160, 180, 255 } ).Finish();
		var material = Material.Create( $"complex-specialization-{feature}-{debugMode}", "shaders/complex.shader" );
		byte[] disabled = null;
		try
		{
			Assert.IsTrue( material.IsValid );
			Assert.AreEqual( "shaders/complex.shader", material.Shader.native.GetFilename(), "Shader failed to load and was replaced by error.shader." );
			material.Set( "g_tColor", colorTexture );
			material.Set( "g_flMetalness", 0.0f );
			material.Set( "g_tNormal", normalTexture );
			material.Set( "g_tBentNormal", bentTexture );
			material.Set( "g_tAmbientOcclusion", Texture.White );
			material.Set( "g_tTransmissiveColor", Texture.White );
			material.Set( "g_flRoughnessScaleFactor", 1.0f );
			material.Set( "g_vColorTint", Color.White );
			material.Set( "g_vTexCoordScale", Vector2.One );
			material.Set( "g_tDetail", detailTexture );
			material.Set( "g_tNormalDetail", detailNormal );
			material.Set( "g_tDetailMask", Texture.White );
			material.Set( "g_flDetailBlendFactor", 1.0f );
			material.Set( "g_flDetailNormalStrength", 1.0f );
			material.Set( "g_vDetailTexCoordScale", Vector2.One );
			material.Set( "g_flNormalMapScaleFactor", 0.2f );
			material.Set( "g_flLightRangeForSelfShadowNormals", 0.707f );
			var renderer = scene.CreateObject().Components.Create<ModelRenderer>();
			renderer.Model = Model.Sphere;
			renderer.MaterialOverride = material;
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.WorldPosition = debugMode < 0 ? Vector3.Zero : new Vector3( -140, 0, 0 );
			camera.WorldRotation = Rotation.Identity;
			camera.FieldOfView = 50;
			camera.ZNear = 1;
			camera.BackgroundColor = Color.Black;
			// The dedicated view makes the feature observable even without baked/DDGI
			// lighting in this isolated test world. It still executes the real complex PS.
			camera.DebugMode = (SceneCameraDebugMode)Math.Abs( debugMode );
			var ambient = scene.CreateObject().Components.Create<AmbientLight>();
			ambient.Color = Color.White * 0.25f;
			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = new Angles( 40, 30, 0 );
			sun.Shadows = false;
			sun.LightColor = Color.White;
			var point = scene.CreateObject().Components.Create<PointLight>();
			point.WorldPosition = new Vector3( -80, 20, 30 );
			point.Radius = 500;
			point.Shadows = false;
			point.LightColor = Color.White * 20;
			if ( feature == "F_TRANSMISSIVE_BACKFACE_NDOTL" )
			{
				point.WorldPosition = new Vector3( 80, 20, 30 );
				point.LightColor = Color.White * 100;
			}
			var envmap = scene.CreateObject().Components.Create<EnvmapProbe>();
			envmap.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			envmap.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
			envmap.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 10000 );
			int values = feature == "F_DETAIL_TEXTURE" ? 5 : 2;
			for ( int value = 0; value < values; ++value )
			{
				foreach ( var name in feature.Split( '+' ) )
				{
					material.SetFeature( name, value );
					Assert.AreEqual( value, material.GetFeature( name ) );
				}
				// Feature reloads can add texture/parameter bindings that were absent in
				// the previous variant. Bind the test inputs after selecting the variant.
				material.Set( "g_tDetail", detailTexture );
				material.Set( "g_tNormalDetail", detailNormal );
				material.Set( "g_tDetailMask", Texture.White );
				material.Set( "g_flDetailBlendFactor", 1.0f );
				material.Set( "g_flDetailNormalStrength", 1.0f );
				material.Set( "g_tTransmissiveColor", Texture.White );
				using var bitmap = new Bitmap( 128, 128 );
				for ( int frame = 0; frame < 20; ++frame )
				{
					// Allow async pipeline creation to finish instead of comparing fallback draws.
					System.Threading.Thread.Sleep( 50 );
					scene.GameTick( 0.0f );
					camera.RenderToBitmap( bitmap );
				}
				var pixels = bitmap.GetPixels();
				int visible = pixels.Count( p => p.r > 0.03f || p.g > 0.03f || p.b > 0.03f );
				if ( debugMode < 0 && value == 0 )
					Assert.AreEqual( 0, visible, "One-sided material should cull the sphere interior." );
				else
				{
					Assert.IsTrue( visible > 200, "Scene did not render a visible object." );
					Assert.IsTrue( pixels.Any( p => p.g > 0.03f || p.b > 0.03f ), "Error shader (red checker) rendered." );
				}
				var bytes = new byte[pixels.Length * 3];
				for ( int i = 0; i < pixels.Length; ++i )
				{
					bytes[i * 3] = (byte)Math.Clamp( (int)MathF.Round( pixels[i].r * 255 ), 0, 255 );
					bytes[i * 3 + 1] = (byte)Math.Clamp( (int)MathF.Round( pixels[i].g * 255 ), 0, 255 );
					bytes[i * 3 + 2] = (byte)Math.Clamp( (int)MathF.Round( pixels[i].b * 255 ), 0, 255 );
				}
				var path = Path.Combine( directory, $"{feature}-{debugMode}-{value}.rgb" );
				File.WriteAllBytes( Path.Combine( directory, $"{feature}-{debugMode}-{(capture ? "reference" : "comparison")}-{value}.png" ), bitmap.ToPng() );
				if ( value == 0 ) disabled = bytes;
				else if ( requireVisibleDifference && (feature != "F_DETAIL_TEXTURE" ||
					(debugMode == 10 ? value != 3 : value >= 3)) )
					Assert.IsTrue( disabled.Zip( bytes, ( a, b ) => Math.Abs( a - b ) ).Max() > 2, $"{feature} had no visible effect; comparison is not meaningful." );
				if ( capture ) File.WriteAllBytes( path, bytes );
				else
				{
					var reference = File.ReadAllBytes( path );
					Assert.AreEqual( reference.Length, bytes.Length );
					int maxError = reference.Zip( bytes, ( a, b ) => Math.Abs( a - b ) ).Max();
					Console.WriteLine( $"{feature} {value}: maximum RGB8 error {maxError}" );
					Assert.IsTrue( maxError <= 2, "Specialized rendering differs from macro rendering." );
				}
			}
		}
		finally
		{
			scene.Destroy();
			material.Destroy();
		}
	}
}
