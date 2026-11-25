using System.Diagnostics;
using System.Threading;

namespace Borealis;

/// <summary>
/// Measures what a render graph would be worth (<c>-graph-measure &lt;folder&gt;</c>; with it off, every call is one
/// static check). Every frame: what planning costs and allocates, whether each recorder's plan and each render's transient
/// targets differ from their last, and the time layout tracking takes. Every <c>-graph-measure-every</c> frames (600), one
/// frame's texture uses in recording order, each with its command list, so lifetimes can be worked out offline. The backend
/// adds the device's side and writes the files (<c>GraphMeasureReport</c>).
/// </summary>
internal static class GraphMeasure
{
	public static readonly string Folder = Arg( "-graph-measure" );
	public static readonly bool Enabled = Folder is not null;
	public static readonly int Every = int.TryParse( Arg( "-graph-measure-every" ), out var every ) && every > 0 ? every : 600;

	static string Arg( string name )
	{
		var args = Environment.GetCommandLineArgs();
		var at = Array.FindIndex( args, a => a.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
	}

	/// <summary>Device frames begun since start.</summary>
	public static long Frame { get; private set; }

	/// <summary>Whether this frame's texture uses are listed.</summary>
	public static bool Listing { get; private set; }

	/// <summary>Counts and times over the frames since the last report, added up.</summary>
	public struct Totals
	{
		public long Frames, Renders, Plans, PlanTicks, PlanAllocBytes, PlanChanges, Segments, TargetSets, TargetChanges;
		public long Transitions, TransitionTicks, TransitionBarriers, ShapeTicks, FixUpTicks, FixUpLists;
	}

	static Totals totals;

	/// <summary>The totals since the last call, and start again.</summary>
	public static Totals Take()
	{
		var taken = totals;
		totals = default;
		return taken;
	}

	public static void AddPlan( long ticks, long allocBytes, int segments, bool changed )
	{
		totals.Plans++;
		totals.PlanTicks += ticks;
		totals.PlanAllocBytes += allocBytes;
		totals.Segments += segments;
		if ( changed ) totals.PlanChanges++;
	}

	public static void AddShapes( long ticks ) => totals.ShapeTicks += ticks;

	public static void AddTargets( bool changed )
	{
		totals.TargetSets++;
		if ( changed ) totals.TargetChanges++;
	}

	public static void AddTransitions( long count, long ticks, long barriers )
	{
		Interlocked.Add( ref totals.Transitions, count );
		Interlocked.Add( ref totals.TransitionTicks, ticks );
		Interlocked.Add( ref totals.TransitionBarriers, barriers );
	}

	public static void AddFixUps( long ticks, bool list )
	{
		Interlocked.Add( ref totals.FixUpTicks, ticks );
		if ( list ) Interlocked.Increment( ref totals.FixUpLists );
	}

	/// <summary>One use of a texture by a recording: its list (submission order comes from it), place in that list, the pass.</summary>
	public struct Use
	{
		public object List;
		public int Order;
		public int Render;
		public string Pass;
		public uint Index, Generation;
		public string Texture;
		public int Layout;
		public bool Barrier;
	}

	static readonly List<Use> uses = new();
	static int useOrder;
	static int renders;

	[ThreadStatic] static int threadRender;
	[ThreadStatic] static string threadPass;

	/// <summary>A device frame begins: the uses listed over the last one, if it was listed (else null).</summary>
	public static List<Use> BeginFrame()
	{
		List<Use> listed = null;
		if ( Listing )
		{
			lock ( uses )
			{
				listed = new List<Use>( uses );
				uses.Clear();
				useOrder = 0;
			}
		}

		Frame++;
		totals.Frames++;
		renders = 0;
		Listing = Frame >= Every && Frame % Every == 0;
		return listed;
	}

	/// <summary>A render system begins recording a view: its setup's uses are its render's.</summary>
	public static int BeginRender()
	{
		totals.Renders++;
		threadRender = ++renders;
		threadPass = "setup";
		return threadRender;
	}

	/// <summary>This thread records <paramref name="pass"/> of <paramref name="render"/> next.</summary>
	public static void Pass( int render, string pass )
	{
		threadRender = render;
		threadPass = pass;
	}

	/// <summary>Recording a texture's use (with <paramref name="barrier"/>, one that moved it) into <paramref name="list"/>.</summary>
	public static void Used( object list, uint index, uint generation, string texture, int layout, bool barrier )
	{
		lock ( uses )
			uses.Add( new Use { List = list, Order = useOrder++, Render = threadRender, Pass = threadPass, Index = index, Generation = generation, Texture = texture, Layout = layout, Barrier = barrier } );
	}

	/// <summary>Stopwatch ticks now, when measuring.</summary>
	public static long Now() => Enabled ? Stopwatch.GetTimestamp() : 0;
}
