using NativeEngine;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SceneTests;

/// <summary>Repeatable gizmo workloads, including native uploads and optional offscreen GPU rendering.</summary>
[TestClass, DoNotParallelize]
public class GizmoBenchmarks
{
	const int WarmupFrames = 120;
	const int MeasuredFrames = 240;
	const int Width = 1280;
	const int Height = 720;

	[TestMethod, TestCategory( "Slow" )]
	[DataRow( "scopes", 1000 )]
	[DataRow( "bounds", 100 )]
	[DataRow( "bounds", 1000 )]
	[DataRow( "animated", 1000 )]
	[DataRow( "controls", 100 )]
	[DataRow( "solids", 1000 )]
	[DataRow( "sprites", 1000 )]
	[DataRow( "labels", 100 )]
	[DataRow( "mixed", 1000 )]
	[DataRow( "movingcursor", 1000 )]
	[DataRow( "coloredsprites", 1000 )]
	[DataRow( "linesphere", 100 )]
	[DataRow( "solidsphere", 100 )]
	[DataRow( "linesphere129", 2 )]
	[DataRow( "solidsphere129", 2 )]
	public void Measure( string workload, int count )
	{
		var directory = Environment.GetEnvironmentVariable( "SBOX_GIZMO_BENCHMARK_OUTPUT" );
		if ( string.IsNullOrWhiteSpace( directory ) )
			Assert.Inconclusive( "Set SBOX_GIZMO_BENCHMARK_OUTPUT to collect measurements." );

		ThreadSafe.MarkMainThread();
		Directory.CreateDirectory( directory );
		using var gizmo = new Gizmo.Instance();
		using var camera = new SceneCamera
		{
			Position = new Vector3( -2048, 0, 0 ),
			Rotation = Rotation.Identity,
			Size = new Vector2( Width, Height ),
			Ortho = true,
			OrthoHeight = 768,
			ZNear = 1,
			ZFar = 4096,
			BackgroundColor = new Color( 0.025f, 0.03f, 0.04f )
		};
		var world = new SceneWorld();
		camera.World = world;
		camera.DebugWorlds.Add( gizmo.World );
		gizmo.Input = new Gizmo.Inputs
		{
			Camera = camera,
			CursorRay = new Ray( camera.Position + Vector3.Up * 1000, Vector3.Forward ),
			CursorPosition = new Vector2( -1000 ),
			IsHovered = true
		};

		var objects = new object[count];
		var transforms = new Transform[count];
		var positions = new Vector2[count];
		var columns = (int)Math.Ceiling( Math.Sqrt( count * (float)Width / Height ) );
		var rows = (count + columns - 1) / columns;
		for ( var i = 0; i < count; ++i )
		{
			objects[i] = new object();
			var y = ((i % columns + 0.5f) / columns - 0.5f) * 1200;
			var z = ((i / columns + 0.5f) / rows - 0.5f) * 680;
			transforms[i] = new Transform( new Vector3( 0, y, z ), Rotation.FromYaw( i % 4 * 15 ), 1 );
			positions[i] = new Vector2( (i % columns) * Width / columns, (i / columns) * Height / rows );
		}

		var bounds = BBox.FromPositionAndSize( Vector3.Zero, 16 );
		var samples = new Sample[MeasuredFrames];
		using var target = Graphics.IsAvailable ? Texture.CreateRenderTarget().WithSize( Width, Height )
			.WithFormat( ImageFormat.RGBA8888 ).Create( "Gizmo benchmark" ) : null;
		var view = new ViewSetup { EnablePostprocessing = false, EnableUI = false, EnableDebugOverlays = true };
		var tick = 0;
		GpuProfilerStats.Enabled = Graphics.IsAvailable;
		try
		{
			for ( var frame = -WarmupFrames; frame < MeasuredFrames; ++frame )
			{
				if ( target is not null ) EngineGlobal.SourceEnginePanelAppFrame();
				var bytes = GC.GetAllocatedBytesForCurrentThread();
				var start = Stopwatch.GetTimestamp();
				tick = frame;
				DrawFrame();
				var built = Stopwatch.GetTimestamp();
				var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
				if ( target is not null )
				{
					camera.RenderToTexture( target, null, view );
					Graphics.FlushGPU();
					EngineLoop.DrainFrameEndDisposables();
					RenderTarget.EndOfFrame();
					EngineLoop.RunAsyncTasks();
					GpuProfilerStats.Update();
				}
				var finished = Stopwatch.GetTimestamp();
				if ( frame >= 0 )
					samples[frame] = new Sample( allocated,
						Stopwatch.GetElapsedTime( start, built ).TotalMilliseconds,
						Stopwatch.GetElapsedTime( start, finished ).TotalMilliseconds,
						GpuProfilerStats.BusyMs );
			}

			var name = $"{workload}-{count}";
			var result = new
			{
				Workload = workload,
				Count = count,
				WarmupFrames,
				MeasuredFrames,
				Width,
				Height,
				Graphics = Graphics.IsAvailable,
				Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
				Objects = gizmo.World.SceneObjects.Count,
				Created = gizmo.ObjectsCreated,
				Destroyed = gizmo.ObjectsDestroyed,
				Samples = samples
			};
			File.WriteAllText( Path.Combine( directory, name + ".json" ), JsonSerializer.Serialize( result, new JsonSerializerOptions { WriteIndented = true } ) );
			if ( target is not null )
			{
				using var bitmap = new Bitmap( Width, Height );
				var pixels = target.GetPixels();
				Assert.IsTrue( pixels.Any( x => x.a != 0 ), "Render target was never drawn." );
				if ( workload != "scopes" )
					Assert.IsTrue( pixels.Count( x => x.r > 64 || x.g > 64 || x.b > 64 ) > 100, "Expected visible gizmos." );
				bitmap.SetPixels( pixels.Select( x => (Color)x ).ToArray() );
				File.WriteAllBytes( Path.Combine( directory, name + ".png" ), bitmap.ToPng() );
			}
			Console.WriteLine( $"{name}: {samples.Average( x => x.AllocatedBytes ):N0} B/frame, {samples.Average( x => x.BuildMs ):F3} ms build, {result.Objects} scene objects" );
		}
		finally
		{
			GpuProfilerStats.Enabled = false;
			camera.DebugWorlds.Clear();
			gizmo.Dispose();
			world.Delete();
			SceneWorld.FlushQueuedDeletes();
		}

		void DrawFrame()
		{
			if ( workload == "movingcursor" ) gizmo.Input.CursorRay = new Ray( camera.Position + Vector3.Up * 1000 + Vector3.Left * MathF.Sin( tick * .05f ) * 100, Vector3.Forward );
			using var frame = gizmo.PushScope();
			for ( var i = 0; i < count; ++i )
			{
				var transform = transforms[i];
				if ( workload == "animated" ) transform.Position += Vector3.Up * MathF.Sin( tick * 0.05f + i ) * 8;
				using var scope = Gizmo.ObjectScope( objects[i], transform );
				Gizmo.Draw.Color = Color.White;
				switch ( workload )
				{
					case "scopes":
						using ( Gizmo.Scope( "bounds" ) )
						using ( Gizmo.Scope( "outline" ) )
							Gizmo.Hitbox.BBox( bounds );
						break;
					case "animated":
					case "movingcursor":
					case "bounds":
						Gizmo.Hitbox.BBox( bounds );
						Gizmo.Draw.LineBBox( bounds );
						break;
					case "controls":
						Gizmo.Control.UpdatePosition( "position", Vector3.Zero, out _ );
						Gizmo.Draw.Position( "position" );
						break;
					case "solids":
						Gizmo.Draw.SolidBox( bounds );
						break;
					case "coloredsprites":
						Gizmo.Draw.Color = new Color( (i % 7 + 1) / 8f, (i % 5 + 1) / 6f, .8f, .75f );
						Gizmo.Draw.Sprite( Vector3.Zero, 12, Texture.White );
						break;
					case "linesphere":
						Gizmo.Draw.LineSphere( Vector3.Zero, 16, 12 );
						break;
					case "solidsphere":
						Gizmo.Draw.SolidSphere( Vector3.Zero, 16, 12, 8 );
						break;
					case "linesphere129":
						Gizmo.Draw.LineSphere( Vector3.Zero, 128, 129 );
						break;
					case "solidsphere129":
						Gizmo.Draw.SolidSphere( Vector3.Zero, 128, 129, 129 );
						break;
					case "sprites":
						Gizmo.Draw.Sprite( Vector3.Zero, 12, Texture.White );
						break;
					case "labels":
						Gizmo.Draw.ScreenText( "Gizmo label", positions[i], size: 12 );
						break;
					case "mixed":
						Gizmo.Draw.Color = new Color( 0.25f, 0.65f, 1f, 0.65f );
						Gizmo.Draw.LineBBox( bounds );
						Gizmo.Draw.IgnoreDepth = true;
						Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( 0, 8 ) );
						Gizmo.Draw.Sprite( Vector3.Zero, 4, Texture.White );
						break;
				}
			}
		}
	}

	public readonly record struct Sample( long AllocatedBytes, double BuildMs, double FrameMs, double GpuMs );
}
