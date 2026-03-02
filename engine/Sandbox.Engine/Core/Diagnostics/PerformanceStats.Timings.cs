using Sandbox.Utility;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using static Sandbox.Diagnostics.Performance;

namespace Sandbox.Diagnostics;

public static partial class PerformanceStats
{
	public sealed class Timings
	{
		internal static ConcurrentDictionary<string, Timings> All = new( StringComparer.OrdinalIgnoreCase );

		public static Timings Async { get; } = Get( "Async", "#e9edc9" );
		public static Timings Animation { get; } = Get( "Animation", "#ff70a6" );
		public static Timings Audio { get; } = Get( "Audio", "#bdb2ff" );
		public static Timings Editor { get; } = Get( "Editor", "#7f8188" );
		//	public static Timings Io { get; } = Get( "IO", "#b5838d" );
		public static Timings Input { get; } = Get( "Input", "#e9ff70" );
		//	public static Timings Internal { get; } = Get( "Internal", "#e5e5e5" );
		public static Timings NavMesh { get; } = Get( "NavMesh", "#738D45" );
		public static Timings Network { get; } = Get( "Network", "#809bce" );
		public static Timings Particles { get; } = Get( "Particles", "#f7aef8" );
		public static Timings Physics { get; } = Get( "Physics", "#f37748" );
		public static Timings Render { get; } = Get( "Render", "#8ac926" );
		public static Timings Update { get; } = Get( "Update", "#56cbf9" );
		public static Timings Ui { get; } = Get( "UI", "#b4869f" );
		public static Timings Video { get; } = Get( "Video", "#f5cac3" );
		public static Timings GcPause { get; } = Get( "GcPause", "#00f5d4" );

		/// <summary>
		/// Return a list of the main top tier timings we're interested in
		/// </summary>
		public static IEnumerable<Timings> GetMain() => _main;

		private static readonly ReadOnlyCollection<Timings> _main = BuildMain();

		private static ReadOnlyCollection<Timings> BuildMain()
		{
			var list = new List<Timings> { Async, Animation, Audio, GcPause, Input, NavMesh, Network, Particles, Physics, Render, Update, Ui, Video };
			if ( Application.IsEditor )
				list.Add( Editor );
			return list.AsReadOnly();
		}

		public string Name { get; internal set; }
		public Color Color { get; internal set; }

		Superluminal _superluminal;

		internal static void FlipAll()
		{
			foreach ( var a in All )
			{
				if ( a.Value.IsManualFlip )
					continue;

				a.Value.Flip();
			}
		}

		public static Timings Get( string stage, Color? color = default )
		{
			if ( All.TryGetValue( stage, out var timing ) )
				return timing;

			return All.GetOrAdd( stage, f => new Timings( stage, color ?? Color.White ) );
		}

		internal Timings( string name, Color color )
		{
			Name = name;
			Color = color;
			_superluminal = new Superluminal( Name, color );
		}

		public struct Frame
		{
			public int Calls;
			public float TotalMs;
		}

		public Sandbox.Utility.CircularBuffer<Frame> History { get; } = new( 256 );

		internal void Flip()
		{
			lock ( this )
			{
				var f = new Frame();
				f.Calls = calls;
				f.TotalMs = (float)(ticks * (1_000.0 / Stopwatch.Frequency)) + (float)milliseconds;

				History.PushFront( f );

				calls = 0;
				ticks = 0;
				milliseconds = 0;
			}
		}

		public bool IsManualFlip { get; set; }

		int calls;
		long ticks;
		double milliseconds;

		internal Performance.ScopeSection Scope()
		{
			_superluminal.Start();

			Interlocked.Increment( ref calls );

			return new Performance.ScopeSection()
			{
				Source = this,
				Timer = FastTimer.StartNew(),
				// Only snapshot on main thread — GC.GetTotalPauseDuration() is process-wide,
				// attributing from multiple threads simultaneously would double-subtract.
				GcPauseTicksAtStart = ThreadSafe.IsMainThread ? GC.GetTotalPauseDuration().Ticks : -1
			};
		}

		internal void ScopeFinished( ScopeSection section )
		{
			var elapsedTicks = section.Timer.ElapsedTicks;

			// Subtract any GC pause that occurred during this scope so per-system
			// timings aren't inflated by GC.
			if ( section.GcPauseTicksAtStart >= 0 )
			{
				var elapsedMs = elapsedTicks * (1_000.0 / Stopwatch.Frequency);
				var gcMs = Math.Min(
					TimeSpan.FromTicks( GC.GetTotalPauseDuration().Ticks - section.GcPauseTicksAtStart ).TotalMilliseconds,
					elapsedMs );

				if ( gcMs > 0 )
					elapsedTicks -= (long)(gcMs * Stopwatch.Frequency / 1_000.0);
			}

			Interlocked.Add( ref ticks, elapsedTicks );
			_superluminal.Dispose();
		}

		internal void AddMilliseconds( double ms, int addcalls = 1 )
		{
			milliseconds += ms;
			calls += addcalls;
		}

		public float AverageMs( int frames )
		{
			int count = Math.Min( frames, History.Size );
			if ( count == 0 ) return 0;

			double sum = 0;
			for ( int i = 0; i < count; i++ ) sum += History[i].TotalMs;
			return (float)(sum / count);
		}

		public PeriodMetric GetMetric( int frames )
		{
			int count = Math.Min( frames, History.Size );
			if ( count == 0 ) return default;

			if ( count == 1 )
			{
				var f = History[0];
				return new PeriodMetric( f.TotalMs, f.TotalMs, f.TotalMs, f.Calls );
			}

			float min = float.MaxValue, max = float.MinValue;
			double sum = 0;
			int calls = 0;
			for ( int i = 0; i < count; i++ )
			{
				var f = History[i];
				if ( f.TotalMs < min ) min = f.TotalMs;
				if ( f.TotalMs > max ) max = f.TotalMs;
				sum += f.TotalMs;
				calls += f.Calls;
			}
			return new PeriodMetric( min, max, (float)(sum / count), calls );
		}
	}

}
