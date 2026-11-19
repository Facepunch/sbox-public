using NativeEngine;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SceneTests;

/// <summary>Measures the scene's real component gizmo callbacks, including disabled and hidden cases.</summary>
[TestClass, DoNotParallelize]
public class GizmoSceneBenchmarks
{
	[TestMethod, TestCategory( "Slow" )]
	[DataRow( "empty", 2000, "visible", false )]
	[DataRow( "models", 2000, "visible", false )]
	[DataRow( "boxes", 2000, "visible", false )]
	[DataRow( "boxes", 2000, "visible", true )]
	[DataRow( "modelcolliders", 500, "visible", true )]
	[DataRow( "spawn", 2000, "visible", false )]
	[DataRow( "lights", 200, "visible", false )]
	[DataRow( "lights", 200, "visible", true )]
	[DataRow( "probe", 2000, "visible", false )]
	[DataRow( "probe", 10000, "disabled", false )]
	[DataRow( "probe", 2000, "hidden-object", false )]
	[DataRow( "probe", 2000, "hidden-component", false )]
	[DataRow( "probe", 2000, "hidden-type", false )]
	[DataRow( "probe", 2000, "inactive-object", false )]
	[DataRow( "probe", 2000, "inactive-component", false )]
	[DataRow( "probe", 2000, "toggle-global", false )]
	[DataRow( "probe", 2000, "toggle-type", false )]
	[DataRow( "probe", 2000, "toggle-object", false )]
	[DataRow( "lights", 200, "toggle-type", false )]
	public void MeasureScene( string workload, int count, string visibility, bool selected )
	{
		var output = Environment.GetEnvironmentVariable( "SBOX_GIZMO_BENCHMARK_OUTPUT" );
		if ( string.IsNullOrWhiteSpace( output ) ) Assert.Inconclusive( "Set SBOX_GIZMO_BENCHMARK_OUTPUT." );
		Directory.CreateDirectory( output );
		ThreadSafe.MarkMainThread();
		var oldLibrary = Game.TypeLibrary;
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		var scene = new Scene( true, false );
		var world = new SceneWorld();
		using var gizmo = new Gizmo.Instance();
		using var camera = new SceneCamera
		{
			World = world,
			Position = new Vector3( -2048, 0, 0 ),
			Rotation = Rotation.Identity,
			Size = new Vector2( 1280, 720 ),
			Ortho = true,
			OrthoHeight = 1200,
			ZNear = 1,
			ZFar = 4096,
			BackgroundColor = new Color( .025f, .03f, .04f )
		};
		camera.DebugWorlds.Add( gizmo.World );
		gizmo.Input = new() { Camera = camera, IsHovered = false };
		gizmo.Settings.GizmosEnabled = visibility != "disabled";
		if ( visibility == "hidden-type" ) gizmo.Settings.SetGizmoEnabled( typeof( GizmoBenchmarkProbe ), false );
		try
		{
			using var sceneScope = scene.PushScope();
			var objects = new GameObject[count];
			for ( var i = 0; i < count; ++i )
			{
				var go = objects[i] = scene.CreateObject();
				go.WorldPosition = new Vector3( 0, (i % 50 - 25) * 24, (i / 50 - 20) * 24 );
				Component component = workload switch
				{
					"models" => go.Components.Create<ModelRenderer>(),
					"boxes" => go.Components.Create<BoxCollider>(),
					"modelcolliders" => go.Components.Create<ModelCollider>(),
					"spawn" => go.Components.Create<SpawnPoint>(),
					"lights" => go.Components.Create<PointLight>(),
					"probe" => go.Components.Create<GizmoBenchmarkProbe>(),
					_ => null
				};
				if ( component is ModelCollider collider )
				{
					collider.Model = Model.Load( "models/citizen_human/human.vmdl" );
					Assert.IsTrue( collider.Model.Physics?.Parts.Count > 0, "Model collider benchmark requires physics geometry." );
				}
				if ( component is ModelRenderer renderer ) renderer.Model = Model.Load( "models/dev/box.vmdl" );
				if ( visibility == "hidden-object" ) go.Flags |= GameObjectFlags.Hidden;
				if ( visibility == "inactive-object" ) go.Enabled = false;
				if ( visibility == "hidden-component" ) component.Flags |= ComponentFlags.Hidden;
				if ( visibility == "inactive-component" ) component.Enabled = false;
				if ( selected ) gizmo.Selection.Add( go );
			}

			using var target = Graphics.IsAvailable ? Texture.CreateRenderTarget().WithSize( 1280, 720 )
				.WithFormat( ImageFormat.RGBA8888 ).Create( "Scene gizmo benchmark" ) : null;
			var view = new ViewSetup { EnableDebugOverlays = true, EnablePostprocessing = false, EnableUI = false };
			var samples = new SceneSample[240];
			var warmed = Stopwatch.StartNew();
			var warmupFrames = 0;
			var step = 0;
			GpuProfilerStats.Enabled = target is not null;
			for ( var frame = -1; frame < samples.Length; )
			{
				if ( target is not null ) EngineGlobal.SourceEnginePanelAppFrame();
				var show = (step++ & 1) == 0;
				SetVisibility( show );
				GizmoBenchmarkProbe.Calls = 0;
				var allBytes = GC.GetTotalAllocatedBytes( true );
				var bytes = GC.GetAllocatedBytesForCurrentThread();
				var start = Stopwatch.GetTimestamp();
				using ( gizmo.PushScope() ) scene.DrawGizmos();
				var built = Stopwatch.GetTimestamp();
				var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
				if ( target is not null )
				{
					camera.RenderToTexture( target, null, view );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
					EngineLoop.DrainFrameEndDisposables();
					RenderTarget.EndOfFrame();
					EngineLoop.RunAsyncTasks();
					GpuProfilerStats.Update();
				}
				var finished = Stopwatch.GetTimestamp();
				var totalAllocated = GC.GetTotalAllocatedBytes( true ) - allBytes;
				if ( frame >= 0 ) samples[frame++] = new( allocated, totalAllocated,
					Stopwatch.GetElapsedTime( start, built ).TotalMilliseconds,
					Stopwatch.GetElapsedTime( start, finished ).TotalMilliseconds, GpuProfilerStats.BusyMs, GizmoBenchmarkProbe.Calls );
				else if ( ++warmupFrames >= 120 && warmed.Elapsed.TotalSeconds >= 1 ) frame = 0;
			}
			void SetVisibility( bool show )
			{
				if ( visibility == "toggle-global" ) gizmo.Settings.GizmosEnabled = show;
				if ( visibility == "toggle-type" ) gizmo.Settings.SetGizmoEnabled( workload == "lights" ? typeof( PointLight ) : typeof( GizmoBenchmarkProbe ), show );
				if ( visibility == "toggle-object" )
					foreach ( var go in objects ) go.Flags = show ? go.Flags & ~GameObjectFlags.Hidden : go.Flags | GameObjectFlags.Hidden;
			}

			if ( workload == "modelcolliders" ) Assert.IsTrue( gizmo.World.SceneObjects.Count > 0, "Model collider benchmark must draw geometry." );
			var name = $"scene-{workload}-{count}-{visibility}-{selected}";
			File.WriteAllText( Path.Combine( output, name + ".json" ), JsonSerializer.Serialize( new
			{
				Workload = workload,
				Count = count,
				Visibility = visibility,
				Selected = selected,
				WarmupFrames = warmupFrames,
				MeasuredFrames = samples.Length,
				Graphics = Graphics.IsAvailable,
				Objects = gizmo.World.SceneObjects.Count,
				VisibleObjects = gizmo.World.SceneObjects.Count( x => x.RenderingEnabled ),
				Samples = samples
			}, new JsonSerializerOptions { WriteIndented = true } ) );
			if ( target is not null )
			{
				if ( visibility.StartsWith( "toggle-", StringComparison.Ordinal ) )
				{
					SetVisibility( true );
					using ( gizmo.PushScope() ) scene.DrawGizmos();
					camera.RenderToTexture( target, null, view );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				}
				using var bitmap = target.GetBitmap();
				File.WriteAllBytes( Path.Combine( output, name + ".png" ), bitmap.ToPng() );
			}
		}
		finally
		{
			GpuProfilerStats.Enabled = false;
			camera.DebugWorlds.Clear();
			gizmo.Dispose();
			scene.Destroy();
			world.Delete();
			SceneWorld.FlushQueuedDeletes();
			Game.TypeLibrary = oldLibrary;
		}
	}

	public readonly record struct SceneSample( long AllocatedBytes, long AllThreadBytes, double BuildMs, double FrameMs, double GpuMs, int Callbacks );
}

public sealed class GizmoBenchmarkProbe : Component
{
	internal static int Calls;
	protected override void DrawGizmos()
	{
		Calls++;
		Gizmo.Hitbox.BBox( BBox.FromPositionAndSize( 0, 16 ) );
		Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( 0, 16 ) );
	}
}
