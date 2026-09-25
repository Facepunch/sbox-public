using System;
using NativeEngine;
using System.Collections.Generic;
using Sandbox.Diagnostics;

namespace EngineTests;

[TestClass]
public class ShaderActiveIfTest
{
	[TestMethod]
	[DataRow( "complex", "F_USE_BENT_NORMALS", "TextureBentNormal" )]
	[DataRow( "complex", "F_TRANSMISSIVE_BACKFACE_NDOTL", "TextureTransmissiveColor" )]
	[DataRow( "skin", "F_USE_BENT_NORMALS", "TextureBentNormal" )]
	[DataRow( "skin", "F_AGE_TEXTURE", "TextureAgeColor" )]
	[DataRow( "skin", "F_AGE_TEXTURE", "TextureAgeNormal" )]
	public void EditorUsesMaterialFeatureValues( string name, string feature, string input )
	{
		var shader = new Shader();
		try
		{
			Assert.IsTrue( shader.LoadCompiledForRecompile( System.IO.Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), $"core/shaders/{name}.shader" ) ) );
			Assert.IsFalse( shader.native.IsUiVariableActive( input, feature, 0 ), "Inactive texture input was visible." );
			Assert.IsTrue( shader.native.IsUiVariableActive( input, feature, 1 ), "Active texture input was hidden." );
			Assert.IsTrue( shader.native.IsUiVariableActive( "TextureColor", feature, 0 ), "Unconditional texture disappeared." );
			Assert.IsFalse( shader.native.IsUiVariableActive( input, feature, 0 ), "Toggling the feature did not hide the input again." );
		}
		finally { shader.Destroy(); }
	}

	[TestMethod]
	public void MissingOptionalTexturesUseFallbacks()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" ) Assert.Inconclusive( "Requires Vulkan." );
		var assets = System.IO.Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "addons/citizen/Assets" );
		FullFileSystem.AddProjectPath( "activeif-citizen", assets.ToLowerInvariant() );
		g_pResourceSystem.InvalidateDatabase();
		var messages = new List<string>();
		void OnMessage( LogEvent e ) { messages.Add( e.Message ); }
		Logging.OnMessage += OnMessage;
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( 128, 128 ).Create();
		var renderer = scene.CreateObject().Components.Create<ModelRenderer>();
		renderer.Model = Model.Sphere;
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.WorldPosition = new Vector3( -140, 0, 0 );
		camera.WorldRotation = Rotation.Identity;
		camera.BackgroundColor = Color.Black;
		camera.ZNear = 1;
		camera.FieldOfView = 50;
		camera.DebugMode = (SceneCameraDebugMode)10;
		using var normal = Texture.Create( 1, 1 ).WithData( new byte[] { 155, 120, 150, 255 } ).Finish();
		camera.SceneCamera.Attributes.Set( "msaa", 0 );
		var light = scene.CreateObject().Components.Create<DirectionalLight>();
		light.Shadows = false;
		light.LightColor = Color.White;
		try
		{
			foreach ( var name in new[] { "complex", "skin" } )
			{
				// Use existing compiled materials that legitimately omit the optional textures.
				var material = LegacyMaterial( name );
				try
				{
					Assert.IsTrue( material.IsValid );
					Assert.AreEqual( $"shaders/{name}.shader", material.Shader.native.GetFilename() );
					Assert.IsFalse( material.native.HasParam( "g_tBentNormal" ), "Fixture must omit optional textures." );
					material.SetFeature( "F_USE_BENT_NORMALS", 0 );
					if ( name == "skin" ) material.SetFeature( "F_AGE_TEXTURE", 0 );
					else material.SetFeature( "F_TRANSMISSIVE_BACKFACE_NDOTL", 0 );
					material.Set( "g_tColor", Texture.White );
					material.Set( "g_tNormal", normal );
					material.Set( "g_vColorTint", Color.White );
					material.Set( "g_vTexCoordScale", Vector2.One );
					material.Set( "g_tAmbientOcclusion", Texture.White );
					material.Set( "g_tCombinedMasks", Texture.White );
					material.Set( "g_tTintLookup", Texture.White );
					Render( material );
					using var missing = target.GetBitmap();
					var before = missing.GetPixels32();
					Assert.IsTrue( missing.GetPixels().Any( p => p.g > 0.03f || p.b > 0.03f ), "No visible material." );
					foreach ( var texture in new[] { "g_tBentNormal", "g_tAgeNormal", "g_tAgeColor", "g_tTransmissiveColor" } ) material.Set( texture, Texture.White );
					Render( material );
					using var supplied = target.GetBitmap();
					CollectionAssert.AreEqual( before, supplied.GetPixels32(), "Inactive missing textures changed rendering." );
					Logging.PushQueuedMessages();
					Assert.IsFalse( messages.Any( m => m.Contains( "doesn't exist in" ) &&
						(m.Contains( "g_tBentNormal" ) || m.Contains( "g_tAge" ) || m.Contains( "g_tTransmissiveColor" )) ), "Inactive texture produced a warning." );
				}
				finally { material.Destroy(); }
			}
			var active = LegacyMaterial( "skin" );
			try
			{
				messages.Clear();
				active.SetFeature( "F_AGE_TEXTURE", 1 );
				Assert.AreEqual( 1, active.GetFeature( "F_AGE_TEXTURE" ) );
				Render( active ); // Must still diagnose missing active age textures.
				Logging.PushQueuedMessages();
				Assert.IsTrue( messages.Any( m => m.Contains( "g_tAgeColor" ) && m.Contains( "doesn't exist" ) ), "Missing active texture was not diagnosed." );
				active.Set( "g_tAgeColor", Texture.White );
				active.Set( "g_tAgeNormal", Texture.White );
				var supplied = active.GetTexture( "g_tAgeColor" );
				Assert.IsNotNull( supplied );
				active.SetFeature( "F_AGE_TEXTURE", 0 );
				active.SetFeature( "F_AGE_TEXTURE", 1 );
				Assert.AreEqual( supplied.native, active.GetTexture( "g_tAgeColor" )?.native, "Feature toggle discarded the supplied texture." );
			}
			finally { active.Destroy(); }
		}
		finally { Logging.OnMessage -= OnMessage; scene.Destroy(); }

		Material LegacyMaterial( string name )
		{
			var relative = name == "skin" ? "models/citizen/skin/skeleton/Textures/skeleton_skin.vmat" : "models/citizen_clothes/dress/simple_dress/textures/simple_dress_blue.vmat";
			var loaded = Material.Load( relative );
			Assert.IsNotNull( loaded, "Citizen fixture missing." );
			return loaded.CreateCopy();
		}

		void Render( Material material )
		{
			renderer.MaterialOverride = material;
			for ( int i = 0; i < 20; ++i )
			{
				EngineGlobal.SourceEnginePanelAppFrame();
				scene.GameTick( 0 );
				camera.RenderToTexture( target );
				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();
			}
		}
	}
}
