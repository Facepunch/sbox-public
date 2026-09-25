using NativeEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace EngineTests;

/// <summary>Opt-in, fresh-process shader loading and GPU timestamp benchmark.</summary>
[TestClass]
public class ComplexPerformanceTest
{
	[TestMethod]
	public void Measure()
	{
		var output = Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_OUTPUT" );
		if ( string.IsNullOrEmpty( output ) ) Assert.Inconclusive( "Set SBOX_COMPLEX_PERF_OUTPUT to run the performance experiment." );
		Assert.IsTrue( Directory.Exists( Path.GetDirectoryName( output ) ) );
		Assert.AreEqual( RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN, g_pRenderDevice.GetRenderDeviceAPI() );
		int resolution = int.Parse( Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_RESOLUTION" ) ?? "2048" );
		int samples = int.Parse( Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_SAMPLES" ) ?? "120" );
		int warmup = int.Parse( Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_WARMUP" ) ?? "60" );
		bool textureRendering = Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_TEXTURE" ) == "1";
		int rotation = int.Parse( Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_ROTATION" ) ?? "0" );
		int grid = int.Parse( Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_GRID" ) ?? "6" );
		string shaderName = Environment.GetEnvironmentVariable( "SBOX_SHADER_PERF_NAME" ) ?? "complex";
		Assert.IsTrue( shaderName is "complex" or "skin" or "generic" );
		string shaderPath = $"shaders/{shaderName}.shader";
		var records = new List<object>();
		var materials = new List<Material>();
		using var process = Process.GetCurrentProcess();
		var scene = new Scene();
		using var scope = scene.Push();
		using var normal = Texture.Create( 1, 1 ).WithData( new byte[] { 155, 120, 150, 255 } ).Finish();
		using var bent = Texture.Create( 1, 1 ).WithData( new byte[] { 170, 128, 0, 255 } ).Finish();
		using var detail = Texture.Create( 1, 1 ).WithData( new byte[] { 80, 160, 210, 255 } ).Finish();
		using var detailNormal = Texture.Create( 1, 1 ).WithData( new byte[] { 180, 100, 0, 255 } ).Finish();
		using var color = Texture.Create( 1, 1 ).WithData( new byte[] { 120, 160, 180, 255 } ).Finish();
		using var masks = Texture.Create( 1, 1 ).WithData( new byte[] { 0, 255, 0, 255 } ).Finish();
		using var legacyNormal = Texture.Create( 1, 1 ).WithData( new byte[] { 128, 128, 255, 128 } ).Finish();
		using var bitmap = new Bitmap( resolution, resolution );
		using var target = Texture.CreateRenderTarget().WithSize( resolution, resolution ).Create();
		var previousProfilerMode = CSceneSystem.GetGPUProfilerMode();
		CSceneSystem.SetGPUProfilerMode( SceneSystemGPUProfilerMode.SCENE_GPU_PROFILER_TIMESTAMP_ONLY );
		try
		{
			// Direct metadata loads bypass resources preloaded during engine boot. This
			// includes reading the complete file and parsing metadata/shared tables, but
			// excludes lazy variant decompression and driver pipeline creation.
			var metadataLoads = new List<double>();
			for ( int i = 0; i < 10; ++i )
			{
				var shader = new Shader();
				var loadTimer = Stopwatch.StartNew();
				Assert.IsTrue( shader.LoadCompiledForRecompile( Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "core", shaderPath ) ) );
				metadataLoads.Add( loadTimer.Elapsed.TotalMilliseconds );
				shader.Destroy();
				EngineLoop.RunAsyncTasks();
			}
			records.Add( new { kind = "metadata", milliseconds = metadataLoads } );
			// Warm managed/native material entry points with an unrelated shader first.
			var dummy = Material.Create( "complex-perf-jit", "shaders/unlit.shader" );
			dummy.SetFeature( "F_RENDER_BACKFACES", 0 );
			materials.Add( dummy );
			process.Refresh();
			long memoryBefore = process.PrivateMemorySize64;
			var timer = Stopwatch.StartNew();
			var material = Material.Create( "complex-perf-first", shaderPath );
			double firstMaterialMs = timer.Elapsed.TotalMilliseconds;
			materials.Add( material );
			Assert.AreEqual( shaderPath, material.Shader.native.GetFilename() );
			process.Refresh();
			records.Add( new { kind = "preloaded-material", firstMaterialMs, privateDeltaBytes = process.PrivateMemorySize64 - memoryBefore } );

			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.GameObject.Name = "ComplexPerf";
			camera.WorldPosition = new Vector3( -500 * grid / 6.0f, 0, 0 );
			camera.WorldRotation = Rotation.Identity;
			camera.FieldOfView = 50;
			camera.ZNear = 1;
			camera.BackgroundColor = Color.Black;
			camera.SceneCamera.Attributes.Set( "msaa", 0 );
			for ( int y = 0; y < grid; ++y )
				for ( int z = 0; z < grid; ++z )
				{
					var renderer = scene.CreateObject().Components.Create<ModelRenderer>();
					renderer.Model = Model.Sphere;
					renderer.WorldPosition = new Vector3( 0, (y - (grid - 1) * 0.5f) * 64, (z - (grid - 1) * 0.5f) * 64 );
					renderer.MaterialOverride = material;
				}
			var ambient = scene.CreateObject().Components.Create<AmbientLight>();
			ambient.Color = Color.White * 0.25f;
			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = new Angles( 40, 30, 0 );
			sun.Shadows = false;
			sun.LightColor = Color.White;
			var point = scene.CreateObject().Components.Create<PointLight>();
			point.WorldPosition = new Vector3( -200, 20, 100 );
			point.Radius = 1000;
			point.Shadows = false;
			point.LightColor = Color.White * 20;
			var envmap = scene.CreateObject().Components.Create<EnvmapProbe>();
			envmap.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			envmap.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
			envmap.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 10000 );

			var cases = new (string Name, string Features)[]
			{
				("plain", ""),
				("bent", "F_USE_BENT_NORMALS=1"),
				("scale", "F_SCALE_NORMAL_MAP=1"),
				("self-shadow", "F_ENABLE_NORMAL_SELF_SHADOW=1"),
				("cloth", "F_CLOTH_SHADING=1"),
				("detail1", "F_DETAIL_TEXTURE=1"),
				("detail2", "F_DETAIL_TEXTURE=2"),
				("detail3", "F_DETAIL_TEXTURE=3"),
				("detail4", "F_DETAIL_TEXTURE=4"),
				("transmission", "F_TRANSMISSIVE_BACKFACE_NDOTL=1"),
				("two-sided", "F_RENDER_BACKFACES=1"),
				("bent-cloth-detail", "F_USE_BENT_NORMALS=1,F_CLOTH_SHADING=1,F_DETAIL_TEXTURE=4")
			};
			if ( shaderName == "skin" )
				cases = [("plain", ""), ("bent", "F_USE_BENT_NORMALS=1"), ("age", "F_AGE_TEXTURE=1"),
					("bent-age", "F_USE_BENT_NORMALS=1,F_AGE_TEXTURE=1"),
					("bent-debug-off", ""), ("bent-debug-on", "F_USE_BENT_NORMALS=1")];
			else if ( shaderName == "generic" )
				cases = [("plain", ""), ("two-sided", "F_RENDER_BACKFACES=1"),
					("specular", "F_SPECULAR=1"), ("specular-two-sided", "F_SPECULAR=1,F_RENDER_BACKFACES=1"),
					("interior-one-sided", ""), ("interior-two-sided", "F_RENDER_BACKFACES=1")];
			var featureNames = cases.SelectMany( c => c.Features.Split( ',', StringSplitOptions.RemoveEmptyEntries ) ).Select( x => x.Split( '=' )[0] ).Distinct().ToArray();
			cases = cases.Skip( rotation % cases.Length ).Concat( cases.Take( rotation % cases.Length ) ).ToArray();
			// Stabilize the GPU and common rendering paths before the first feature case.
			Bind( material );
			for ( int i = 0; i < 180; ++i ) Render();
			var filter = Environment.GetEnvironmentVariable( "SBOX_COMPLEX_PERF_CASES" )?.Split( ',' );
			foreach ( var testCase in cases.Where( c => filter is null || filter.Contains( c.Name ) ) )
			{
				bool interior = testCase.Name.StartsWith( "interior-", StringComparison.Ordinal );
				camera.WorldPosition = interior ? new Vector3( 0, -(grid - 1) * 32, -(grid - 1) * 32 ) : new Vector3( -500 * grid / 6.0f, 0, 0 );
				// Isolate culling/normal flipping for the interior checks from lighting.
				camera.DebugMode = (SceneCameraDebugMode)(interior ? 21 : testCase.Name.StartsWith( "bent-debug", StringComparison.Ordinal ) ? 25 : 0);
				timer.Restart();
				foreach ( var name in featureNames ) material.SetFeature( name, 0 );
				foreach ( var entry in testCase.Features.Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
				{
					var parts = entry.Split( '=' );
					material.SetFeature( parts[0], int.Parse( parts[1] ) );
					Assert.AreEqual( int.Parse( parts[1] ), material.GetFeature( parts[0] ) );
				}
				double selectMs = timer.Elapsed.TotalMilliseconds;
				Bind( material );
				timer.Restart();
				Render();
				double firstRenderMs = timer.Elapsed.TotalMilliseconds;
				for ( int i = 0; i < warmup; ++i ) Render();
				using var textureBitmap = textureRendering ? target.GetBitmap() : null;
				var captureBitmap = textureBitmap ?? bitmap;
				var pixels = captureBitmap.GetPixels();
				Assert.IsTrue( pixels.Count( p => p.g > 0.03f || p.b > 0.03f ) > resolution * resolution / 10 || testCase.Name == "interior-one-sided", "Missing object or error shader." );
				File.WriteAllBytes( output + "." + testCase.Name + ".png", captureBitmap.ToPng() );
				// Keep readback/PNG work outside the measured sample window.
				for ( int i = 0; i < 30; ++i ) Render();
				var frames = new List<object>();
				for ( int i = 0; i < samples; ++i )
				{
					timer.Restart();
					Render();
					double wallMs = timer.Elapsed.TotalMilliseconds;
					CSceneSystem.RefreshGpuTimestampSnapshot();
					var gpu = new Dictionary<string, float>();
					for ( int j = 0; j < CSceneSystem.GetGpuTimestampCount(); ++j )
						gpu[CSceneSystem.GetGpuTimestampPath( j )] = CSceneSystem.GetGpuTimestampDuration( j );
					Assert.IsTrue( gpu.Count > 0, "GPU timestamp collection failed." );
					var invalid = gpu.Where( x => !float.IsFinite( x.Value ) || x.Value < 0 ).ToArray();
					Assert.AreEqual( 0, invalid.Length, "Invalid timestamps: " + JsonSerializer.Serialize( invalid ) );
					Assert.IsTrue( gpu.TryGetValue( (textureRendering ? "ComplexPerf.RenderToTexture" : "RenderToBitmap") + "/Dynamic Opaque Forward", out var forward ) && float.IsFinite( forward ) && forward > 0, "Invalid forward-pass timestamp." );
					frames.Add( new { wallMs, gpu } );
				}
				records.Add( new { kind = "render", name = testCase.Name, selectMs, firstRenderMs, frames } );
				Console.WriteLine( $"Measured {testCase.Name}: selection {selectMs:F3} ms, first render {firstRenderMs:F3} ms, {samples} GPU samples" );
				Save();
			}

			// Retain all instances; repeat the same valid feature workload to separate
			// variant first-use from already-loaded material construction.
			for ( int pass = 0; shaderName == "complex" && pass < 2; ++pass )
			{
				process.Refresh();
				long before = process.PrivateMemorySize64;
				timer.Restart();
				for ( int i = 0; i < 128; ++i )
				{
					var m = Material.Create( $"complex-perf-{pass}-{i}", shaderPath );
					materials.Add( m );
					m.SetFeature( "F_DETAIL_TEXTURE", i % 5 );
					m.SetFeature( "F_USE_BENT_NORMALS", (i / 5) % 2 );
					m.SetFeature( "F_CLOTH_SHADING", (i / 10) % 2 );
					m.SetFeature( "F_ENABLE_NORMAL_SELF_SHADOW", (i / 20) % 2 );
					m.SetFeature( "F_RENDER_BACKFACES", (i / 40) % 2 );
					Bind( m );
				}
				double materialBatchMs = timer.Elapsed.TotalMilliseconds;
				process.Refresh();
				records.Add( new { kind = "materials", pass, count = 128, materialBatchMs, privateDeltaBytes = process.PrivateMemorySize64 - before } );
				Save();
			}
			Save();
			Console.WriteLine( g_pRenderDevice.GetGpuStatsSummary() );

			void Bind( Material m )
			{
				m.Set( "g_tColor", color );
				m.Set( "g_tNormal", normal );
				m.Set( "g_tBentNormal", bent );
				m.Set( "g_tAmbientOcclusion", Texture.White );
				m.Set( "g_tTransmissiveColor", Texture.White );
				m.Set( "g_tDetail", detail );
				m.Set( "g_tNormalDetail", detailNormal );
				m.Set( "g_tDetailMask", Texture.White );
				m.Set( "g_flMetalness", 0.0f );
				m.Set( "g_flRoughnessScaleFactor", 1.0f );
				m.Set( "g_vColorTint", Color.White );
				m.Set( "g_vTexCoordScale", Vector2.One );
				m.Set( "g_flDetailBlendFactor", 1.0f );
				m.Set( "g_flDetailNormalStrength", 1.0f );
				m.Set( "g_vDetailTexCoordScale", Vector2.One );
				m.Set( "g_flNormalMapScaleFactor", 0.2f );
				m.Set( "g_flLightRangeForSelfShadowNormals", 0.707f );
				if ( shaderName == "skin" )
				{
					m.Set( "g_tCombinedMasks", masks );
					m.Set( "g_tTintLookup", Texture.White );
					m.Set( "g_tAgeNormal", detailNormal );
					m.Set( "g_tAgeColor", detail );
					m.Set( "g_bOverrideTinting", true );
					m.Set( "g_flSkinAge", 0.75f );
				}
				if ( shaderName == "generic" )
				{
					m.Set( "g_tNormal", legacyNormal );
					m.Set( "g_tRoughness", normal );
					m.Set( "g_tMetalnessReflectanceFresnel", masks );
				}
			}
			void Render()
			{
				// Advance native resource/scene frames so completed timestamps are collected
				// and query pools recycled. Report GPU layer timestamps separately from
				// end-to-end wall time, which includes submission and synchronization.
				EngineGlobal.SourceEnginePanelAppFrame();
				scene.GameTick( 0.0f );
				if ( textureRendering ) camera.RenderToTexture( target );
				else camera.RenderToBitmap( bitmap );
				CompleteFrame();
			}
			void CompleteFrame()
			{
				// Even offscreen tests need a frame boundary to retire/reuse Vulkan
				// queries and native allocations, plus managed render-target cleanup.
				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();
			}
			void Save() => File.WriteAllText( output, JsonSerializer.Serialize( new
			{
				shaderName,
				resolution,
				samples,
				warmup,
				rotation,
				grid,
				textureRendering,
				graphicsArgs = Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS_ARGS" ),
				records
			}, new JsonSerializerOptions { WriteIndented = true } ) );
		}
		finally
		{
			CSceneSystem.SetGPUProfilerMode( previousProfilerMode );
			scene.Destroy();
			foreach ( var material in materials ) material.Destroy();
		}
	}
}
