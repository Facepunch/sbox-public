using NativeEngine;
using Sandbox.Diagnostics;
using Sandbox.Rendering;
using System;
using System.Collections.Generic;

namespace EngineTests;

/// <summary>
/// The GPU's busy time per frame (<see cref="BorealisGpuRenderBackend.GpuBusyMs"/>): the pass timer's queries read back on a
/// worker, not on the thread that starts frames, and published every frame while GPU profiling is on.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BorealisGpuBusyTimings
{
	[TestMethod]
	public void BusyTimeIsReadEveryFrameOnAWorker()
	{
		TestGraphics.Require();
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend )
		{
			Assert.Inconclusive( "Borealis.Gpu only" );
			return;
		}

		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( 128, 128 ).WithFormat( ImageFormat.RGBA8888 ).Create( "busy-timings" );
		var camera = new GameObject( true, "camera" ).Components.Create<CameraComponent>();
		var box = new GameObject( true, "box" );
		box.WorldPosition = new Vector3( 200, 0, 0 );
		box.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );

		var profiling = GpuProfilerStats.Enabled;
		GpuProfilerStats.Enabled = true;
		try
		{
			var elsewhere = backend.BusyReadsElsewhere;
			var frames = new List<ulong>();
			for ( int frame = 0; frame < 24; frame++ )
			{
				Application.FrameCount++;
				SceneRenderer.BeginFrame();
				EngineGlobal.SourceEnginePanelAppFrame();
				Assert.IsTrue( camera.RenderToTexture( target ) );
				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				frames.Add( backend.GpuBusyFrame );
			}

			// Once reads have started, each frame's start publishes the frame read at the last one's: a new frame every frame
			var last = frames.GetRange( frames.Count - 8, 8 );
			for ( int i = 1; i < last.Count; i++ ) Assert.AreEqual( last[i - 1] + 1, last[i], $"busy frames read: {string.Join( ", ", frames )}" );
			Assert.IsTrue( backend.GpuBusyMs > 0, "a busy time" );
			Assert.IsTrue( backend.BusyReadsElsewhere - elsewhere > 8, $"reads on a worker: {backend.BusyReadsElsewhere - elsewhere} of {frames.Count} frames" );
		}
		finally
		{
			GpuProfilerStats.Enabled = profiling;
			scene.Destroy();
		}
	}
}
