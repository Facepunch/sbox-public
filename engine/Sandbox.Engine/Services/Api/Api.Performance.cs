using System.Diagnostics;
using System.Threading;
using static Sandbox.Diagnostics.PerformanceStats;

namespace Sandbox;

internal static partial class Api
{
	internal class Performance
	{
		static FastTimer time = FastTimer.StartNew();

		static double lastFrame;
		static float startTime;

		static int FrameCount;

		static int[] FrameBucket = new int[10];
		static Dictionary<string, Accum> Stages = new Dictionary<string, Accum>( 16 );
		static Dictionary<string, Accum> statDict = new( 16 );

		static Lock Lock = new Lock();
		static readonly Diagnostics.GpuPassTimings gpuPassTimings = new();
		static readonly Diagnostics.GpuTimingSamplingWindow gpuSamplingWindow = new();
		static bool wasSamplingGpu;
		static Guid gpuSession;
		static Diagnostics.GpuFrameTimeline gpuTimeline;
		static ulong timelineAfterFrame;
		static double lastTimelineCapture = double.NegativeInfinity;

		[ConVar( "gpu_timing_telemetry", ConVarFlags.Protected, Help = "Sample GPU pass timings for performance reports for one second every thirty seconds" )]
		internal static bool GpuTimingTelemetry { get; set; } = true;

		[ConVar( "gpu_timeline_interval", ConVarFlags.Protected, Help = "Seconds between telemetry timeline captures (minimum 60). Uploaded on the next activity heartbeat. 0 disables timeline uploads." )]
		internal static int GpuTimelineInterval { get; set; } = 300;

		// Called under the Frame lock. GPU samples are deduplicated by completed GPU frame,
		// with their own exposure count because profiling only runs during sampling windows.
		static void CollectGpuStats()
		{
			var allowed = GpuTimingTelemetry && AccountInformation.UseAnalytics && Activity.IsSessionActive
				&& !Application.IsEditor && !Application.IsHeadless && !Application.IsStandalone;

			if ( gpuSession != SessionId )
			{
				gpuSession = SessionId;
				gpuPassTimings.Clear();
				gpuTimeline = null;
				gpuSamplingWindow.Reset();
				wasSamplingGpu = false;
				lastTimelineCapture = double.NegativeInfinity;
			}

			if ( !allowed )
			{
				gpuPassTimings.Clear();
				gpuTimeline = null;
			}

			if ( GpuTimelineInterval <= 0 ) gpuTimeline = null;

			var sampling = gpuSamplingWindow.Update( RealTime.Now, allowed );
			Diagnostics.GpuProfilerStats.Enabled = DebugOverlay.overlay_gpu == 1 || DebugOverlay.overlay_gpu_timeline == 1 || sampling;
			Diagnostics.GpuProfilerStats.Update();

			if ( sampling )
			{
				if ( !wasSamplingGpu )
				{
					timelineAfterFrame = Diagnostics.GpuProfilerStats.FrameId;
					gpuPassTimings.SkipFrame( timelineAfterFrame );
				}
				else
				{
					gpuPassTimings.AddFrame( Diagnostics.GpuProfilerStats.FrameId, Diagnostics.GpuProfilerStats.Rows, Diagnostics.GpuProfilerStats.Renderer );
					if ( gpuTimeline?.Renderer != Diagnostics.GpuProfilerStats.Renderer )
					{
						gpuTimeline = null;
					}

					if ( gpuTimeline is null && GpuTimelineInterval > 0
						&& RealTime.Now - lastTimelineCapture >= Math.Max( 60, GpuTimelineInterval )
						&& Diagnostics.GpuProfilerStats.FrameId > timelineAfterFrame )
					{
						gpuTimeline = Diagnostics.GpuFrameTimeline.Capture();
						if ( gpuTimeline is not null ) lastTimelineCapture = RealTime.Now;
					}
				}
			}

			wasSamplingGpu = sampling;
		}

		struct Accum
		{
			public int Cnt;
			public double Min;
			public double Max;
			public double Sum;
			public double Mean;
			public double M2;
			public long Calls;

			public void Add( double value, long calls = 1 )
			{
				if ( Cnt == 0 )
				{
					Min = value;
					Max = value;
				}
				else
				{
					if ( value < Min ) Min = value;
					if ( value > Max ) Max = value;
				}

				Cnt++;
				Sum += value;
				Calls += calls;

				var d = value - Mean;
				Mean += d / Cnt;
				M2 += d * (value - Mean);
			}

			public object ToMetric() => new
			{
				Cnt,
				Min,
				Max,
				Sum,
				Avg = Mean,
				Dev = Cnt > 1 ? Math.Sqrt( M2 / (Cnt - 1) ) : 0.0,
				Calls,
			};
		}

		public static void Frame()
		{
			var t = time.ElapsedMilliSeconds;
			float delta = (float)(t - lastFrame);
			if ( delta < 0 ) delta = 0;

			// new, bucketed frames
			int bucketId = (delta / 10).FloorToInt();
			if ( bucketId >= 9 ) bucketId = 9;
			FrameBucket[bucketId]++;

			lock ( Lock )
			{
				CollectGpuStats();

				foreach ( var stat in Timings.GetMain() )
				{
					FlipStat( stat );
				}

				var s = FrameStats.Current;
				CollectStat( "ObjectsRendered", s.ObjectsRendered );
				CollectStat( "TrianglesRendered", s.TrianglesRendered );
				CollectStat( "DrawCalls", s.DrawCalls );
				CollectStat( "MaterialChanges", s.MaterialChanges );
				CollectStat( "DisplayLists", s.DisplayLists );
				CollectStat( "SceneViewsRendered", s.SceneViewsRendered );
				CollectStat( "RenderTargetResolves", s.RenderTargetResolves );
				CollectStat( "ObjectsCulledByVis", s.ObjectsCulledByVis );
				CollectStat( "ObjectsCulledByScreenSize", s.ObjectsCulledByScreenSize );
				CollectStat( "ObjectsCulledByFade", s.ObjectsCulledByFade );
				CollectStat( "ObjectsFading", s.ObjectsFading );
				CollectStat( "ShadowedLights", s.ShadowedLightsInView );
				CollectStat( "UnshadowedLights", s.UnshadowedLightsInView );
				CollectStat( "ShadowMaps", s.ShadowMaps );
				CollectStat( "GpuFrametime", PerformanceStats.GpuFrametime );
				CollectStat( "GC0", PerformanceStats.Gen0Collections );
				CollectStat( "GC1", PerformanceStats.Gen1Collections );
				CollectStat( "GC2", PerformanceStats.Gen2Collections );
				CollectStat( "Exceptions", PerformanceStats.Exceptions );
				CollectStat( "NetworkIn", Networking.LocalStats.InBytesPerSecond );
				CollectStat( "NetworkOut", Networking.LocalStats.OutBytesPerSecond );
				CollectStat( "NetworkPing", Networking.LocalStats.Ping );
			}

			lastFrame = t;
			FrameCount++;
		}

		/// <summary>
		/// Collect a statistic. This should usually be called ONCE per frame, per stat.
		/// </summary>
		internal static void CollectStat( string name, double value )
		{
			statDict.TryGetValue( name, out var a );
			a.Add( value, 0 );
			statDict[name] = a;
		}

		private static void FlipStat( Timings stat )
		{
			var m = stat.GetMetric( 1 );

			Stages.TryGetValue( stat.Name, out var a );
			a.Add( m.Avg, m.Calls );
			Stages[stat.Name] = a;
		}

		public static object Flip()
		{
			var time = RealTime.Now;
			var delta = time - startTime;
			if ( delta < 0 ) delta = 0;
			if ( FrameCount <= 0 ) FrameCount = 1;

			lock ( Lock )
			{
				var msPerFrame = (delta * 1000.0f) / ((float)FrameCount);
				var gpuPasses = gpuPassTimings.Flip();

				Process currentProc = Process.GetCurrentProcess();

				var o = new
				{
					Time = delta,
					Frames = FrameCount,
					Avg = msPerFrame,
					Memory = (int)(currentProc.WorkingSet64 / (1024 * 1024)),
					FrameBucket = FrameBucket.ToArray(), // need to copy
					Stages = Stages.Where( x => x.Value.Calls > 0 ).ToDictionary( x => x.Key, x => x.Value.ToMetric() ), // need to copy
					Stats = BuildStats(),
					GpuPasses = AccountInformation.UseAnalytics && GpuTimingTelemetry ? gpuPasses : null,
					GpuTimeline = AccountInformation.UseAnalytics && GpuTimingTelemetry && GpuTimelineInterval > 0 ? gpuTimeline?.ToTelemetry() : null,
				};

				FrameCount = 0;
				gpuTimeline = null;
				startTime = time;
				Stages.Clear();
				statDict.Clear();

				for ( int i = 0; i < FrameBucket.Length; i++ )
				{
					FrameBucket[i] = 0;
				}

				return o;
			}
		}

		/// <summary>
		/// Convert statDict into an object that we can send to the backend.
		/// </summary>
		static object BuildStats()
		{
			return statDict.ToDictionary( x => x.Key, x => x.Value.ToMetric() );
		}
	}
}
