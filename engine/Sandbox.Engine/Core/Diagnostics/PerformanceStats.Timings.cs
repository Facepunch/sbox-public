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
		public static Timings Idle { get; } = Get( "Idle", "#808080" );
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

		internal static IReadOnlyList<Timings> Main => _main;

		private static readonly ReadOnlyCollection<Timings> _main = BuildMain();

		private static ReadOnlyCollection<Timings> BuildMain()
		{
			var list = new List<Timings> { Async, Animation, Audio, GcPause, Idle, Input, NavMesh, Network, Particles, Physics, Render, Update, Ui, Video };
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

			// Static lambda with the colour passed as state. A capturing lambda here would allocate its closure on
			// entry, before the lookup above, so every scope paid for it even on a hit.
			timing = All.GetOrAdd( stage, static ( s, c ) => new Timings( s, c ?? Color.White ), color );

			// Counted once it's in All, so a reader who sees the count change finds it there. Two threads racing to make
			// the same one count it twice, which only costs the reader a look that finds nothing new.
			Interlocked.Increment( ref _created );
			return timing;
		}

		static int _created;

		/// <summary>
		/// How many timings have been made, so a reader can tell when there are new ones without counting
		/// <see cref="All"/>, which takes every lock it has.
		/// </summary>
		internal static int Created => Volatile.Read( ref _created );

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

		/// <summary>
		/// Time recorded since the last flip - the frame that's in progress. Read at the end of a frame, it's that frame's
		/// whole time, where <see cref="History"/> only has it once the next frame starts.
		/// </summary>
		internal float CurrentMs => (float)(Interlocked.Read( ref ticks ) * (1_000.0 / Stopwatch.Frequency) + milliseconds);

		/// <summary>
		/// Scopes opened since the last flip.
		/// </summary>
		internal int CurrentCalls => Volatile.Read( ref calls );

		/// <summary>
		/// The scope this one was first seen opened inside, on the same thread - so the dev tools can show what a category
		/// is made of. Null for one opened at the top.
		/// </summary>
		internal Timings Parent { get; private set; }

		/// <summary>
		/// The scope open on this thread, if any.
		/// </summary>
		[ThreadStatic] static Timings _open;

		/// <summary>
		/// This scope opened on this thread: note what it's inside, and return the scope to go back to when it closes.
		/// </summary>
		internal Timings Opened()
		{
			var outer = _open;
			if ( outer is not null && outer != this && Parent is null ) Parent = outer;
			_open = this;
			return outer;
		}

		/// <summary>
		/// A scope closed: the one it was opened inside is open again.
		/// </summary>
		internal static void Closed( Timings outer ) => _open = outer;

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
				Outer = Opened(),
				Timer = FastTimer.StartNew(),
				// Only snapshot on main thread — GC.GetTotalPauseDuration() is process-wide,
				// attributing from multiple threads simultaneously would double-subtract.
				GcPauseTicksAtStart = ThreadSafe.IsMainThread ? GC.GetTotalPauseDuration().Ticks : -1
			};
		}

		internal void ScopeFinished( ScopeSection section )
		{
			var elapsedTicks = section.Timer.ElapsedTicks;
			Closed( section.Outer );

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
