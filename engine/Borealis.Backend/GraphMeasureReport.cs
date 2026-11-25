using Borealis.Gpu;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Borealis.Backend;

/// <summary>
/// Writes <see cref="GraphMeasure"/>'s files into its folder, at the start of a device frame: <c>summary.tsv</c>, a line
/// every <see cref="GraphMeasure.Every"/> frames with the totals since the last per frame (planning, layout tracking, barrier
/// calls and what they order); and for each listed frame <c>uses-N.tsv</c> (its texture uses in submission order),
/// <c>resources-N.tsv</c> (every live texture and buffer) and <c>views-N.tsv</c> (the views' visibility sets and the morph atlas).
/// </summary>
internal static class GraphMeasureReport
{
	static BarrierCounts lastBarriers;
	static (long Lists, long Scopes, long Barriers, long FullBarriers, long Resumed) lastRecorded;
	static long lastBarrierTicks;
	static double pairTicks = -1;

	public static void BeginFrame( Device gpu, BackendDevice device )
	{
		if ( !GraphMeasure.Enabled ) return;
		if ( pairTicks < 0 ) Start( gpu );

		var listed = GraphMeasure.BeginFrame();
		if ( listed is not null ) WriteFrame( gpu, device, listed, GraphMeasure.Frame - 1 );
		if ( GraphMeasure.Frame % GraphMeasure.Every == 0 ) WriteSummary( gpu );
	}

	static void Start( Device gpu )
	{
		Directory.CreateDirectory( GraphMeasure.Folder );
		gpu.TimesBarriers = true;

		// What a pair of timestamps costs, taken off the timed calls
		var pairs = 1_000_000;
		var start = Stopwatch.GetTimestamp();
		long sink = 0;
		for ( int i = 0; i < pairs; i++ ) sink += Stopwatch.GetTimestamp() - Stopwatch.GetTimestamp();
		pairTicks = (Stopwatch.GetTimestamp() - start) / (double)pairs / 2;
		GC.KeepAlive( sink );

		lastBarriers = gpu.RecordedBarriers;
		lastRecorded = gpu.Recorded;
		lastBarrierTicks = gpu.BarrierTicks;

		var path = Path.Combine( GraphMeasure.Folder, "summary.tsv" );
		if ( !File.Exists( path ) )
			File.WriteAllText( path, "frame\tframes\trenders\tplans\tplan_us\tplan_alloc_bytes\tsegments\tplan_changes\ttarget_sets\ttarget_changes\tshape_us\ttransitions\ttransition_us\ttransition_barriers\tfixup_us\tfixup_lists\tlists\tscopes\tbarrier_calls\tbarrier_us\tsame_gap\tsame_image_splits\tmemory\timages\tbuffers\tfrom_undefined\tafter_write\tread_to_write\tread_to_read_same\tread_to_read\tbuffers_after_write\tbuffers_read_to_read\ttimer_pair_ns\n" );
	}

	static void WriteSummary( Device gpu )
	{
		var t = GraphMeasure.Take();
		var b = gpu.RecordedBarriers;
		var r = gpu.Recorded;
		var barrierTicks = gpu.BarrierTicks;
		var frames = Math.Max( t.Frames, 1 );
		double Us( double ticks ) => ticks * 1e6 / Stopwatch.Frequency / frames;
		double F( long n ) => n / (double)frames;

		var line = Join(
			GraphMeasure.Frame, t.Frames, F( t.Renders ), F( t.Plans ), Us( t.PlanTicks - t.Plans * pairTicks ), F( t.PlanAllocBytes ), t.Plans == 0 ? 0 : t.Segments / (double)t.Plans,
			t.PlanChanges, t.TargetSets, t.TargetChanges, Us( t.ShapeTicks - t.Plans * pairTicks ),
			F( t.Transitions ), Us( t.TransitionTicks - t.Transitions * pairTicks ), F( t.TransitionBarriers ), Us( t.FixUpTicks ), F( t.FixUpLists ),
			F( r.Lists - lastRecorded.Lists ), F( r.Scopes - lastRecorded.Scopes ), F( b.Calls - lastBarriers.Calls ), Us( barrierTicks - lastBarrierTicks - (b.Calls - lastBarriers.Calls) * pairTicks ),
			F( b.SameGap - lastBarriers.SameGap ), F( b.SameImageSplits - lastBarriers.SameImageSplits ), F( b.Memory - lastBarriers.Memory ), F( b.Images - lastBarriers.Images ), F( b.Buffers - lastBarriers.Buffers ),
			F( b.ImagesFromUndefined - lastBarriers.ImagesFromUndefined ), F( b.ImagesAfterWrite - lastBarriers.ImagesAfterWrite ), F( b.ImagesReadToWrite - lastBarriers.ImagesReadToWrite ),
			F( b.ImagesReadToReadSameLayout - lastBarriers.ImagesReadToReadSameLayout ), F( b.ImagesReadToRead - lastBarriers.ImagesReadToRead ),
			F( b.BuffersAfterWrite - lastBarriers.BuffersAfterWrite ), F( b.BuffersReadToRead - lastBarriers.BuffersReadToRead ), pairTicks * 1e9 / Stopwatch.Frequency );

		File.AppendAllText( Path.Combine( GraphMeasure.Folder, "summary.tsv" ), line + "\n" );

		// The same barrier counts by list name, per frame
		var byList = new StringBuilder();
		foreach ( var (name, c) in gpu.TakeBarriersByList() )
			byList.Append( Join( GraphMeasure.Frame, name, F( c.Calls ), F( c.SameGap ), F( c.SameImageSplits ), F( c.Memory ), F( c.Images ), F( c.Buffers ), F( c.ImagesFromUndefined ), F( c.ImagesAfterWrite ), F( c.ImagesReadToWrite ), F( c.ImagesReadToReadSameLayout ), F( c.ImagesReadToRead ), F( c.BuffersAfterWrite ), F( c.BuffersReadToRead ) ) ).Append( '\n' );
		File.AppendAllText( Path.Combine( GraphMeasure.Folder, "barriers-by-list.tsv" ), byList.ToString() );
		lastBarriers = b;
		lastRecorded = r;
		lastBarrierTicks = barrierTicks;
	}

	static string Join( params object[] values ) => string.Join( '\t', values.Select( v => v is IFormattable f ? f.ToString( null, CultureInfo.InvariantCulture ) : v?.ToString() ) );

	static void WriteFrame( Device gpu, BackendDevice device, List<GraphMeasure.Use> uses, long frame )
	{
		// Submission order: by each use's list, then where in it
		static long Submitted( object list ) => list is CommandList l && l.SubmitOrder > 0 ? l.SubmitOrder : long.MaxValue;
		uses.Sort( ( a, b ) => Submitted( a.List ) != Submitted( b.List ) ? Submitted( a.List ).CompareTo( Submitted( b.List ) ) : a.Order.CompareTo( b.Order ) );

		var text = new StringBuilder( "submit\tlist\torder\trender\tpass\tindex\tgeneration\ttexture\tlayout\tbarrier\n" );
		foreach ( var u in uses )
		{
			var list = u.List as CommandList;
			text.Append( CultureInfo.InvariantCulture, $"{(list?.SubmitOrder ?? 0)}\t{list?.Name}\t{u.Order}\t{u.Render}\t{u.Pass}\t{u.Index}\t{u.Generation}\t{u.Texture}\t{(ImageLayout)u.Layout}\t{(u.Barrier ? 1 : 0)}\n" );
		}

		File.WriteAllText( Path.Combine( GraphMeasure.Folder, $"uses-{frame}.tsv" ), text.ToString() );

		var (textures, buffers) = gpu.ListResources();
		var res = new StringBuilder( "kind\tindex\tgeneration\tname\twidth\theight\tdepth\tlayers\tmips\tsamples\tformat\tusage\tbytes\tsize\tlocation\n" );
		foreach ( var (handle, info, bytes) in textures )
			res.Append( CultureInfo.InvariantCulture, $"texture\t{handle.Index}\t{handle.Generation}\t{info.Name}\t{info.Width}\t{info.Height}\t{info.Depth}\t{info.Layers}\t{info.Mips}\t{info.Samples}\t{info.Format}\t{info.Usage}\t{bytes}\t\t{(info.Content ? "content" : "")}\n" );
		foreach ( var (handle, name, size, usage, location, bytes) in buffers )
			res.Append( CultureInfo.InvariantCulture, $"buffer\t{handle.Index}\t{handle.Generation}\t{name}\t\t\t\t\t\t\t\t{usage}\t{bytes}\t{size}\t{location}\n" );

		File.WriteAllText( Path.Combine( GraphMeasure.Folder, $"resources-{frame}.tsv" ), res.ToString() );
		File.WriteAllText( Path.Combine( GraphMeasure.Folder, $"views-{frame}.tsv" ), device.VisibilityViewsReport() + device.MorphReport() );
	}
}
