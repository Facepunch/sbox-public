using Borealis.Gpu;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using GpuCullMode = Borealis.Gpu.CullMode;
using GpuDevice = Borealis.Gpu.Device;
using GpuVertexAttribute = Borealis.Gpu.VertexAttribute;
using TextureBarrier = Borealis.Gpu.TextureBarrier;
using V4 = System.Numerics.Vector4;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-mdi-bench</c>: what a draw costs the GPU's front end when meshes are drawn as many small indirect draws, as a
/// meshlet cull that writes a command per kept meshlet would draw them. One depth-only pass over 40,000 patches of
/// 98 sub-pixel triangles (64 vertices each, about 45% facing away, as Construct's prepass is), drawn as one draw, as
/// one multi-draw with a command per patch or per run of patches, with and without a count from a buffer, and as half of
/// the patches (what culling half of them would leave). GPU time of each, medians over the frames.
/// <c>-width</c>/<c>-height</c> (1280x800), <c>-frames</c> (60), <c>-patches</c> (40000).
/// </summary>
internal static unsafe class MdiBench
{
	public static bool Requested => Args.Has( "-mdi-bench" );

	const int Side = 8, Quads = Side - 1, TrianglesPerPatch = Quads * Quads * 2, VerticesPerPatch = Side * Side;

	[StructLayout( LayoutKind.Sequential )]
	struct Command
	{
		public uint IndexCount, InstanceCount, FirstIndex;
		public int VertexOffset;
		public uint FirstInstance;
	}

	public static int Run()
	{
		var width = Args.Int( "-width", 1280 );
		var height = Args.Int( "-height", 800 );
		var frames = Args.Int( "-frames", 60 );
		var patches = Args.Int( "-patches", 40000 );
		var errors = 0;
		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = "Borealis Lab mdi",
			Validation = Args.Has( "-validation" ),
			Message = m =>
			{
				if ( m.Severity < MessageSeverity.Warning ) return;
				if ( m.Severity == MessageSeverity.Error ) Interlocked.Increment( ref errors );
				Console.WriteLine( $"[{m.Severity}] {m.Id}: {m.Text}" );
			},
		} );

		// Patches: an 8x8 vertex grid about 3 pixels across at a random place and depth, a random way round
		var random = new Random( 1 );
		var vertices = new V4[patches * VerticesPerPatch];
		var indices = new uint[patches * TrianglesPerPatch * 3];
		var pixel = new V4( 2f / width, 2f / height, 0, 0 );
		for ( int p = 0; p < patches; p++ )
		{
			var cx = (float)random.NextDouble() * 2 - 1;
			var cy = (float)random.NextDouble() * 2 - 1;
			var z = 0.01f + (float)random.NextDouble() * 0.9f;
			var flip = random.NextDouble() < 0.45;
			for ( int y = 0; y < Side; y++ )
				for ( int x = 0; x < Side; x++ )
					vertices[p * VerticesPerPatch + y * Side + x] = new V4( cx + x * 0.43f * pixel.X, cy + y * 0.43f * pixel.Y, z, 1 );

			var i = p * TrianglesPerPatch * 3;
			for ( int y = 0; y < Quads; y++ )
			{
				for ( int x = 0; x < Quads; x++ )
				{
					uint a = (uint)(y * Side + x), b = a + 1, c = a + Side, d = c + 1;
					uint[] quad = flip ? [a, c, b, b, c, d] : [a, b, c, b, d, c];
					foreach ( var v in quad ) indices[i++] = v;
				}
			}
		}

		var vertexBuffer = gpu.CreateBuffer( new BufferInfo { Size = vertices.Length * 16, Usage = BufferUsage.Vertex | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Mdi vertices" } );
		gpu.WriteBuffer<V4>( vertexBuffer, vertices );
		var indexBuffer = gpu.CreateBuffer( new BufferInfo { Size = indices.Length * 4, Usage = BufferUsage.Index | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Mdi indices" } );
		gpu.WriteBuffer<uint>( indexBuffer, indices );

		// Commands: one per run of k patches, and every other patch alone; then the counts
		int[] runs = [1, 2, 4, 8, 32];
		var commands = new List<Command>();
		var firstCommand = new Dictionary<string, int>();
		void Add( string name, IEnumerable<(int First, int Count)> list )
		{
			firstCommand[name] = commands.Count;
			foreach ( var (first, count) in list )
				commands.Add( new Command { IndexCount = (uint)(count * TrianglesPerPatch * 3), InstanceCount = 1, FirstIndex = (uint)(first * TrianglesPerPatch * 3), VertexOffset = first * VerticesPerPatch, FirstInstance = 0 } );
		}

		// Patch indices are local to the patch, so a run of patches can't share a vertex offset: each run's indices are
		// rebased into a second index buffer laid out as the runs are (patch p's vertices at p * 64)
		var global = new uint[indices.Length];
		for ( int k = 0; k < indices.Length; k++ ) global[k] = indices[k] + (uint)(k / (TrianglesPerPatch * 3) * VerticesPerPatch);
		var globalBuffer = gpu.CreateBuffer( new BufferInfo { Size = global.Length * 4, Usage = BufferUsage.Index | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Mdi global indices" } );
		gpu.WriteBuffer<uint>( globalBuffer, global );

		foreach ( var k in runs ) Add( $"runs{k}", Enumerable.Range( 0, (patches + k - 1) / k ).Select( r => (r * k, Math.Min( k, patches - r * k )) ) );
		Add( "half1", Enumerable.Range( 0, patches / 2 ).Select( r => (r * 2, 1) ) );

		var commandArray = commands.ToArray();
		for ( int c = 0; c < commandArray.Length; c++ ) commandArray[c].VertexOffset = 0;
		var commandBuffer = gpu.CreateBuffer( new BufferInfo { Size = commandArray.Length * sizeof( Command ), Usage = BufferUsage.Indirect | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Mdi commands" } );
		gpu.WriteBuffer<Command>( commandBuffer, commandArray );
		var counts = runs.Select( k => (uint)((patches + k - 1) / k) ).Append( (uint)(patches / 2) ).ToArray();
		var countBuffer = gpu.CreateBuffer( new BufferInfo { Size = counts.Length * 4, Usage = BufferUsage.Indirect | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Mdi counts" } );
		gpu.WriteBuffer<uint>( countBuffer, counts );

		var shader = gpu.CreateShader( MapsSpirv( "mdi.vs.spv" ), "mdi vs" );
		var pipeline = gpu.CreateGraphicsPipeline( new GraphicsPipelineInfo
		{
			VertexShader = shader,
			VertexEntry = "MainVs",
			Sets = [],
			VertexBuffers = [new VertexBufferLayout( 16 )],
			Attributes = [new GpuVertexAttribute( 0, 0, VertexFormat.Float4, 0 )],
			Topology = PrimitiveTopology.TriangleList,
			CullMode = GpuCullMode.Back,
			FrontFace = FrontFace.CounterClockwise,
			DepthTest = true,
			DepthWrite = true,
			DepthCompare = CompareFunction.GreaterEqual,
			Blend = [default],
			ColorFormats = [],
			DepthFormat = Format.D32Float,
			Samples = 1,
			Name = "mdi depth",
		} );

		var depth = gpu.CreateTexture( TextureInfo.RenderTarget( width, height, Format.D32Float, 1, "Mdi depth" ) );

		// Each case: name and how to draw it
		var cases = new List<(string Name, Action<CommandList> Draw)>
		{
			("one draw, all patches", l => { l.SetIndexBuffer( globalBuffer, IndexFormat.UInt32 ); l.DrawIndexed( global.Length ); }),
			("one draw, every other patch's worth (a compacted half)", l => { l.SetIndexBuffer( globalBuffer, IndexFormat.UInt32 ); l.DrawIndexed( global.Length / 2 ); }),
		};

		for ( int r = 0; r < runs.Length; r++ )
		{
			var k = runs[r];
			var first = firstCommand[$"runs{k}"];
			var count = (int)counts[r];
			var countOffset = r * 4;
			cases.Add( ($"multi-draw, {count} commands of {k} patch{(k > 1 ? "es" : "")} ({k * TrianglesPerPatch} triangles)", l => { l.SetIndexBuffer( globalBuffer, IndexFormat.UInt32 ); l.DrawIndexedIndirect( commandBuffer, first * (long)sizeof( Command ), count, sizeof( Command ) ); }) );
			cases.Add( ($"multi-draw with count, {count} commands of {k} patch{(k > 1 ? "es" : "")}", l => { l.SetIndexBuffer( globalBuffer, IndexFormat.UInt32 ); l.DrawIndexedIndirectCount( commandBuffer, first * (long)sizeof( Command ), countBuffer, countOffset, count, sizeof( Command ) ); }) );
		}

		var halfFirst = firstCommand["half1"];
		cases.Add( ($"multi-draw with count, every other patch ({patches / 2} commands)", l => { l.SetIndexBuffer( globalBuffer, IndexFormat.UInt32 ); l.DrawIndexedIndirectCount( commandBuffer, halfFirst * (long)sizeof( Command ), countBuffer, runs.Length * 4, patches / 2, sizeof( Command ) ); }) );

		using var timestamps = gpu.CreateTimestamps( cases.Count * 2 );
		var period = gpu.Capabilities.TimestampPeriod / 1e6;
		var times = cases.Select( _ => new List<double>() ).ToArray();
		var ticks = new ulong[2];
		for ( int f = 0; f < frames + 5; f++ )
		{
			var list = gpu.BeginCommandList( QueueType.Graphics, "Mdi" );
			list.SetPipeline( pipeline );
			for ( int c = 0; c < cases.Count; c++ )
			{
				list.Barrier( TextureBarrier.Transition( depth, ImageLayout.Undefined, ImageLayout.DepthTarget, PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetWrite, PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests, MemoryAccess.DepthTargetRead | MemoryAccess.DepthTargetWrite ) );
				list.WriteTimestamp( timestamps, c * 2 );
				list.BeginRendering( [], DepthTarget.Of( depth, 0.0f ) );
				list.SetPipeline( pipeline );
				list.SetVertexBuffer( 0, vertexBuffer );
				cases[c].Draw( list );
				list.EndRendering();
				list.WriteTimestamp( timestamps, c * 2 + 1 );
			}

			gpu.Submit( list );
			gpu.WaitIdle();
			if ( f < 5 ) continue;
			for ( int c = 0; c < cases.Count; c++ )
				if ( timestamps.TryRead( c * 2, ticks ) ) times[c].Add( (ticks[1] - ticks[0]) * period );
		}

		Console.WriteLine( $"{gpu.Adapter}: {patches} patches, {patches * TrianglesPerPatch / 1e6:0.00}M triangles, {width}x{height}, {frames} frames, validation errors {errors}" );
		for ( int c = 0; c < cases.Count; c++ )
		{
			var sorted = times[c].OrderBy( x => x ).ToList();
			var median = sorted.Count > 0 ? sorted[sorted.Count / 2] : double.NaN;
			Console.WriteLine( FormattableString.Invariant( $"  {median,7:0.000} ms [{sorted.FirstOrDefault():0.000}-{sorted.LastOrDefault():0.000}]  {cases[c].Name}" ) );
		}

		gpu.WaitIdle();
		return errors > 0 ? 1 : 0;
	}

	static byte[] MapsSpirv( string name )
	{
		using var stream = typeof( MdiBench ).Assembly.GetManifestResourceStream( name ) ?? throw new InvalidOperationException( $"{name} isn't embedded" );
		var data = new byte[stream.Length];
		stream.ReadExactly( data );
		return data;
	}
}
