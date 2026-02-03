using System.Diagnostics;
using System.Threading;
using static Sandbox.Diagnostics.PerformanceStats;

namespace Sandbox;

internal static partial class Api
{
	internal class Performance
	{
		static FastTimer time = FastTimer.StartNew();

		static float lastFrame;
		static float startTime;

		static int FrameCount;

		static int[] FrameBucket = new int[10];
		static Dictionary<string, PeriodMetric> Stages = new Dictionary<string, PeriodMetric>( 16 );
		static Dictionary<string, List<double>> statDict = new( 16 );

		static Lock Lock = new Lock();

		public static void Frame()
		{
			var t = time.Elapsed.Milliseconds;
			var delta = (t - lastFrame);
			if ( delta < 0 ) delta = 0;

			// new, bucketed frames
			int bucketId = (delta / 10).FloorToInt();
			if ( bucketId >= 9 ) bucketId = 9;
			FrameBucket[bucketId]++;

			lock ( Lock )
			{
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
				CollectStat( "GC0", PerformanceStats.Gen0Collections );
				CollectStat( "GC1", PerformanceStats.Gen1Collections );
				CollectStat( "GC2", PerformanceStats.Gen2Collections );
				CollectStat( "Exceptions", PerformanceStats.Exceptions );
			}

			lastFrame = t;
			FrameCount++;
		}

		/// <summary>
		/// Collect a statistic. This should usually be called ONCE per frame, per stat.
		/// </summary>
		internal static void CollectStat( string name, double value )
		{
			if ( !statDict.TryGetValue( name, out var list ) )
			{
				list = new List<double>();
				statDict[name] = list;
			}

			list.Add( value );
		}

		private static void FlipStat( Timings stat )
		{
			var timings = stat.GetMetric( 1 );

			// I think this is a good way to get the metrics, maybe?

			if ( Stages.TryGetValue( stat.Name, out var existing ) )
			{
				Stages[stat.Name] = new PeriodMetric( MathF.Min( existing.Min, timings.Min ),
											MathF.Max( existing.Max, timings.Max ),
											(existing.Avg + timings.Avg) / 2.0f,
											existing.Calls + timings.Calls );
			}
			else
			{
				Stages[stat.Name] = timings;
			}
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

				Process currentProc = Process.GetCurrentProcess();

				var o = new
				{
					Time = delta,
					Frames = FrameCount,
					Avg = msPerFrame,
					Memory = (int)(currentProc.WorkingSet64 / (1024 * 1024)),
					FrameBucket = FrameBucket.ToArray(), // need to copy
					Stages = Stages.Where( x => x.Value.Calls > 0 ).ToDictionary( x => x.Key, x => x.Value ), // need to copy
					Stats = BuildStats(),
				};

				FrameCount = 0;
				startTime = time;
				Stages.Clear();

				foreach ( var e in statDict )
				{
					e.Value.Clear();
				}


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
			return statDict.ToDictionary( x => x.Key, x => ArrayToMetric( x.Value ) );
		}

		/// <summary>
		///  Here we take the array of values and convert them into an object with a bunch of statistics about them.
		///  Basically trying to cover our bases as much as possible, in case we need to show this data later on.
		/// </summary>
		private static object ArrayToMetric( List<double> data )
		{
			var count = data.Count;
			var min = data.Min();
			var max = data.Max();
			var sum = data.Sum();
			var mean = data.Average();
			double variance = data.Select( val => (val - mean) * (val - mean) ).Sum() / (count - 1);
			double stdDeviation = Math.Sqrt( variance );

			return new
			{
				Cnt = data.Count,
				Min = min,
				Max = max,
				Sum = sum,
				Avg = mean,
				Dev = stdDeviation,
			};
		}
	}
}
