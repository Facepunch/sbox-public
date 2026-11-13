using Sandbox.Rendering;
using System.Diagnostics;

namespace Sandbox;

/// <summary>
/// Live stats in the title of a game window drawn on Basalt.Gpu, four times a second: frames a second, the main thread's
/// time in each phase of Basalt's frames, the GPU's frame time, objects, visible objects and draws, the paths drawing, and
/// the camera's position. Each frame only adds the last frame's counters to a sum; the text is made on the update.
/// </summary>
internal sealed class BasaltGpuTitle
{
	const double Interval = 0.25;

	readonly System.Text.StringBuilder text = new( 256 );
	long since = Stopwatch.GetTimestamp();
	int frames;
	ManagedFrameCounters sum;

	/// <summary>Count a presented frame, and every <see cref="Interval"/> seconds set <paramref name="setTitle"/>'s text.</summary>
	public void Frame( BasaltGpuWindow window, BasaltGpuRenderBackend backend, string name, Action<string> setTitle )
	{
		frames++;
		sum.Add( ManagedSceneRendering.LastFrame );

		var now = Stopwatch.GetTimestamp();
		var seconds = Stopwatch.GetElapsedTime( since, now ).TotalSeconds;
		if ( seconds < Interval ) return;

		text.Clear();
		text.Append( $"{name} on Basalt.Gpu | {frames / seconds:0} fps" );

		// Basalt's frames' main thread phases, per frame, where it rendered any
		if ( sum.Renders > 0 )
			text.Append( $" | sync {sum.SyncMs / frames:0.00}, collect {sum.CollectMs / frames:0.00}, prepare {sum.PrepareMs / frames:0.00}, setup {sum.SetupMs / frames:0.00}, record {sum.RecordMs / frames:0.00}, submit {sum.SubmitMs / frames:0.00} ms" );

		if ( window.GpuFrameMs is { } gpu ) text.Append( $" | gpu {gpu:0.00} ms" );

		if ( sum.Renders > 0 )
			text.Append( $" | {sum.Objects / frames:N0} objects, {sum.ObjectsVisible / frames:N0} visible, {sum.TotalDraws / frames:N0} draws" );

		var services = backend.Services;
		text.Append( services.GpuDriven ? services.GpuDrivenVisibility ? " | gpu-driven, visibility" : " | gpu-driven" : " | classic" );

		if ( window.CameraPosition is { } at ) text.Append( $" | ({at.x:0}, {at.y:0}, {at.z:0})" );

		setTitle( text.ToString() );
		since = now;
		frames = 0;
		sum = default;
	}
}
