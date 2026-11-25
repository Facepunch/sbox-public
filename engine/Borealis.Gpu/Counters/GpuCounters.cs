using System.Linq;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>A hardware counter GPUPerfAPI offers on the device.</summary>
/// <param name="Index">Its index in GPUPerfAPI's list.</param>
/// <param name="Name">Its name, as GPUPerfAPI's documentation gives it (<c>GPUTime</c>, <c>L2CacheHit</c>).</param>
/// <param name="Group">GPUPerfAPI's group for it (Timing, MemoryCache, ...).</param>
/// <param name="Description">GPUPerfAPI's description.</param>
/// <param name="Unit">What its value counts.</param>
/// <param name="Discrete">Whether it gives one value per sample (the only kind sampled here).</param>
public readonly record struct GpuCounterInfo( int Index, string Name, string Group, string Description, GpuCounterUnit Unit, bool Discrete )
{
	internal bool Float { get; init; }

	/// <summary>Whether its values add up over a scope's parts (times, counts, bytes), rather than being a share or a ratio.</summary>
	public bool Additive => Unit is not (GpuCounterUnit.Percentage or GpuCounterUnit.Ratio);
}

/// <summary>What a <see cref="GpuCounterInfo"/> counts.</summary>
public enum GpuCounterUnit
{
	/// <summary>A ratio of two values.</summary>
	Ratio,

	/// <summary>A percentage, usually 0 to 100.</summary>
	Percentage,

	/// <summary>Clock cycles.</summary>
	Cycles,

	/// <summary>Milliseconds.</summary>
	Milliseconds,

	/// <summary>Bytes.</summary>
	Bytes,

	/// <summary>A count of things: vertices, primitives, pixels, waves, requests.</summary>
	Items,

	/// <summary>Kilobytes.</summary>
	Kilobytes,

	/// <summary>Nanoseconds.</summary>
	Nanoseconds,
}

/// <summary>
/// Hardware performance counters per GPU scope, from AMD's GPUPerfAPI (GPA), where the device was made with
/// <see cref="DeviceOptions.GpuCounters"/>: each scope a pass timer times (Borealis's passes, the caller's own) becomes a GPA
/// sample. GPA needs several passes over the same work for most counter sets, one frame each, so a scene sampled should
/// hold still for <see cref="PassCount"/> frames per sample. <see cref="Capture"/> samples, <see cref="Results"/> reads.
/// </summary>
/// <remarks>
/// Lists on the graphics queue are sampled; lists on the async compute queue aren't (run without async compute to see its
/// passes). Scopes nested in a sampled scope in the same list aren't sampled themselves: they're part of the outer one.
/// </remarks>
public sealed unsafe class GpuCounters
{
	/// <summary>The counter sets <see cref="DeviceOptions.GpuCounters"/> names; counters a device doesn't have are left out.</summary>
	public static readonly IReadOnlyDictionary<string, string[]> Presets = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase )
	{
		["default"] =
		[
			"GPUTime", "GPUBusy", "VsGsBusy", "PSBusy", "CSBusy",
			"VALUBusy", "SALUBusy", "VALUInstsPerWave", "SALUInstsPerWave", "VMemInstsPerWave", "WavesLaunched", "LDSBankConflictCycles",
			"TexUnitBusy", "MemUnitBusy", "MemUnitStalled", "WriteUnitStalled", "FetchSize", "WriteSize",
			"L0CacheHit", "L1CacheHit", "L2CacheHit", "ScalarCacheHit", "InstCacheHit",
			"PrimitivesIn", "CulledPrims", "PrimitiveAssemblyBusy", "PSPixelsOut", "PSExportStalls",
			"PreZSamplesPassing", "PreZSamplesFailingZ", "PostZSamplesPassing", "PostZSamplesFailingZ", "DepthStencilTestBusy", "ZUnitStalled",
			"CSWavefrontsLaunched", "CSThreadsLaunched", "CSLDSBankConflict",
		],
		["timing"] = ["GPUTime", "GPUBusy", "VsGsBusy", "PSBusy", "CSBusy", "TexUnitBusy", "MemUnitBusy", "DepthStencilTestBusy", "PrimitiveAssemblyBusy"],
		["memory"] =
		[
			"GPUTime", "TexUnitBusy", "MemUnitBusy", "MemUnitStalled", "WriteUnitStalled", "FetchSize", "WriteSize", "LocalVidMemBytes",
			"L0CacheHit", "L1CacheHit", "L2CacheHit", "ScalarCacheHit", "InstCacheHit", "L0TagConflictReadStalledCycles", "TexTriFilteringPct", "TexAveAnisotropy",
		],
		["raster"] =
		[
			"GPUTime", "PrimitivesIn", "CulledPrims", "ClippedPrims", "PrimitiveAssemblyBusy", "PAStalledOnRasterizer", "PSPixelsOut", "PSExportStalls",
			"HiZQuadsCulled", "PreZQuadsCulled", "PostZQuads", "PreZSamplesPassing", "PreZSamplesFailingZ", "PostZSamplesPassing", "PostZSamplesFailingZ",
			"DepthStencilTestBusy", "ZUnitStalled", "DBMemRead", "DBMemWritten",
		],
		["compute"] =
		[
			"GPUTime", "CSBusy", "VALUBusy", "SALUBusy", "VALUInstsPerWave", "SALUInstsPerWave", "WavesLaunched", "CSThreadGroupsLaunched", "CSWavefrontsLaunched", "CSThreadsLaunched",
			"CSALUStalledByLDS", "CSLDSBankConflict", "MemUnitBusy", "MemUnitStalled", "FetchSize", "WriteSize", "L0CacheHit", "L2CacheHit",
		],
	};

	readonly nint context;
	readonly object gate = new();
	readonly int[] enabled;
	readonly bool[] enabledFloat;
	readonly int busyCycles;

	// Where each shown counter comes from: its position among the enabled counters, or a composite and the positions of its
	// events' instances
	readonly (int Position, Composite Composite, int[][] Events)[] sources;

	/// <summary>
	/// A counter made of GPA's raw shader counters (exposed with hardware counters on): each event summed over every
	/// workgroup processor (<c>SQWGP0_..SQWGPn_</c>), then put together with the GPU's busy cycles.
	/// </summary>
	internal sealed record Composite( string Name, GpuCounterUnit Unit, string Description, string[] Events, Func<double[], double, GpuCounters, double> Value )
	{
		static readonly System.Text.RegularExpressions.Regex Instance = new( @"^SQWGP(\d+)_(.+)$" );

		/// <summary>Per event, the indices of its instances in <paramref name="available"/>.</summary>
		public List<int>[] Instances( IReadOnlyList<GpuCounterInfo> available )
		{
			var lists = Events.Select( _ => new List<int>() ).ToArray();
			foreach ( var c in available )
			{
				var m = Instance.Match( c.Name );
				if ( !m.Success || !c.Discrete ) continue;
				var e = Array.IndexOf( Events, m.Groups[2].Value );
				if ( e >= 0 ) lists[e].Add( c.Index );
			}

			return lists;
		}
	}

	static double Ratio( double a, double b ) => b > 0 ? a / b : 0;

	/// <summary>The composites: VALU and SALU work, waves and occupancy, LDS bank conflicts.</summary>
	internal static readonly Composite[] Composites =
	[
		// GPUBusyCycles is summed over the shader engines (checked on RDNA3: over one engine's share it gives the 2.1-2.6 GHz
		// clock at peak), so a SIMD's busy cycles are it over the engines. VALU issue cycles over every SIMD's busy cycles (4
		// SIMD32s per WGP); scalar instructions at one a cycle
		new( "VALUBusy", GpuCounterUnit.Percentage, "Share of the busy time the SIMDs spent issuing vector ALU instructions (all WGPs' SQ_PERF_SEL_INST_CYCLES_VALU over busy cycles x SIMDs)",
			["SQ_PERF_SEL_INST_CYCLES_VALU"], ( e, busy, g ) => 100 * Ratio( e[0], busy / g.ShaderEngines * g.Simds ) ),
		new( "SALUBusy", GpuCounterUnit.Percentage, "Share of the busy time the SIMDs spent issuing scalar ALU instructions, at one a cycle (SQ_PERF_SEL_INSTS_SALU over busy cycles x SIMDs)",
			["SQ_PERF_SEL_INSTS_SALU"], ( e, busy, g ) => 100 * Ratio( e[0], busy / g.ShaderEngines * g.Simds ) ),
		new( "VALUInstsPerWave", GpuCounterUnit.Ratio, "Vector ALU instructions per wave", ["SQ_PERF_SEL_INSTS_VALU", "SQ_PERF_SEL_WAVES"], ( e, _, _ ) => Ratio( e[0], e[1] ) ),
		new( "SALUInstsPerWave", GpuCounterUnit.Ratio, "Scalar ALU instructions per wave", ["SQ_PERF_SEL_INSTS_SALU", "SQ_PERF_SEL_WAVES"], ( e, _, _ ) => Ratio( e[0], e[1] ) ),
		new( "VMemInstsPerWave", GpuCounterUnit.Ratio, "Texture and buffer load instructions per wave", ["SQ_PERF_SEL_INSTS_TEX_LOAD", "SQ_PERF_SEL_WAVES"], ( e, _, _ ) => Ratio( e[0], e[1] ) ),
		new( "WavesLaunched", GpuCounterUnit.Items, "Waves launched, every shader stage", ["SQ_PERF_SEL_WAVES"], ( e, _, _ ) => e[0] ),
		new( "LDSBankConflictCycles", GpuCounterUnit.Cycles, "Cycles LDS accesses stalled on bank conflicts, every stage, summed over the WGPs", ["SQC_PERF_SEL_LDS_BANK_CONFLICT"], ( e, _, _ ) => e[0] ),
	];

	/// <summary>SIMDs on the GPU: four per workgroup processor.</summary>
	internal int Simds { get; }

	/// <summary>Shader engines on the GPU, from GPA's per-engine SQG counters.</summary>
	internal int ShaderEngines { get; }
	readonly Action<string> note;

	/// <summary>Every counter GPA offers on the device, derived and (with hardware counters on) raw.</summary>
	public IReadOnlyList<GpuCounterInfo> Available { get; }

	/// <summary>The counters sampled, in the order results give them.</summary>
	public IReadOnlyList<GpuCounterInfo> Counters { get; }

	/// <summary>Counters asked for that the device doesn't have.</summary>
	public IReadOnlyList<string> Missing { get; }

	/// <summary>Frames GPA needs per sample of all <see cref="Counters"/>.</summary>
	public int PassCount { get; }

	/// <summary>GPA's name for the GPU, its hardware generation, wave slots, VGPRs per SIMD and LDS bytes per workgroup processor.</summary>
	public (string Name, string Generation, int WaveSlots, int Vgprs, int LdsBytes) Hardware { get; }

	/// <summary>Where GPUPerfAPI was loaded from.</summary>
	public string Library => Gpa.LoadedFrom;

	/// <summary>Whether <see cref="DeviceOptions.GpuCounters"/> asked for the counter list (<c>list</c>).</summary>
	public bool ListRequested { get; }

	internal GpuCounters( Device device, nint context, string selection, Action<string> note )
	{
		this.context = context;
		this.note = note;

		byte* text;
		int generation = 0;
		uint waveSlots = 0, vgprs = 0, lds = 0;
		var name = Gpa.GetDeviceName( context, &text ) == Gpa.Ok ? Gpa.Text( text ) : device.Adapter.Name;
		Gpa.GetDeviceGeneration( context, &generation );
		Gpa.GetDeviceMaxWaveSlots( context, &waveSlots );
		Gpa.GetDeviceMaxVgprs( context, &vgprs );
		Gpa.GetDeviceMaxLdsBytes( context, &lds );
		Hardware = (name, Gpa.Generation( generation ), (int)waveSlots, (int)vgprs, (int)lds);

		// GPA 4 lists counters per session: list them from one made for the purpose
		nint session;
		Check( Gpa.CreateSession( context, Gpa.SessionDiscrete, &session ), "GpaCreateSession" );
		try
		{
			uint count;
			Check( Gpa.GetNumCounters( session, &count ), "GpaGetNumCounters" );
			var available = new List<GpuCounterInfo>( (int)count );
			for ( uint i = 0; i < count; i++ )
			{
				byte* counterName, group, description;
				int type, usage, sampleType;
				Gpa.GetCounterName( session, i, &counterName );
				Gpa.GetCounterGroup( session, i, &group );
				Gpa.GetCounterDescription( session, i, &description );
				Gpa.GetCounterDataType( session, i, &type );
				Gpa.GetCounterUsageType( session, i, &usage );
				Gpa.GetCounterSampleType( session, i, &sampleType );
				available.Add( new GpuCounterInfo( (int)i, Gpa.Text( counterName ), Gpa.Text( group ), Gpa.Text( description ), (GpuCounterUnit)usage, (sampleType & 1) != 0 ) { Float = type == Gpa.DataFloat64 } );
			}

			Available = available;
			Simds = 4 * Math.Max( 1, available.Count( c => c.Name.StartsWith( "SQWGP", StringComparison.Ordinal ) && c.Name.EndsWith( "_SQ_PERF_SEL_WAVES", StringComparison.Ordinal ) ) );
			ShaderEngines = Math.Max( 1, available.Count( c => System.Text.RegularExpressions.Regex.IsMatch( c.Name, @"^SQG\d+_PERF_SEL_WAVES$" ) ) );
			var (wanted, list) = Parse( selection );
			ListRequested = list;
			var byName = new Dictionary<string, GpuCounterInfo>( StringComparer.OrdinalIgnoreCase );
			foreach ( var c in available ) byName.TryAdd( c.Name, c );

			// What's shown: GPA's counters by name, and composites of the raw shader counters (Composites); GPU time first, as
			// it weights the percentages when scopes are added together
			var shown = new List<(GpuCounterInfo Info, Composite Composite)>();
			var missing = new List<string>();
			if ( byName.TryGetValue( "GPUTime", out var time ) ) shown.Add( (time, null) );
			foreach ( var w in wanted )
			{
				if ( shown.Any( s => s.Info.Name.Equals( w, StringComparison.OrdinalIgnoreCase ) ) ) continue;
				if ( byName.TryGetValue( w, out var c ) && c.Discrete ) shown.Add( (c, null) );
				else if ( Composites.FirstOrDefault( x => x.Name.Equals( w, StringComparison.OrdinalIgnoreCase ) ) is { } composite && composite.Instances( available ).All( i => i.Count > 0 ) && byName.ContainsKey( "GPUBusyCycles" ) )
					shown.Add( (new GpuCounterInfo( -1, composite.Name, "Composite", composite.Description, composite.Unit, true ), composite) );
				else missing.Add( w );
			}

			Missing = missing;

			// Enable what's shown and what the composites are made of
			var raw = new HashSet<int>();
			var listing = session;
			void Enable( int index )
			{
				if ( raw.Add( index ) ) Check( Gpa.EnableCounter( listing, (uint)index ), $"GpaEnableCounter {available[index].Name}" );
			}

			foreach ( var (info, composite) in shown )
			{
				if ( composite is null ) Enable( info.Index );
				else
				{
					Enable( byName["GPUBusyCycles"].Index );
					foreach ( var instances in composite.Instances( available ) ) foreach ( var i in instances ) Enable( i );
				}
			}

			uint passes, enabledCount;
			Check( Gpa.GetPassCount( session, &passes ), "GpaGetPassCount" );
			Check( Gpa.GetNumEnabledCounters( session, &enabledCount ), "GpaGetNumEnabledCounters" );
			PassCount = (int)passes;

			// Results come in the order GPA reports the enabled counters
			enabled = new int[enabledCount];
			var position = new Dictionary<int, int>();
			for ( uint i = 0; i < enabledCount; i++ )
			{
				uint index;
				Check( Gpa.GetEnabledIndex( session, i, &index ), "GpaGetEnabledIndex" );
				enabled[i] = (int)index;
				position[(int)index] = (int)i;
			}

			enabledFloat = enabled.Select( i => available[i].Float ).ToArray();
			busyCycles = byName.TryGetValue( "GPUBusyCycles", out var busy ) && position.TryGetValue( busy.Index, out var at ) ? at : -1;
			sources = shown.Select( s => s.Composite is null
				? (position[s.Info.Index], (Composite)null, (int[][])null)
				: (-1, s.Composite, s.Composite.Instances( available ).Select( list => list.Select( i => position[i] ).ToArray() ).ToArray()) ).ToArray();
			Counters = shown.Select( s => s.Info ).ToList();
		}
		finally
		{
			Gpa.DeleteSession( session );
		}
	}

	/// <summary>
	/// The counters a selection names, and whether it asks for the list: a preset (<see cref="Presets"/>), counter names, or
	/// both, separated by commas; <c>list</c> also lists every counter; empty is <c>default</c>.
	/// </summary>
	public static (List<string> Counters, bool List) Parse( string selection )
	{
		var names = new List<string>();
		var list = false;
		var parts = (selection ?? "").Split( [',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		if ( parts.Length == 0 || parts.All( p => p.Equals( "list", StringComparison.OrdinalIgnoreCase ) || p is "1" or "on" ) ) parts = [.. parts, "default"];
		foreach ( var part in parts )
		{
			if ( part.Equals( "list", StringComparison.OrdinalIgnoreCase ) ) { list = true; continue; }
			if ( part is "1" or "on" ) continue;
			if ( Presets.TryGetValue( part, out var preset ) )
			{
				foreach ( var n in preset ) if ( !names.Contains( n, StringComparer.OrdinalIgnoreCase ) ) names.Add( n );
			}
			else if ( !names.Contains( part, StringComparer.OrdinalIgnoreCase ) ) names.Add( part );
		}

		return (names, list);
	}

	static void Check( int status, string what )
	{
		if ( status != Gpa.Ok ) throw new GpuException( $"{what} failed: {Gpa.Status( status )}" );
	}

	// Sampling: one GPA session at a time records, a frame per pass; ended sessions wait for the GPU
	sealed class Session
	{
		public nint Id;
		public int Pass;
		public int EndTries;
		public int FramesWaiting;
		public bool Broken;
		public readonly Dictionary<(string, int), uint> Samples = new();
		public readonly Dictionary<string, int> Occurrences = new();
		public readonly List<int> SampledPerPass = new();
	}

	Session recording;
	readonly List<Session> waiting = new();
	int sessionsToStart;
	readonly Dictionary<(string Name, int Occurrence), (double[] Sum, int Count)> totals = new();

	/// <summary>Samples taken and read back since the last <see cref="Capture"/>; samples given up (the work changed between passes, or the GPU never finished it).</summary>
	public (int Done, int Failed) Sessions { get; private set; }

	/// <summary>Whether a capture is still sampling or waiting for results.</summary>
	public bool Capturing
	{
		get { lock ( gate ) return sessionsToStart > 0 || recording is not null || waiting.Count > 0; }
	}

	/// <summary>
	/// Forget earlier results and sample <paramref name="samples"/> times, each over <see cref="PassCount"/> frames, starting
	/// at the next frame. <see cref="Capturing"/> goes false once they're all read.
	/// </summary>
	public void Capture( int samples )
	{
		lock ( gate )
		{
			totals.Clear();
			Sessions = default;
			sessionsToStart = Math.Max( 1, samples );
		}
	}

	/// <summary>Stop sampling: what's being recorded is given up, what's already recorded is still read.</summary>
	public void Stop()
	{
		lock ( gate )
		{
			sessionsToStart = 0;
			if ( recording is not null ) recording.Broken = true;
		}
	}

	/// <summary>The mean per scope over the samples read so far.</summary>
	public GpuCounterResults Results
	{
		get
		{
			lock ( gate ) return GpuCounterResults.Build( Counters, totals.Select( t => (t.Key.Name, t.Key.Occurrence, t.Value.Sum, t.Value.Count) ), Sessions.Done );
		}
	}

	/// <summary>A frame starts (the pass timer's <c>BeginFrame</c>): move to the next pass, end and read sessions.</summary>
	public void BeginFrame()
	{
		lock ( gate )
		{
			ReadWaiting();

			if ( recording is { } r )
			{
				if ( r.Pass < PassCount ) r.Pass++;
				r.Occurrences.Clear();
				if ( r.Pass >= PassCount ) TryEnd( r );
			}

			if ( recording is null && sessionsToStart > 0 && waiting.Count < 4 ) StartSession();
		}
	}

	void StartSession()
	{
		nint id;
		var status = Gpa.CreateSession( context, Gpa.SessionDiscrete, &id );
		if ( status != Gpa.Ok )
		{
			Fail( $"GpaCreateSession failed: {Gpa.Status( status )}" );
			return;
		}

		foreach ( var index in enabled ) Gpa.EnableCounter( id, (uint)index );
		status = Gpa.BeginSession( id );
		if ( status != Gpa.Ok )
		{
			Gpa.DeleteSession( id );
			Fail( $"GpaBeginSession failed: {Gpa.Status( status )}" );
			return;
		}

		sessionsToStart--;
		recording = new Session { Id = id };
		recording.SampledPerPass.Add( 0 );
	}

	void Fail( string why )
	{
		Sessions = Sessions with { Failed = Sessions.Failed + 1 };
		if ( Sessions.Failed <= 3 ) note?.Invoke( why );
		if ( Sessions.Failed >= 8 && Sessions.Done == 0 ) sessionsToStart = 0;
	}

	void TryEnd( Session r )
	{
		// Every pass must have sampled the same scopes, or GPA can't put a sample's counters together
		var consistent = !r.Broken && r.SampledPerPass.Count == PassCount && r.SampledPerPass.All( n => n == r.Samples.Count );
		if ( !consistent )
		{
			Gpa.AbortSession( r.Id );
			Gpa.DeleteSession( r.Id );
			recording = null;
			Fail( r.Broken ? "a counter sample was given up" : $"a counter sample was given up: its passes sampled {string.Join( "/", r.SampledPerPass )} scopes (the frames' work changed)" );
			return;
		}

		var status = Gpa.EndSession( r.Id );
		if ( status == Gpa.Ok )
		{
			recording = null;
			waiting.Add( r );
			return;
		}

		// Lists of the last pass still recording: try again next frame, a few times
		if ( ++r.EndTries < 8 ) return;
		Gpa.AbortSession( r.Id );
		Gpa.DeleteSession( r.Id );
		recording = null;
		Fail( $"GpaEndSession failed: {Gpa.Status( status )}" );
	}

	void ReadWaiting()
	{
		for ( int i = waiting.Count - 1; i >= 0; i-- )
		{
			var s = waiting[i];
			// Not ready yet, or never: a list of the sample's that was never submitted
			var status = Gpa.IsSessionComplete( s.Id );
			if ( status != Gpa.Ok && ++s.FramesWaiting < 600 ) continue;

			waiting.RemoveAt( i );
			if ( status != Gpa.Ok )
			{
				Gpa.DeleteSession( s.Id );
				Fail( $"a counter sample never finished: {Gpa.Status( status )}" );
				continue;
			}

			Read( s );
			Gpa.DeleteSession( s.Id );
		}
	}

	void Read( Session s )
	{
		var values = new ulong[enabled.Length];
		var raw = new double[enabled.Length];
		var size = (nuint)(enabled.Length * sizeof( ulong ));
		var read = 0;
		foreach ( var (key, sample) in s.Samples )
		{
			nuint needed;
			if ( Gpa.GetSampleResultSize( s.Id, sample, &needed ) != Gpa.Ok || needed != size ) continue;
			fixed ( ulong* p = values )
			{
				if ( Gpa.GetSampleResult( s.Id, sample, size, p ) != Gpa.Ok ) continue;
			}

			for ( int i = 0; i < raw.Length; i++ ) raw[i] = enabledFloat[i] ? BitConverter.UInt64BitsToDouble( values[i] ) : values[i];

			if ( !totals.TryGetValue( key, out var total ) ) total = (new double[Counters.Count], 0);
			for ( int c = 0; c < Counters.Count; c++ ) total.Sum[c] += Evaluate( c, raw );

			totals[key] = (total.Sum, total.Count + 1);
			read++;
		}

		if ( read == 0 ) Fail( "a counter sample's results couldn't be read" );
		else Sessions = Sessions with { Done = Sessions.Done + 1 };
	}

	// A shown counter's value in a sample, from the enabled counters' raw values
	double Evaluate( int counter, double[] raw )
	{
		var (position, composite, events) = sources[counter];
		if ( composite is null ) return raw[position];

		var sums = new double[events.Length];
		for ( int e = 0; e < events.Length; e++ ) foreach ( var p in events[e] ) sums[e] += raw[p];
		return composite.Value( sums, busyCycles >= 0 ? raw[busyCycles] : 0, this );
	}

	/// <summary>A list begins: on the graphics queue while a sample records, it joins the sample's current pass.</summary>
	internal void ListBegan( CommandList list )
	{
		list.CounterList = 0;
		list.CounterSampleOpen = false;
		if ( list.Queue != QueueType.Graphics ) return;

		lock ( gate )
		{
			if ( recording is not { } r || r.Pass >= PassCount || r.Broken ) return;
			nint id;
			var status = Gpa.BeginCommandList( r.Id, (uint)r.Pass, list.Handle, Gpa.CommandListPrimary, &id );
			if ( status != Gpa.Ok )
			{
				r.Broken = true;
				note?.Invoke( $"GpaBeginCommandList failed: {Gpa.Status( status )}" );
				return;
			}

			list.CounterList = id;
			list.CounterPass = r.Pass;
			list.CounterSession = r;
		}
	}

	/// <summary>A list ends (before <c>vkEndCommandBuffer</c>): close its part of the sample.</summary>
	internal void ListEnded( CommandList list )
	{
		if ( list.CounterList == 0 ) return;
		lock ( gate )
		{
			if ( list.CounterSampleOpen ) Gpa.EndSample( list.CounterList );
			Gpa.EndCommandList( list.CounterList );
			list.CounterList = 0;
			list.CounterSampleOpen = false;
			list.CounterSession = null;
		}
	}

	/// <summary>
	/// Start sampling scope <paramref name="name"/> in <paramref name="list"/>; false if it isn't (no sample recording, a
	/// list GPA doesn't see, or a scope already open in the list).
	/// </summary>
	public bool BeginScope( CommandList list, string name )
	{
		if ( list.CounterList == 0 || list.CounterSampleOpen ) return false;
		lock ( gate )
		{
			if ( list.CounterSession is not Session r || r.Broken ) return false;
			var pass = list.CounterPass;
			r.Occurrences.TryGetValue( name, out var occurrence );
			r.Occurrences[name] = occurrence + 1;
			var key = (name, occurrence);

			uint sample;
			if ( pass == 0 )
			{
				sample = (uint)r.Samples.Count;
				if ( !r.Samples.TryAdd( key, sample ) ) return false;
			}
			else if ( !r.Samples.TryGetValue( key, out sample ) )
			{
				// A scope the first pass didn't have: the passes won't line up
				r.Broken = r.Broken || pass < PassCount;
				return false;
			}

			var status = Gpa.BeginSample( sample, list.CounterList );
			if ( status != Gpa.Ok )
			{
				r.Broken = true;
				note?.Invoke( $"GpaBeginSample {name} failed: {Gpa.Status( status )}" );
				return false;
			}

			while ( r.SampledPerPass.Count <= pass ) r.SampledPerPass.Add( 0 );
			r.SampledPerPass[pass]++;
			list.CounterSampleOpen = true;
			return true;
		}
	}

	/// <summary>End the scope <see cref="BeginScope"/> began in <paramref name="list"/>.</summary>
	public void EndScope( CommandList list )
	{
		if ( list.CounterList == 0 || !list.CounterSampleOpen ) return;
		lock ( gate )
		{
			Gpa.EndSample( list.CounterList );
			list.CounterSampleOpen = false;
		}
	}

	/// <summary>Give up everything recording and close the context, before the device goes.</summary>
	internal void Close()
	{
		lock ( gate )
		{
			sessionsToStart = 0;
			if ( recording is not null )
			{
				Gpa.AbortSession( recording.Id );
				Gpa.DeleteSession( recording.Id );
				recording = null;
			}

			foreach ( var s in waiting ) Gpa.DeleteSession( s.Id );
			waiting.Clear();
			Gpa.CloseContext( context );
		}
	}

	// Process-wide: GPA initialises once, for the first device that asks
	static int initialised;

	/// <summary>Load GPA and initialise it, before the instance is made; the reason it couldn't, or null.</summary>
	internal static string Prepare()
	{
		if ( Interlocked.CompareExchange( ref initialised, 1, 0 ) != 0 ) return "GPUPerfAPI is already in use by another device in this process";
		var why = Gpa.Load();
		if ( why is not null ) return why;
		var status = Gpa.Initialize( 0 );
		return status == Gpa.Ok ? null : $"GpaInitialize failed: {Gpa.Status( status )}";
	}

	/// <summary>Undo <see cref="Prepare"/> once the device and instance are gone.</summary>
	internal static void Release()
	{
		if ( Interlocked.Exchange( ref initialised, 0 ) == 0 || Gpa.Destroy is null ) return;
		Gpa.Destroy();
	}

	/// <summary>Open a context on a device made with GPA's extensions; null and a note if it can't.</summary>
	internal static GpuCounters Open( Device device, string selection, nint instance, nint physicalDevice, nint vkDevice, Action<string> note )
	{
		// Peak clocks: left alone, the GPU drops its clocks in the idle moments around each sample and sampled work runs two
		// to three times slower than the timeline's; pinned at peak, sampled times match it. BOREALIS_GPU_COUNTERS_CLOCKS=none
		// leaves clocks alone, =stable uses GPA's sustainable clocks
		var clocks = Environment.GetEnvironmentVariable( "BOREALIS_GPU_COUNTERS_CLOCKS" );
		var flags = Gpa.OpenEnableHardwareCounters | (clocks?.ToLowerInvariant() switch { "stable" => 0u, "none" => Gpa.OpenClockModeNone, _ => Gpa.OpenClockModePeak });
		var info = new Gpa.VkContextOpenInfo { Instance = instance, PhysicalDevice = physicalDevice, Device = vkDevice };
		nint context;
		var status = Gpa.OpenContext( &info, flags, &context );
		if ( status != Gpa.Ok )
		{
			note?.Invoke( $"GpaOpenContext failed: {Gpa.Status( status )}" );
			return null;
		}

		try
		{
			return new GpuCounters( device, context, selection, note );
		}
		catch ( GpuException e )
		{
			note?.Invoke( e.Message );
			Gpa.CloseContext( context );
			return null;
		}
	}
}
