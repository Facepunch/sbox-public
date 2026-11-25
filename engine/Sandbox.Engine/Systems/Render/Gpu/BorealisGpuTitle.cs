using Borealis.Backend;
using Sandbox.Rendering;
using System.Diagnostics;

namespace Sandbox;

/// <summary>
/// Live stats in a window's title, four times a second: frames a second, the main thread's time in each phase of Borealis's
/// frames, the GPU's frame time, objects, visible objects and draws, the paths drawing, streamed textures' memory and the
/// camera's position. The game window on Borealis.Gpu shows them, and so do borealislab's windows. Each frame only adds the
/// last frame's counters to a sum; the text is made on the update, in a kept <see cref="System.Text.StringBuilder"/> the
/// game window hands to SDL without making a string of it.
/// </summary>
internal sealed class BorealisGpuTitle( string device = "Borealis.Gpu" )
{
	const double Interval = 0.25;

	readonly System.Text.StringBuilder text = new( 256 );
	long since = Stopwatch.GetTimestamp();
	int frames, gpuFrames;
	double gpuMs;
	ManagedFrameCounters sum;

	/// <summary>Count a frame the game window presented, from its last frame's counters.</summary>
	public void Frame( BorealisGpuWindow window, BorealisGpuRenderBackend backend, string name, Action<System.Text.StringBuilder> setTitle )
		=> Frame( name, ManagedSceneRendering.LastFrame, window.GpuFrameMs, backend.Services, window.CameraPosition, setTitle );

	/// <summary>
	/// As <see cref="Frame(string, in ManagedFrameCounters, double?, BackendDevice, Vector3?, Action{string})"/>, handing over
	/// the kept text itself rather than a string of it.
	/// </summary>
	public void Frame( string name, in ManagedFrameCounters counters, double? gpu, BackendDevice services, Vector3? camera, Action<System.Text.StringBuilder> setTitle )
	{
		if ( Update( name, counters, gpu, services, camera ) )
			setTitle( text );
	}

	/// <summary>
	/// Count a presented frame: what Borealis did in it, the GPU's frame time if known, the backend drawing it (its paths and
	/// streaming; none on Source 2's device) and the camera. Every <see cref="Interval"/> seconds set <paramref name="setTitle"/>'s
	/// text: <paramref name="name"/> on the device, then the stats.
	/// </summary>
	public void Frame( string name, in ManagedFrameCounters counters, double? gpu, BackendDevice services, Vector3? camera, Action<string> setTitle )
	{
		if ( Update( name, counters, gpu, services, camera ) )
			setTitle( text.ToString() );
	}

	/// <summary>Add a frame to the sums; on an update, make the title's text and say so.</summary>
	bool Update( string name, in ManagedFrameCounters counters, double? gpu, BackendDevice services, Vector3? camera )
	{
		frames++;
		sum.Add( counters );
		if ( gpu is { } ms && !double.IsNaN( ms ) )
		{
			gpuMs += ms;
			gpuFrames++;
		}

		var now = Stopwatch.GetTimestamp();
		var seconds = Stopwatch.GetElapsedTime( since, now ).TotalSeconds;
		if ( seconds < Interval ) return false;

		text.Clear();
		text.Append( $"{name} on {device} | {frames / seconds:0} fps" );

		// Borealis's frames' main thread phases, per frame, where it rendered any
		if ( sum.Renders > 0 )
			text.Append( $" | sync {sum.SyncMs / frames:0.00}, collect {sum.CollectMs / frames:0.00}, prepare {sum.PrepareMs / frames:0.00}, setup {sum.SetupMs / frames:0.00}, record {sum.RecordMs / frames:0.00}, submit {sum.SubmitMs / frames:0.00} ms" );

		if ( gpuFrames > 0 ) text.Append( $" | gpu {gpuMs / gpuFrames:0.00} ms" );

		if ( sum.Renders > 0 )
			text.Append( $" | {sum.Objects / frames:N0} objects, {sum.ObjectsVisible / frames:N0} visible, {sum.TotalDraws / frames:N0} draws" );

		if ( services is not null )
		{
			text.Append( services.GpuDriven ? services.GpuDrivenVisibility ? " | gpu-driven, visibility" : " | gpu-driven" : " | classic" );
			if ( services.StreamingStats is { } streaming ) text.Append( $" | textures {streaming.Resident >> 20} of {streaming.Wanted >> 20} MiB, {streaming.Building} building" );
		}

		if ( camera is { } at ) text.Append( $" | ({at.x:0}, {at.y:0}, {at.z:0})" );

		since = now;
		frames = 0;
		gpuFrames = 0;
		gpuMs = 0;
		sum = default;
		return true;
	}
}
