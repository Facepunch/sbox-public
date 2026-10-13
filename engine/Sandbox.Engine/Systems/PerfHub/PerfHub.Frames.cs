namespace Sandbox.Diagnostics;

internal static partial class PerfHub
{
	/// <summary>
	/// The last <see cref="FrameHistory.Capacity"/> frames, newest first. Always recorded - three floats a frame - so
	/// the hub has history the moment it opens instead of starting from nothing.
	/// </summary>
	internal static FrameHistory Frames { get; } = new();

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

		// What the hub itself cost, so it can be taken back out of the frame it's judging. Timings flip at the start of
		// the frame, so this is the previous frame's - near enough for averages.
		var hubMs = Timing.History.Size > 0 ? Timing.History[0].TotalMs : 0f;

		Frames.Push( (float)frameMs, (float)idleMs, PerformanceStats.GpuFrametime, hubMs );
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
		int _head;

		/// <summary>
		/// How many frames are recorded, up to <see cref="Capacity"/>.
		/// </summary>
		public int Count { get; private set; }

		public void Push( float frameMs, float idleMs, float gpuMs, float hubMs )
		{
			_frame[_head] = frameMs;
			_idle[_head] = idleMs;
			_gpu[_head] = gpuMs;
			_hub[_head] = hubMs;
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
		/// Frame time minus the cap wait and the hub's own cost - the time the engine actually spent on the frame.
		/// </summary>
		public float BusyMs( int age ) => MathF.Max( 0, _frame[Index( age )] - _idle[Index( age )] - _hub[Index( age )] );

		/// <summary>
		/// What the hub cost, <paramref name="age"/> frames ago.
		/// </summary>
		public float HubMs( int age ) => _hub[Index( age )];

		/// <summary>
		/// The latest GPU frame time we knew about, <paramref name="age"/> frames ago. GPU timings arrive a few frames
		/// late, so this lags the CPU side slightly.
		/// </summary>
		public float GpuMs( int age ) => _gpu[Index( age )];

		/// <summary>
		/// A copy that stays as it is, for a paused hub to keep drawing.
		/// </summary>
		public FrameHistory Clone()
		{
			var copy = new FrameHistory();
			Array.Copy( _frame, copy._frame, Capacity );
			Array.Copy( _idle, copy._idle, Capacity );
			Array.Copy( _gpu, copy._gpu, Capacity );
			Array.Copy( _hub, copy._hub, Capacity );
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
