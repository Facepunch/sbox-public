namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// The main thread categories recorded for every frame, in the order <see cref="FrameHistory.CategoryMs"/> takes.
	/// Idle is the cap wait, which the frame already has.
	/// </summary>
	internal static PerformanceStats.Timings[] Categories => CategoryList.All;

	/// <summary>
	/// A class of its own so it's built on first use: static fields across DevTools's files initialise in no set order,
	/// and the frame ring and summaries size themselves from it.
	/// </summary>
	static class CategoryList
	{
		public static readonly PerformanceStats.Timings[] All = PerformanceStats.Timings.GetMain()
			.Where( t => t != PerformanceStats.Timings.Idle )
			.ToArray();

		public static readonly int GcPause = Array.IndexOf( All, PerformanceStats.Timings.GcPause );
	}

	/// <summary>
	/// The last <see cref="FrameHistory.Capacity"/> frames, newest first. Always recorded - a few floats a frame - so
	/// the dev tools has history the moment it opens instead of starting from nothing.
	/// </summary>
	internal static FrameHistory Frames { get; } = new();

	/// <summary>
	/// The GPU times are the graphics queue's busy time from its passes, not the whole frame's.
	/// </summary>
	internal static bool GpuFromPasses { get; private set; }

	static long _prevAllocated = -1, _prevPauseTicks;
	static int _prevGen0, _prevGen1, _prevGen2;

	/// <summary>
	/// Called once a frame from the engine loop, after the frame pacer has waited.
	/// <paramref name="frameMs"/> is the whole loop iteration, <paramref name="idleMs"/> the part of it spent waiting on the
	/// frame rate cap.
	/// </summary>
	internal static void SampleFrame( double frameMs, double idleMs )
	{
		if ( Application.IsHeadless )
			return;

		EnsureLogCapture();

		// What the dev tools itself cost, so it can be taken back out of the frame it's judging. Timings flip at the start of
		// the frame, so this is the previous frame's - near enough for averages.
		var selfMs = Timing.History.Size > 0 ? Timing.History[0].TotalMs : 0f;

		// The managed heap over this frame. Allocations on every thread, not just this one.
		var allocated = GC.GetTotalAllocatedBytes( false );
		var pauseTicks = GC.GetTotalPauseDuration().Ticks;
		int gen0 = GC.CollectionCount( 0 ), gen1 = GC.CollectionCount( 1 ), gen2 = GC.CollectionCount( 2 );
		var first = _prevAllocated < 0;
		var allocKb = first ? 0 : (allocated - _prevAllocated) / 1024f;
		var gcMs = first ? 0 : (float)TimeSpan.FromTicks( pauseTicks - _prevPauseTicks ).TotalMilliseconds;
		var gcGen = first ? -1 : gen2 != _prevGen2 ? 2 : gen1 != _prevGen1 ? 1 : gen0 != _prevGen0 ? 0 : -1;
		_prevAllocated = allocated;
		_prevPauseTicks = pauseTicks;
		(_prevGen0, _prevGen1, _prevGen2) = (gen0, gen1, gen2);

		// Only a main window's whole frame is timed on the GPU; without one - the editor - the passes say how busy it was
		var gpuMs = PerformanceStats.GpuFrametime;
		GpuFromPasses = gpuMs <= 0 && GpuProfilerStats.BusyMs > 0;
		if ( GpuFromPasses ) gpuMs = GpuProfilerStats.BusyMs;

		Frames.Push( (float)frameMs, (float)idleMs, gpuMs, selfMs, allocKb, gcMs, gcGen );
	}

	/// <summary>
	/// A ring of per-frame timings.
	/// </summary>
	internal sealed class FrameHistory
	{
		public const int Capacity = 512;

		readonly float[] _frame = new float[Capacity];
		readonly float[] _idle = new float[Capacity];
		readonly float[] _gpu = new float[Capacity];
		readonly float[] _hub = new float[Capacity];
		readonly float[] _allocKb = new float[Capacity];
		readonly float[] _gcMs = new float[Capacity];
		readonly sbyte[] _gcGen = new sbyte[Capacity];
		readonly float[] _categories = new float[Capacity * Categories.Length];
		readonly long[] _number = new long[Capacity];
		long _pushed;
		int _head;

		/// <summary>
		/// How many frames are recorded, up to <see cref="Capacity"/>.
		/// </summary>
		public int Count { get; private set; }

		public void Push( float frameMs, float idleMs, float gpuMs, float selfMs, float allocKb = 0, float gcMs = 0, int gcGen = -1 )
		{
			_frame[_head] = frameMs;
			_idle[_head] = idleMs;
			_gpu[_head] = gpuMs;
			_hub[_head] = selfMs;
			_allocKb[_head] = allocKb;
			_gcMs[_head] = gcMs;
			_gcGen[_head] = (sbyte)gcGen;
			_number[_head] = ++_pushed;

			// The categories haven't flipped yet, so what they hold now is this frame's
			var at = _head * Categories.Length;
			for ( int c = 0; c < Categories.Length; c++ )
				_categories[at + c] = Categories[c].CurrentMs;

			// The GC pause category is only booked when the next frame starts; this frame's pause is measured here
			if ( CategoryList.GcPause >= 0 ) _categories[at + CategoryList.GcPause] = gcMs;

			_head = (_head + 1) % Capacity;
			if ( Count < Capacity ) Count++;
		}

		int Index( int age ) => (_head - 1 - age + Capacity * 2) % Capacity;

		/// <summary>
		/// Whole frame time, <paramref name="age"/> frames ago. 0 is the newest.
		/// </summary>
		public float FrameMs( int age ) => _frame[Index( age )];

		/// <summary>
		/// Time spent waiting on the frame rate cap, <paramref name="age"/> frames ago.
		/// </summary>
		public float IdleMs( int age ) => _idle[Index( age )];

		/// <summary>
		/// Frame time minus the cap wait and the dev tools' own cost - the time the engine actually spent on the frame.
		/// </summary>
		public float BusyMs( int age ) => MathF.Max( 0, _frame[Index( age )] - _idle[Index( age )] - _hub[Index( age )] );

		/// <summary>
		/// What the dev tools cost, <paramref name="age"/> frames ago.
		/// </summary>
		public float SelfMs( int age ) => _hub[Index( age )];

		/// <summary>
		/// The latest GPU frame time we knew about, <paramref name="age"/> frames ago. GPU timings arrive a few frames
		/// late, so this lags the CPU side slightly.
		/// </summary>
		public float GpuMs( int age ) => _gpu[Index( age )];

		/// <summary>
		/// Managed memory allocated during the frame, on any thread, in KiB.
		/// </summary>
		public float AllocKb( int age ) => _allocKb[Index( age )];

		/// <summary>
		/// Time the runtime paused for garbage collection during the frame.
		/// </summary>
		public float GcMs( int age ) => _gcMs[Index( age )];

		/// <summary>
		/// The oldest generation collected during the frame, or -1 when there was no collection.
		/// </summary>
		public int GcGen( int age ) => _gcGen[Index( age )];

		/// <summary>
		/// Time in one of the <see cref="Categories"/> during the frame. Categories nest, so they can add up to more than
		/// the frame.
		/// </summary>
		public float CategoryMs( int age, int category ) => _categories[Index( age ) * Categories.Length + category];

		/// <summary>
		/// A running count of frames, so a frame can be found again after newer ones push it along.
		/// </summary>
		public long Number( int age ) => _number[Index( age )];

		/// <summary>
		/// How many frames ago the frame with this <see cref="Number"/> was, or -1 if it's gone from the ring.
		/// </summary>
		public int AgeOf( long number )
		{
			var age = (int)(_pushed - number);
			return age >= 0 && age < Count ? age : -1;
		}

		/// <summary>
		/// A copy that stays as it is, for a paused dev tools to keep drawing.
		/// </summary>
		public FrameHistory Clone()
		{
			var copy = new FrameHistory();
			Array.Copy( _frame, copy._frame, Capacity );
			Array.Copy( _idle, copy._idle, Capacity );
			Array.Copy( _gpu, copy._gpu, Capacity );
			Array.Copy( _hub, copy._hub, Capacity );
			Array.Copy( _allocKb, copy._allocKb, Capacity );
			Array.Copy( _gcMs, copy._gcMs, Capacity );
			Array.Copy( _gcGen, copy._gcGen, Capacity );
			Array.Copy( _categories, copy._categories, _categories.Length );
			Array.Copy( _number, copy._number, Capacity );
			copy._pushed = _pushed;
			copy._head = _head;
			copy.Count = Count;
			return copy;
		}

		/// <summary>
		/// How many of the newest frames add up to at least <paramref name="milliseconds"/>.
		/// </summary>
		public int FramesCovering( float milliseconds )
		{
			float total = 0;
			for ( int i = 0; i < Count; i++ )
			{
				total += _frame[Index( i )];
				if ( total >= milliseconds ) return i + 1;
			}

			return Count;
		}
	}
}
