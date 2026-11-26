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

		static readonly Lock Lock = new();
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
			Diagnostics.GpuProfilerStats.Enabled = Diagnostics.DevTools.WantsGpuTimings || sampling;
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

		/// <summary>
		/// Record this frame's timings and counters for the next activity report.
		/// </summary>
		public static void Frame()
		{
			var t = time.ElapsedMilliSeconds;
			float delta = (float)(t - lastFrame);
			if ( delta < 0 ) delta = 0;

			// new, bucketed frames
			int bucketId = (delta / 10).FloorToInt();
			if ( bucketId >= 9 ) bucketId = 9;

			lock ( Lock )
			{
				CollectGpuStats();

				var main = Timings.Main;
				for ( int i = 0; i < main.Count; i++ )
				{
					FlipStat( main[i] );
				}

				var s = FrameStats.Current;
				AddStat( "ObjectsRendered", s.ObjectsRendered );
				AddStat( "TrianglesRendered", s.TrianglesRendered );
				AddStat( "DrawCalls", s.DrawCalls );
				AddStat( "MaterialChanges", s.MaterialChanges );
				AddStat( "DisplayLists", s.DisplayLists );
				AddStat( "SceneViewsRendered", s.SceneViewsRendered );
				AddStat( "RenderTargetResolves", s.RenderTargetResolves );
				AddStat( "ObjectsCulledByVis", s.ObjectsCulledByVis );
				AddStat( "ObjectsCulledByScreenSize", s.ObjectsCulledByScreenSize );
				AddStat( "ObjectsCulledByFade", s.ObjectsCulledByFade );
				AddStat( "ObjectsFading", s.ObjectsFading );
				AddStat( "ShadowedLights", s.ShadowedLightsInView );
				AddStat( "UnshadowedLights", s.UnshadowedLightsInView );
				AddStat( "ShadowMaps", s.ShadowMaps );
				AddStat( "GpuFrametime", PerformanceStats.GpuFrametime );
				AddStat( "GC0", PerformanceStats.Gen0Collections );
				AddStat( "GC1", PerformanceStats.Gen1Collections );
				AddStat( "GC2", PerformanceStats.Gen2Collections );
				AddStat( "Exceptions", PerformanceStats.Exceptions );
				AddStat( "NetworkIn", Networking.LocalStats.InBytesPerSecond );
				AddStat( "NetworkOut", Networking.LocalStats.OutBytesPerSecond );
				AddStat( "NetworkPing", Networking.LocalStats.Ping );

				FrameBucket[bucketId]++;
				FrameCount++;
			}

			lastFrame = t;
		}

		/// <summary>
		/// Record a batch of samples in the same activity reporting interval.
		/// </summary>
		internal static void CollectStats( ReadOnlySpan<(string Name, double Value)> samples )
		{
			lock ( Lock )
			{
				foreach ( var (name, value) in samples )
				{
					AddStat( name, value );
				}
			}
		}

		// The caller holds Lock so Flip cannot snapshot and clear a partially updated sample.
		private static void AddStat( string name, double value )
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

		/// <summary>
		/// Take a snapshot of the accumulated samples and start the next reporting interval.
		/// </summary>
		public static object Flip()
		{
			lock ( Lock )
			{
				var time = RealTime.Now;
				var delta = time - startTime;
				if ( delta < 0 ) delta = 0;

				var frames = Math.Max( FrameCount, 1 );
				var msPerFrame = (delta * 1000.0f) / frames;
				var gpuPasses = gpuPassTimings.Flip();

				Process currentProc = Process.GetCurrentProcess();

				var o = new
				{
					Time = delta,
					Frames = frames,
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
