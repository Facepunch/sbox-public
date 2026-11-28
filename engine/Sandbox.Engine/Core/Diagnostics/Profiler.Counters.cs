using System.IO;
using System.Text;

namespace Sandbox.Diagnostics;

/// <summary>
/// Per-frame render, GPU and memory counters, sampled at frame boundaries while a capture records.
/// </summary>
internal static partial class Profiler
{
	internal enum FrameCounter { DrawCalls, Triangles, Batches, Primitives, ObjectsTested, ObjectsDrawn, MaterialChanges, ConstantBufferUpdates, ConstantBufferBytes, GpuMs, WorkingSetMiB }
	internal const int FrameCounterCount = 11;
	internal static readonly string[] FrameCounterNames = ["Draw calls", "Triangles", "Batches", "Primitives", "Objects tested", "Objects drawn", "Material changes", "Constant buffer updates", "Constant buffer bytes", "GPU ms", "Working set MiB"];

	/// <summary>
	/// One frame boundary's counters, at <paramref name="Ms"/> from the capture start: what the frame that just ended
	/// rendered, its GPU time, and the working set. NaN where a value wasn't measured.
	/// </summary>
	internal sealed record CounterSample( double Ms, float[] Values );

	struct RawCounters
	{
		public ulong Ticks;
		public float WorkingSetMiB;
		public float DrawCalls, Triangles, Batches, Primitives, ObjectsTested, ObjectsDrawn, MaterialChanges, ConstantBufferUpdates, ConstantBufferBytes;
		public bool SceneStats;
	}

	// About a day of frames at 60 fps; a streamed capture keeps sampling past its in-memory part
	const int MaxCounterSamples = 1 << 22;

	static void SampleCounters( Builder builder, ulong ticks )
	{
		if ( builder.Counters.Count >= MaxCounterSamples ) return;
		var sample = new RawCounters
		{
			Ticks = ticks,
			WorkingSetMiB = PerformanceStats.ApproximateProcessMemoryUsage / (1024f * 1024f),
		};
		// Unit tests have no scene system
		if ( !Application.IsUnitTest )
		{
			var stats = NativeEngine.CSceneSystem.GetPerFrameStats();
			// The managed renderer's draws in the frame that just ended, which native's counters don't see
			var managed = Rendering.ManagedSceneRendering.ThisFrame;
			sample.SceneStats = true;
			sample.DrawCalls = stats.m_nDrawCalls + managed.TotalDraws;
			sample.Triangles = stats.m_nTrianglesRendered + managed.Triangles;
			sample.Batches = stats.m_nRenderBatchDraws;
			sample.Primitives = stats.m_nDrawPrimitives;
			sample.ObjectsTested = stats.m_nNumObjectsTested + managed.Objects;
			sample.ObjectsDrawn = stats.m_nNumObjectsPassingCullCheck + managed.ObjectsVisible;
			sample.MaterialChanges = stats.m_nMaterialChangesNonShadow;
			sample.ConstantBufferUpdates = stats.m_nNumConstantBufferUpdates;
			sample.ConstantBufferBytes = stats.m_nNumConstantBufferBytes;
		}
		builder.Counters.Add( sample );
	}

	/// <summary>
	/// The raw samples as counters. A GPU frame's time goes to the frame its first submission was made in: the first
	/// sample at or after that host time, which closes that frame.
	/// </summary>
	static CounterSample[] ResolveCounters( List<RawCounters> raw, IReadOnlyList<(ulong HostTicks, float Ms)> gpuFrames, ulong start, double toMs )
	{
		var gpu = new float[raw.Count];
		Array.Fill( gpu, float.NaN );
		foreach ( var (hostTicks, ms) in gpuFrames )
		{
			int lo = 0, hi = raw.Count;
			while ( lo < hi )
			{
				var mid = (lo + hi) / 2;
				if ( raw[mid].Ticks < hostTicks ) lo = mid + 1;
				else hi = mid;
			}
			if ( lo == 0 || lo >= raw.Count ) continue;
			gpu[lo] = float.IsNaN( gpu[lo] ) ? ms : gpu[lo] + ms;
		}

		var result = new CounterSample[raw.Count];
		for ( int i = 0; i < raw.Count; i++ )
		{
			var x = raw[i];
			var values = new float[FrameCounterCount];
			Array.Fill( values, float.NaN );
			if ( x.SceneStats )
			{
				values[(int)FrameCounter.DrawCalls] = x.DrawCalls;
				values[(int)FrameCounter.Triangles] = x.Triangles;
				values[(int)FrameCounter.Batches] = x.Batches;
				values[(int)FrameCounter.Primitives] = x.Primitives;
				values[(int)FrameCounter.ObjectsTested] = x.ObjectsTested;
				values[(int)FrameCounter.ObjectsDrawn] = x.ObjectsDrawn;
				values[(int)FrameCounter.MaterialChanges] = x.MaterialChanges;
				values[(int)FrameCounter.ConstantBufferUpdates] = x.ConstantBufferUpdates;
				values[(int)FrameCounter.ConstantBufferBytes] = x.ConstantBufferBytes;
			}
			values[(int)FrameCounter.GpuMs] = gpu[i];
			if ( x.WorkingSetMiB > 0 ) values[(int)FrameCounter.WorkingSetMiB] = x.WorkingSetMiB;
			result[i] = new( ((double)x.Ticks - start) * toMs, values );
		}
		return result;
	}

	/// <summary>
	/// Each frame's counters: the first sample at or after its end, if it comes before the next frame starts.
	/// </summary>
	internal static float[] FrameCounters( Capture capture, int frame )
	{
		var frames = capture.Frames;
		var samples = capture.CounterSamples;
		if ( frame < 0 || frame >= frames.Length || samples.Length == 0 || frames[frame].Incomplete ) return null;
		var end = frames[frame].EndMs;
		int lo = 0, hi = samples.Length;
		while ( lo < hi )
		{
			var mid = (lo + hi) / 2;
			if ( samples[mid].Ms < end - 0.0005 ) lo = mid + 1;
			else hi = mid;
		}
		if ( lo >= samples.Length ) return null;
		var next = frame + 1 < frames.Length ? frames[frame + 1].StartMs : double.PositiveInfinity;
		return samples[lo].Ms <= next + 0.0005 ? samples[lo].Values : null;
	}

	/// <summary>
	/// A counter over the capture's frames, NaN where not measured.
	/// </summary>
	internal static float FrameCounterValue( Capture capture, int frame, FrameCounter counter ) =>
		FrameCounters( capture, frame ) is { } values ? values[(int)counter] : float.NaN;

	internal static bool HasCounters( Capture capture ) => capture.CounterSamples.Length > 0;

	static void WriteCounters( BinaryWriter writer, CounterSample[] samples )
	{
		writer.Write( FrameCounterCount );
		writer.Write( samples.Length );
		foreach ( var sample in samples )
		{
			writer.Write( sample.Ms );
			foreach ( var value in sample.Values ) writer.Write( value );
		}
	}

	static CounterSample[] ReadCounters( Stream stream, double offsetMs = 0, double durationMs = double.PositiveInfinity )
	{
		using var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );
		var columns = reader.ReadInt32();
		var count = reader.ReadInt32();
		if ( columns is < 1 or > 64 || count < 0 || (long)count * (8 + columns * 4) != stream.Length - stream.Position ) throw new InvalidDataException( "Invalid profiler capture counters." );
		var result = new List<CounterSample>( Math.Min( count, 1 << 16 ) );
		for ( int i = 0; i < count; i++ )
		{
			var ms = reader.ReadDouble();
			var values = new float[FrameCounterCount];
			Array.Fill( values, float.NaN );
			for ( int c = 0; c < columns; c++ )
			{
				var value = reader.ReadSingle();
				if ( c < FrameCounterCount ) values[c] = value;
			}
			if ( !double.IsFinite( ms ) ) throw new InvalidDataException( "Invalid profiler capture counter time." );
			if ( ms < offsetMs || ms > offsetMs + durationMs ) continue;
			result.Add( new( ms - offsetMs, values ) );
		}
		return result.ToArray();
	}

	static T ReadSection<T>( Stream stream, FileSection section, Func<Stream, T> read )
	{
		using var reader = new SectionStream( stream, section );
		return read( reader );
	}
}
