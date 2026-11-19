using NativeEngine;
using Sandbox.Rendering;
using Sandbox.UI;
using System;
using System.Collections.Generic;

namespace EngineTests;

/// <summary>
/// The renderer allocates no managed memory once a scene is steady (owner, 2026-11-17): a camera's frames on Borealis.Gpu,
/// on the main thread and the recording workers, counted by <see cref="Borealis.RendererAllocations"/> as the dev tools'
/// per-frame renderer allocations are. Runs with <c>SBOX_TEST_GRAPHICS=1</c>, and in the allocation tests' category, which
/// SboxBuild runs with tiered compilation off: code still at tier 0 allocates boxes and closures that optimised code keeps on
/// the stack, so set <c>DOTNET_TieredCompilation=0</c> to run it alone.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RendererAllocationTests
{
	const int Width = 640, Height = 360;

	// Frames in a row that must allocate nothing, and the most frames it may take to get there (shaders, pipelines,
	// streaming and caches warm up first)
	const int SettledFrames = 8, MaxFrames = 240;

	/// <summary>
	/// A scene with something of each kind the renderer draws: static and moving models (GPU-driven), a skinned and
	/// animating citizen, a sun with cascaded shadows, shadowed point and spot lights, particles, a screen panel and a world
	/// panel, and post processing (bloom, tonemapping, ambient occlusion, sharpening). Once it has warmed up, frames in a row
	/// allocate nothing in the renderer.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void SteadyFramesAllocateNothing()
	{
		TestGraphics.Require();

		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA16161616F ).Create( "renderer-allocations" );
		try
		{
			var camera = Build( scene, out var spinning );
			var setup = new ViewSetup { EnablePostprocessing = true, EnableUI = true, EnableDebugOverlays = false };

			var bytes = new List<long>( MaxFrames );
			var settled = 0;
			for ( int frame = 0; frame < MaxFrames && settled < SettledFrames; frame++ )
			{
				spinning.WorldRotation = Rotation.FromYaw( frame * 3 );

				// An engine frame, as the loop starts one: the device starts its next frame with it
				Application.FrameCount++;
				Sandbox.Rendering.SceneRenderer.BeginFrame();
				EngineGlobal.SourceEnginePanelAppFrame();
				scene.GameTick( 1.0 / 60 );
				scene.PreRender();

				Borealis.RendererAllocations.TakeFrame();
				Assert.IsTrue( camera.RenderToTexture( target, setup ) );
				var allocated = Borealis.RendererAllocations.TakeFrame();

				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();

				bytes.Add( allocated );
				settled = allocated == 0 ? settled + 1 : 0;
			}

			Assert.AreEqual( SettledFrames, settled, $"the renderer kept allocating: never {SettledFrames} frames in a row with nothing in {MaxFrames}. Bytes per frame: {string.Join( ", ", bytes )}" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	static CameraComponent Build( Scene scene, out GameObject spinning )
	{
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.CustomSize = new Vector2( Width, Height );
		camera.WorldPosition = new Vector3( -420, -260, 240 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 40 ) - camera.WorldPosition );
		camera.FieldOfView = 75;
		camera.ZNear = 4;
		camera.ZFar = 6000;
		camera.BackgroundColor = new Color( 0.3f, 0.4f, 0.5f );
		camera.GameObject.Components.Create<Bloom>();
		camera.GameObject.Components.Create<Tonemapping>();
		camera.GameObject.Components.Create<AmbientOcclusion>();
		camera.GameObject.Components.Create<Sharpen>();

		// Ground and a ring of boxes and spheres, one of them turning
		var ground = scene.CreateObject().Components.Create<ModelRenderer>();
		ground.Model = Model.Cube;
		ground.WorldScale = new Vector3( 30, 30, 0.1f );
		ground.WorldPosition = new Vector3( 0, 0, -5 );
		for ( int i = 0; i < 12; i++ )
		{
			var angle = i / 12f * MathF.Tau;
			var model = scene.CreateObject().Components.Create<ModelRenderer>();
			model.Model = i % 2 == 0 ? Model.Cube : Model.Sphere;
			model.WorldPosition = new Vector3( MathF.Cos( angle ) * 220, MathF.Sin( angle ) * 220, 30 );
			model.Tint = i % 3 == 0 ? Color.Red : Color.White;
		}

		var tower = scene.CreateObject().Components.Create<ModelRenderer>();
		tower.Model = Model.Cube;
		tower.WorldScale = new Vector3( 0.5f, 0.5f, 3 );
		tower.WorldPosition = new Vector3( 0, 0, 150 );
		spinning = tower.GameObject;

		// A citizen, animating
		var citizen = scene.CreateObject().Components.Create<SkinnedModelRenderer>();
		citizen.Model = Model.Load( "models/citizen_human/human.vmdl" );
		citizen.WorldPosition = new Vector3( 80, -60, 0 );
		citizen.UseAnimGraph = true;

		// Lights, all casting shadows
		scene.CreateObject().Components.Create<AmbientLight>().Color = new Color( 0.1f, 0.12f, 0.15f );
		var sun = scene.CreateObject().Components.Create<DirectionalLight>();
		sun.WorldRotation = new Angles( 50, 30, 0 );
		sun.LightColor = Color.White * 1.5f;
		sun.Shadows = true;
		var point = scene.CreateObject().Components.Create<PointLight>();
		point.WorldPosition = new Vector3( -100, 100, 120 );
		point.Radius = 600;
		point.LightColor = new Color( 3, 2, 1 );
		point.Shadows = true;
		var spot = scene.CreateObject().Components.Create<SpotLight>();
		spot.WorldPosition = new Vector3( 150, -150, 250 );
		spot.WorldRotation = Rotation.LookAt( Vector3.Zero - spot.WorldPosition );
		spot.Radius = 800;
		spot.LightColor = new Color( 1, 2, 3 );
		spot.Shadows = true;

		// Particles at a steady rate
		var particles = scene.CreateObject();
		particles.WorldPosition = new Vector3( -150, -100, 60 );
		var effect = particles.Components.Create<ParticleEffect>();
		effect.MaxParticles = 200;
		effect.Lifetime = 2;
		var emitter = particles.Components.Create<ParticleSphereEmitter>();
		emitter.Rate = 60;
		emitter.Radius = 40;
		particles.Components.Create<ParticleSpriteRenderer>();

		// UI on the screen and in the world
		var screen = scene.CreateObject().Components.Create<ScreenPanel>();
		screen.GetPanel().AddChild( new Label( "Renderer allocations" ) );
		var world = scene.CreateObject();
		world.WorldPosition = new Vector3( 0, 200, 120 );
		world.Components.Create<Sandbox.WorldPanel>().GetPanel().AddChild( new Label( "In the world" ) );

		return camera;
	}
}
