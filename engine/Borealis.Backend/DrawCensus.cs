using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// A draw census of a frame (<c>borealislab -drawcensus</c>, debug only): every model draw and GPU-driven multi-draw the
/// device's contexts record, by pass (the view and layer scope it's in), with its shader, combos, state and binds, and
/// for draws left to the classic path what they are and why (<see cref="GpuContext.CensusNote"/>). Set
/// <see cref="BackendDevice.Census"/> for one frame, wait for the GPU, <see cref="Resolve"/> the indirect commands it wrote,
/// then read <see cref="Draws"/> and <see cref="Passes"/>. While it's off, contexts check one field per draw.
/// </summary>
internal sealed class DrawCensus
{
	/// <summary>How a draw was recorded.</summary>
	public enum Path
	{
		/// <summary>A model draw on the classic path: its own material bind, buffers and draw or multi-draw.</summary>
		Classic,

		/// <summary>A GPU-driven bucket's multi-draw (one per index page, plus forward's compacted one).</summary>
		Bucket,

		/// <summary>A GPU-driven bucket's multi-draw into the visibility prepass's ids.</summary>
		BucketIds,
	}

	/// <summary>One draw call (a multi-draw counts once), what it drew and what recording it bound.</summary>
	public sealed class Draw
	{
		public string Pass;
		public Path Path;

		/// <summary>Classic draws: what it is and why it isn't GPU-driven, as the feature drawing it said.</summary>
		public string Kind;
		public string Shader, Mode, Material;
		public ulong VertexStatic, PixelStatic, VertexDynamic, PixelDynamic;

		/// <summary>The static combos set (not 0) in the pixel stage, or the vertex stage without one; specialization constants marked *.</summary>
		public string Features;

		/// <summary>The render state that goes in the pipeline: culling, blending, alpha to coverage, depth, stencil, pixel shader.</summary>
		public string State;
		public string Layout;

		/// <summary>The code it runs: a hash of each stage's SPIR-V and of its specialization constants, so variants of other combos with the same code tell.</summary>
		public string Code;

		/// <summary>GPU-driven: the bucket (numbered over the frame), what besides its variant made it one, its materials and batches.</summary>
		public int Bucket = -1;
		public string BucketKey;
		public int BucketMaterials, BucketBatches;

		/// <summary>Draw calls, pipeline and descriptor set binds the command list recorded for it, its bind included.</summary>
		public int Calls, Pipelines, Sets;

		/// <summary>Commands it can draw: 1 for a direct draw, the multi-draw's count or maximum otherwise; instances of a direct draw.</summary>
		public int MaxCommands, Instances;

		/// <summary>Where its indirect commands are, and its count when the GPU writes one (offset -1 when not).</summary>
		public BufferHandle Arguments, CountBuffer;
		public long ArgumentsOffset, CountOffset = -1;

		/// <summary>Commands with instances and triangles drawn: known for direct draws, from the arguments after <see cref="Resolve"/> for multi-draws; -1 if unreadable.</summary>
		public int Commands = -1;
		public long Triangles = -1;
	}

	readonly List<Draw> draws = new();
	readonly Dictionary<string, PassTimer.Recorded> passes = new();
	readonly Dictionary<CommandList, PassTimer.Recorded> marks = new( ReferenceEqualityComparer.Instance );
	readonly Dictionary<string, int> leftClassic = new();
	int buckets;

	/// <summary>The draws, in the order they were recorded per list.</summary>
	public IReadOnlyList<Draw> Draws => draws;

	/// <summary>What each pass's lists recorded in all (draw calls, pipeline binds, set binds, rendering scopes), its inner scopes' apart.</summary>
	public IReadOnlyDictionary<string, PassTimer.Recorded> Passes => passes;

	/// <summary>GPU-driven batches the device left to the classic path, by shader, mode and why.</summary>
	public IReadOnlyDictionary<string, int> LeftClassic => leftClassic;

	internal void Add( Draw draw )
	{
		lock ( draws ) draws.Add( draw );
	}

	internal int NextBucket() => Interlocked.Increment( ref buckets ) - 1;

	internal void Left( string why )
	{
		lock ( leftClassic ) leftClassic[why] = leftClassic.GetValueOrDefault( why ) + 1;
	}

	/// <summary>Put what <paramref name="list"/> recorded since its last mark on <paramref name="pass"/>.</summary>
	internal void Mark( CommandList list, string pass )
	{
		var now = PassTimer.Recorded.Of( list );
		lock ( passes )
		{
			var last = marks.GetValueOrDefault( list );
			if ( now.Draws < last.Draws || now.Pipelines < last.Pipelines || now.Sets < last.Sets ) last = default;
			var since = now.Since( last );
			var total = passes.GetValueOrDefault( pass );
			passes[pass] = new PassTimer.Recorded( total.Draws + since.Draws, total.Pipelines + since.Pipelines, total.Sets + since.Sets, total.Scopes + since.Scopes );
			marks[list] = now;
		}
	}

	/// <summary>Once the GPU has finished the frame: each multi-draw's commands and triangles, from its arguments where they're host-visible.</summary>
	public void Resolve( Device gpu )
	{
		var stride = IndirectDraws.Stride;
		foreach ( var draw in draws )
		{
			if ( !draw.Arguments.IsValid ) continue;
			try
			{
				var arguments = gpu.Map( draw.Arguments );
				var count = draw.MaxCommands;
				if ( draw.CountOffset >= 0 ) count = Math.Min( count, (int)BinaryPrimitives.ReadUInt32LittleEndian( gpu.Map( draw.CountBuffer )[(int)draw.CountOffset..] ) );

				var commands = 0;
				var triangles = 0L;
				for ( int i = 0; i < count; i++ )
				{
					var at = (int)(draw.ArgumentsOffset + (long)i * stride);
					var indices = BinaryPrimitives.ReadUInt32LittleEndian( arguments[at..] );
					var instances = BinaryPrimitives.ReadUInt32LittleEndian( arguments[(at + 4)..] );
					if ( instances == 0 || indices == 0 ) continue;
					commands++;
					triangles += indices / 3 * (long)instances;
				}

				(draw.Commands, draw.Triangles) = (commands, triangles);
			}
			catch ( InvalidOperationException )
			{
				// In device memory: what it drew isn't readable here
			}
		}
	}

	/// <summary>A shader's name without its folder, extension or GPU-driven suffix.</summary>
	public static string ShaderName( string name )
	{
		if ( name is null ) return "(none)";
		name = System.IO.Path.GetFileName( name.Replace( '\\', '/' ) );
		foreach ( var suffix in (ReadOnlySpan<string>)[".shader_c", ".shader", ".gpudriven", ".vfx"] )
		{
			if ( name.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) ) name = name[..^suffix.Length];
		}

		return name;
	}

	/// <summary>Fill in a draw's shader, mode, combos, features and state from its variant.</summary>
	public static void Describe( Draw draw, ShaderVariant variant, bool depthOnly )
	{
		draw.Shader = ShaderName( variant.Shader?.Name );
		draw.Mode = variant.Mode?.Name;
		draw.VertexStatic = variant.Vertex?.Static?.Id ?? 0;
		draw.PixelStatic = variant.Pixel?.Static?.Id ?? 0;
		draw.VertexDynamic = variant.Vertex?.Dynamic?.Id ?? 0;
		draw.PixelDynamic = variant.Pixel?.Dynamic?.Id ?? 0;
		var pixel = !depthOnly && variant.Pixel?.Bytecode is not null;
		draw.Features = Features( variant.Pixel?.Static is not null ? variant.Pixel : variant.Vertex );
		draw.State = State( variant.State, pixel );
		draw.Code = $"{Hash( variant.Vertex )}/{(pixel ? Hash( variant.Pixel ) : "-")}";
	}

	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShaderBytecode, string> hashes = new();

	/// <summary>A stage's SPIR-V and specialization constants, hashed (each bytecode hashed once).</summary>
	static string Hash( ShaderStage stage )
	{
		if ( stage?.Bytecode?.Spirv is not { } spirv ) return "-";
		var code = hashes.GetValue( stage.Bytecode, _ => Fnv( spirv, 0xcbf29ce484222325 ).ToString( "x16", CultureInfo.InvariantCulture ) );
		var constants = 0xcbf29ce484222325UL;
		foreach ( var c in stage.Constants ?? [] ) constants = (constants ^ c.Id) * 0x100000001b3 ^ c.Value;
		return $"{code[..8]}+{constants & 0xFFFFFFFF:x8}";
	}

	static ulong Fnv( ReadOnlySpan<byte> bytes, ulong hash )
	{
		foreach ( var b in bytes ) hash = (hash ^ b) * 0x100000001b3;
		return hash;
	}

	/// <summary>A stage's static combos that aren't 0, as <c>NAME=value</c>; a specialization constant's name ends in *.</summary>
	public static string Features( ShaderStage stage )
	{
		if ( stage?.Static is not { } combo ) return "";
		var sb = new StringBuilder();
		foreach ( var c in stage.Program.StaticCombos )
		{
			if ( c.Stride == 0 ) continue;
			var value = c.Min + (int)(combo.Id / c.Stride % (ulong)Math.Max( c.Count, 1 ));
			if ( value == 0 ) continue;
			if ( sb.Length > 0 ) sb.Append( ' ' );
			sb.Append( c.Name ).Append( c.Specialization ? "*" : "" ).Append( '=' ).Append( value );
		}

		return sb.ToString();
	}

	/// <summary>What of a variant's render state goes in its pipeline, short.</summary>
	public static string State( ShaderPipelineState state, bool pixelShader )
	{
		if ( state is null ) return pixelShader ? "ps" : "no ps";
		var sb = new StringBuilder();
		sb.Append( state.Cull switch { CullMode.None => "two-sided", CullMode.Front => "cull front", _ => "cull back" } );
		if ( state.Translucent ) sb.Append( " blend" );
		if ( state.AlphaToCoverage ) sb.Append( " a2c" );
		if ( !state.DepthTest ) sb.Append( " no-ztest" );
		else if ( !state.DepthWrite ) sb.Append( " no-zwrite" );
		if ( state.DepthTest ) sb.Append( " z" ).Append( state.DepthCompare );
		if ( state.StencilTest ) sb.Append( " stencil" );
		if ( !state.DepthClip ) sb.Append( " zclamp" );
		sb.Append( pixelShader ? " ps" : " no-ps" );
		return sb.ToString();
	}

	/// <summary>A vertex layout, short: stride, fields, vertex cache, and a hash of the whole.</summary>
	public static string LayoutName( object layout ) => layout switch
	{
		VertexLayout v => $"{v.Stride}B/{v.Fields.Length}f{(v.VertexCache ? "/cache" : "")}#{(uint)v.GetHashCode() & 0xFFFF:x4}",
		null => "",
		_ => layout.GetType().Name,
	};

	static readonly string[] Columns = ["pass", "path", "kind", "shader", "mode", "material", "vs_static", "ps_static", "vs_dynamic", "ps_dynamic", "features", "state", "code", "layout", "bucket", "bucket_key", "bucket_materials", "bucket_batches", "calls", "pipelines", "sets", "max_commands", "instances", "commands", "triangles"];

	/// <summary>Every draw as a CSV row.</summary>
	public string Csv()
	{
		var sb = new StringBuilder( string.Join( ",", Columns ) ).Append( '\n' );
		foreach ( var d in draws )
		{
			string[] row = [d.Pass, d.Path.ToString(), d.Kind, d.Shader, d.Mode, d.Material, d.VertexStatic.ToString( CultureInfo.InvariantCulture ), d.PixelStatic.ToString( CultureInfo.InvariantCulture ),
				d.VertexDynamic.ToString( CultureInfo.InvariantCulture ), d.PixelDynamic.ToString( CultureInfo.InvariantCulture ), d.Features, d.State, d.Code, d.Layout, d.Bucket.ToString( CultureInfo.InvariantCulture ), d.BucketKey,
				d.BucketMaterials.ToString( CultureInfo.InvariantCulture ), d.BucketBatches.ToString( CultureInfo.InvariantCulture ), d.Calls.ToString( CultureInfo.InvariantCulture ), d.Pipelines.ToString( CultureInfo.InvariantCulture ),
				d.Sets.ToString( CultureInfo.InvariantCulture ), d.MaxCommands.ToString( CultureInfo.InvariantCulture ), d.Instances.ToString( CultureInfo.InvariantCulture ), d.Commands.ToString( CultureInfo.InvariantCulture ), d.Triangles.ToString( CultureInfo.InvariantCulture )];
			sb.AppendJoin( ',', row.Select( Quote ) ).Append( '\n' );
		}

		return sb.ToString();
	}

	/// <summary>Each pass's totals as a CSV: what its lists recorded, and of it the model draws by path.</summary>
	public string PassCsv()
	{
		var sb = new StringBuilder( "pass,draw_calls,pipelines,sets,rendering_scopes\n" );
		foreach ( var (pass, r) in passes.OrderBy( p => p.Key, StringComparer.Ordinal ) )
			sb.Append( Quote( pass ) ).Append( ',' ).Append( r.Draws ).Append( ',' ).Append( r.Pipelines ).Append( ',' ).Append( r.Sets ).Append( ',' ).Append( r.Scopes ).Append( '\n' );
		return sb.ToString();
	}

	static string Quote( string value ) => value is null ? "" : value.IndexOfAny( [',', '"', '\n'] ) >= 0 ? $"\"{value.Replace( "\"", "\"\"" )}\"" : value;

	/// <summary>
	/// A short report: per pass, its draw calls, model draws by path, buckets, commands, triangles and binds; the classic
	/// draws by what they are and why; the buckets by shader, with how many would be left if combos weren't pipelines.
	/// </summary>
	public string Report( string title )
	{
		var sb = new StringBuilder();
		sb.AppendLine( $"### {title}" ).AppendLine();
		sb.AppendLine( "| Pass | Draw calls | Classic | Bucket multi-draws | Ids multi-draws | Other | Buckets | Commands drawn (max) | Triangles drawn | Pipelines | Sets |" );
		sb.AppendLine( "|---|---|---|---|---|---|---|---|---|---|---|" );
		var byPass = draws.GroupBy( d => d.Pass ).ToDictionary( g => g.Key, g => g.ToList() );
		var total = new long[9];
		foreach ( var pass in passes.Keys.Union( byPass.Keys ).OrderBy( p => p, StringComparer.Ordinal ) )
		{
			var r = passes.GetValueOrDefault( pass );
			var list = byPass.GetValueOrDefault( pass ) ?? [];
			var classic = list.Where( d => d.Path == Path.Classic ).Sum( d => d.Calls );
			var bucket = list.Where( d => d.Path == Path.Bucket ).Sum( d => d.Calls );
			var ids = list.Where( d => d.Path == Path.BucketIds ).Sum( d => d.Calls );
			var bucketCount = list.Where( d => d.Bucket >= 0 ).Select( d => d.Bucket ).Distinct().Count();
			var commands = list.Sum( d => Math.Max( d.Commands, 0 ) );
			var max = list.Sum( d => d.MaxCommands );
			var triangles = list.Sum( d => Math.Max( d.Triangles, 0 ) );
			var other = r.Draws - classic - bucket - ids;
			if ( r.Draws == 0 && list.Count == 0 ) continue;
			sb.AppendLine( $"| {pass} | {r.Draws} | {classic} | {bucket} | {ids} | {other} | {bucketCount} | {commands} ({max}) | {triangles:N0} | {r.Pipelines} | {r.Sets} |" );
			long[] add = [r.Draws, classic, bucket, ids, other, bucketCount, commands, max, triangles];
			for ( int i = 0; i < add.Length; i++ ) total[i] += add[i];
		}

		sb.AppendLine( $"| **All** | {total[0]} | {total[1]} | {total[2]} | {total[3]} | {total[4]} | {total[5]} | {total[6]} ({total[7]}) | {total[8]:N0} | {passes.Values.Sum( p => p.Pipelines )} | {passes.Values.Sum( p => p.Sets )} |" );
		sb.AppendLine();

		sb.AppendLine( "Classic draws by what they are and why they aren't GPU-driven:" ).AppendLine();
		sb.AppendLine( "| What, why | Shader | Draw calls | Triangles | Passes |" );
		sb.AppendLine( "|---|---|---|---|---|" );
		foreach ( var g in draws.Where( d => d.Path == Path.Classic ).GroupBy( d => (d.Kind, d.Shader) ).OrderByDescending( g => g.Sum( d => d.Calls ) ) )
		{
			var passList = string.Join( "; ", g.GroupBy( d => d.Pass ).OrderByDescending( p => p.Count() ).Select( p => $"{p.Key} {p.Count()}" ) );
			sb.AppendLine( $"| {g.Key.Kind} | {g.Key.Shader} | {g.Sum( d => d.Calls )} | {g.Sum( d => Math.Max( d.Triangles, 0 ) ):N0} | {passList} |" );
		}

		sb.AppendLine();
		sb.AppendLine( "GPU-driven buckets by shader (a bucket is one pipeline and set bind): buckets, and how many would be left with each shader's combos as data (same shader, mode, state and bucket key):" ).AppendLine();
		sb.AppendLine( "| Shader | Buckets | Without combos | Multi-draws | Commands drawn | Triangles | Materials |" );
		sb.AppendLine( "|---|---|---|---|---|---|---|" );
		var bucketed = draws.Where( d => d.Bucket >= 0 ).ToList();
		foreach ( var g in bucketed.GroupBy( d => d.Shader ).OrderByDescending( g => g.Select( d => d.Bucket ).Distinct().Count() ) )
		{
			var count = g.Select( d => d.Bucket ).Distinct().Count();
			var merged = g.Select( d => (d.Pass, d.Path, d.Mode, d.State, d.BucketKey) ).Distinct().Count();
			var materials = g.GroupBy( d => d.Bucket ).Sum( b => b.First().BucketMaterials );
			sb.AppendLine( $"| {g.Key} | {count} | {merged} | {g.Sum( d => d.Calls )} | {g.Sum( d => Math.Max( d.Commands, 0 ) )} | {g.Sum( d => Math.Max( d.Triangles, 0 ) ):N0} | {materials} |" );
		}

		if ( leftClassic.Count > 0 )
		{
			sb.AppendLine();
			sb.AppendLine( "Batches the GPU-driven path left classic: " + string.Join( ", ", leftClassic.OrderByDescending( x => x.Value ).Select( x => $"{x.Key} {x.Value}" ) ) );
		}

		return sb.ToString();
	}
}
