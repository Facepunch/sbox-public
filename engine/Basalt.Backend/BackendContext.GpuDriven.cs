using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Basalt.Backend;

/// <summary>
/// GPU-driven drawing (<see cref="BackendDevice.GpuDriven"/>, docs/managed/basalt-gpu-driven.md): a layer's aggregate
/// batches are sorted into buckets by what a multi-draw can't vary - the shader's GPU-driven variant, pipeline state
/// and object attributes - and each bucket is one pipeline and set bind and one indirect multi-draw per index page. What
/// the classic path binds per draw (vertex and index buffers, material constants and textures, the instance's transform)
/// comes from <see cref="GpuDrivenTables"/> by instance index instead.
/// </summary>
internal sealed partial class BackendContext
{
	static readonly StringToken GpuDrivenConstantsName = new( "BasaltGpuDriven_t" );

	/// <summary>The table slots <c>basalt_gpu_driven.fxc</c>'s <c>BasaltGpuDriven_t</c> holds.</summary>
	[StructLayout( LayoutKind.Sequential )]
	struct GpuDrivenConstants
	{
		public uint Instances, Draws, Geometry, Materials;
	}

	/// <summary>A bucket: what one multi-draw shares, and the batches in it.</summary>
	sealed class GpuDrivenBucket
	{
		public ShaderVariant Variant;
		public BackendMaterial Material;
		public ShaderParameters Attributes;
		public bool DepthOnly, NoColor;

		/// <summary>The classic variant of its first material, when it's depth-only: what decides whether positions alone will do.</summary>
		public ShaderVariant Classic;

		/// <summary>With <see cref="BackendDevice.GpuDrivenMaterialBuckets"/>, the one pixel material record its draws share; else 0.</summary>
		public uint PixelMaterial;
		/// <summary>Its batches, with the index page each draws from: sorted by page, a bucket is one multi-draw per page.</summary>
		public readonly List<(int Page, int Batch)> Batches = new();
		public int Commands;

		/// <summary>Every fragment of its batches' aggregates: its commands when they're culled on the GPU.</summary>
		public int Fragments;

		/// <summary>The visibility-driven prepass draws it with ids (<see cref="BackendContext.DrawIds"/>).</summary>
		public bool Ids;
	}

	readonly List<GpuDrivenBucket> gpuDrivenBuckets = new();
	int gpuDrivenBucketCount;

	internal override bool GpuDriven => device.DrawsAggregatesGpuDriven;

	/// <summary>Count a batch left to the classic path, for the report (<see cref="BackendDevice.GpuDrivenLeftClassic"/>).</summary>
	void LeftClassic( ShaderFile shader, object mode, string why )
	{
		if ( !device.TimeDraws ) return;
		device.GpuDrivenLeftClassic.AddOrUpdate( $"{shader.Name} {mode} ({why})", 1, static ( _, n ) => n + 1 );
	}

	internal override void DrawGpuDriven( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> args, TransformBuffer transforms, int depthBias, float slopeScaledDepthBias, Span<bool> drawn, in GpuDrivenCull? cull = null )
	{
		if ( !GpuDriven || recording is null || batches.IsEmpty || !EnsureScope() ) return;

		var start = device.TimeDraws ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		var tables = device.GpuDrivenTables;
		var cache = device.MaterialCache;
		var resolver = Resolver( cache );
		var r = recording;
		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var depth = r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth );
		var samples = (r.Color ?? r.Depth).Samples;

		// Which bucket each batch goes in: its material's GPU-driven variant under its object attributes
		gpuDrivenBucketCount = 0;
		var totalCommands = 0;
		var totalFragments = 0;

		// Culled on the GPU when every batch is a whole aggregate whose fragments' transforms are in order
		var gpuCull = device.GpuDrivenCulling && cull.HasValue && CullShader is not null;
		for ( int i = 0; i < batches.Length; i++ )
		{
			ref readonly var batch = ref batches[i];
			if ( batch.Material is not BackendMaterial { Shader: { } shader } m || batch.Mesh?.Geometry is not IBackendGeometry source ) continue;
			if ( source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled is null )
			{
				LeftClassic( shader, shaderMode, "geometry not pooled" );
				continue;
			}

			SetObjectAttributes( null, batch.ObjectAttributes );
			NextPassState( cache );
			var mode = CachedModeName( shader, shaderMode );
			if ( mode is null ) continue;

			comboLookup ??= name => LookupCombo( Token( name ) );
			specializationLookup ??= name => Integer( Lookup( Token( name ) ) );
			var variant = m.GpuDrivenVariant( device.Shaders, mode, ComboKey( m, mode ), comboLookup, specializationLookup );
			if ( variant?.Vertex?.Bytecode is null )
			{
				LeftClassic( shader, mode, "no variant" );
				continue;
			}

			var noColor = batch.StripPixelShader || r.Color is null;
			var depthOnly = StripsPixelShader( m, noColor, batch.StripPixelShader );
			if ( !depthOnly && variant.Pixel?.Bytecode is null )
			{
				LeftClassic( shader, mode, "no pixel variant" );
				continue;
			}

			// One material per bucket where pixels read it, when asked: its record's offset is then the bucket's, uniform
			var pixelMaterial = 0u;
			if ( device.GpuDrivenMaterialBuckets && !depthOnly )
			{
				var uncompressed = source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).UncompressedTangentFrame;
				pixelMaterial = MaterialRecord( tables, m, variant.Pixel, uncompressed );
			}

			var bucket = FindBucket( variant, batch.ObjectAttributes, depthOnly, noColor, pixelMaterial, m, mode );
			bucket.Batches.Add( (source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled.Value.IndexPageSlot, i) );
			bucket.Commands += batch.Count;
			totalCommands += batch.Count;

			var fragmentCount = batch.Owner is AggregateObject aggregate ? aggregate.Fragments.Length : 0;
			gpuCull &= fragmentCount > 0 && transforms.InstanceSlot( batch.FirstInstance + fragmentCount - 1 ) == transforms.InstanceSlot( batch.FirstInstance ) + fragmentCount - 1;
			bucket.Fragments += fragmentCount;
			totalFragments += fragmentCount;
		}

		SetObjectAttributes( null );
		if ( gpuDrivenBucketCount == 0 ) return;

		if ( gpuCull )
		{
			DrawGpuCulled( batches, transforms, cull.Value, totalFragments, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, drawn );
			TimeGpuDriven( start, drawn, totalFragments );
			return;
		}

		// One region this frame for every instance record (8 bytes) and command (20 bytes)
		var instanceBytes = totalCommands * GpuDrivenTables.InstanceStride;
		var commandBase = (instanceBytes + 15) & ~15;
		var frameBytes = commandBase + totalCommands * GpuDrivenTables.CommandStride;
		var (frameBuffer, frameSlot, frameOffset) = tables.Allocate( frameBytes );
		var data = Gpu.Map( frameBuffer ).Slice( (int)frameOffset, frameBytes );
		var firstInstance = (uint)(frameOffset / GpuDrivenTables.InstanceStride);
		var instances = MemoryMarshal.Cast<byte, uint>( data[..instanceBytes] );
		var commands = MemoryMarshal.Cast<byte, GpuBuffer.IndirectDrawIndexedArguments>( data.Slice( commandBase, totalCommands * GpuDrivenTables.CommandStride ) );

		var slots = tables.Slots;
		SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials } );

		var written = 0;
		for ( int b = 0; b < gpuDrivenBucketCount; b++ )
		{
			var bucket = gpuDrivenBuckets[b];

			// A bucket's one material, where it has one, is an object attribute: another bucket's moves a dynamic offset
			SetObjectAttributes( bucket.PixelMaterial != 0 ? device.GpuDrivenMaterialParameters( bucket.PixelMaterial ) : null, bucket.Attributes );
			NextPassState( cache );
			resolver.Begin( passEpoch );

			// The records, and each batch's commands, by index page
			var bucketStart = written;
			var currentPage = default( BufferHandle );
			var currentFormat = IndexFormat.UInt16;
			var runStart = written;
			var bound = false;

			bucket.Batches.Sort( static ( a, b ) => a.Page != b.Page ? a.Page.CompareTo( b.Page ) : a.Batch.CompareTo( b.Batch ) );
			foreach ( var (_, index) in bucket.Batches )
			{
				ref readonly var batch = ref batches[index];
				var material = (BackendMaterial)batch.Material;
				var geometry = ((IBackendGeometry)batch.Mesh.Geometry).For( batch.Draw.Mesh, batch.Draw.DrawCall );
				var pooled = geometry.Pooled.Value;

				// Depth only, from the positions-only copy when the vertex shader reads nothing else these vertices have
				if ( bucket.Classic is not null && device.PositionStreams && pooled.PositionsRecord != 0 && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };

				var vertexMaterial = MaterialRecord( tables, material, bucket.Variant.Vertex, geometry.UncompressedTangentFrame );
				var pixelMaterial = bucket.DepthOnly ? 0 : MaterialRecord( tables, material, bucket.Variant.Pixel, geometry.UncompressedTangentFrame );
				var flags = geometry.UncompressedTangentFrame ? 1u : 0u;

				if ( pooled.IndexPage != currentPage )
				{
					if ( written > runStart ) DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
					currentPage = pooled.IndexPage;
					currentFormat = pooled.IndexFormat;
					runStart = written;
				}

				// An owner's fragments keep their draw records by instance; anything else looks them up by key
				var owned = batch.Owner is null ? null : tables.DrawsOf( batch.Owner, pooled.GeometryRecord, vertexMaterial, pixelMaterial );
				if ( owned is not null ) System.Threading.Monitor.Enter( owned );

				var fragments = args.Slice( batch.FirstArg, batch.Count );
				for ( int f = 0; f < fragments.Length; f++ )
				{
					var arg = fragments[f];
					var firstIndex = pooled.FirstIndex + arg.FirstIndex;
					uint draw;
					if ( owned is null )
					{
						draw = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot );
					}
					else
					{
						var slot = (int)arg.FirstInstance;
						if ( slot >= owned.Draws.Length ) Array.Resize( ref owned.Draws, Math.Max( slot + 1, owned.Draws.Length * 2 ) );

						ref var entry = ref owned.Draws[slot];
						if ( entry.IndexCount != arg.IndexCount || entry.FirstIndex != firstIndex || entry.BaseVertex != arg.BaseVertex )
							entry = new GpuDrivenTables.OwnerDraw( firstIndex, arg.IndexCount, arg.BaseVertex, tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot ) );
						draw = entry.Draw;
					}

					instances[written * 2] = (uint)transforms.InstanceSlot( batch.FirstInstance + (int)arg.FirstInstance );
					instances[written * 2 + 1] = draw;
					commands[written] = new GpuBuffer.IndirectDrawIndexedArguments
					{
						IndexCount = arg.IndexCount,
						InstanceCount = 1,
						FirstIndex = firstIndex,
						BaseVertex = arg.BaseVertex,
						FirstInstance = firstInstance + (uint)written,
					};
					written++;
				}

				if ( owned is not null ) System.Threading.Monitor.Exit( owned );
				drawn[index] = true;
			}

			if ( written > runStart ) DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
			bucket.Batches.Clear();
		}

		SetObjectAttributes( null );
		r.Binder = null;
		TimeGpuDriven( start, drawn, written );
	}

	/// <summary>
	/// The timings of a <see cref="DrawGpuDriven"/>: a bucket is a bind and its first multi-draw; the rest of its multi-draws
	/// (one per index page) draws; its commands fragment draws. The time is the whole call's, put on the first bind.
	/// </summary>
	void TimeGpuDriven( long start, Span<bool> drawn, int commands )
	{
		if ( device.TimeDraws )
		{
			var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
			for ( int b = 0; b < gpuDrivenBucketCount; b++ )
			{
				device.DrawTimes.Add( b == 0 ? ticks : 0, bind: true, drawn: true );
				device.DrawTimes.AddIndirect( gpuDrivenBuckets[b].Commands );
			}

			for ( int i = gpuDrivenBucketCount; i < gpuDrivenRuns; i++ ) device.DrawTimes.Add( 0, bind: false, drawn: true );

			var drawnBatches = 0;
			foreach ( var d in drawn ) drawnBatches += d ? 1 : 0;
			device.DrawTimes.AddGpuDriven( drawnBatches, gpuDrivenBucketCount, gpuDrivenRuns, commands );
		}

		gpuDrivenRuns = 0;
	}

	ShaderFile CullShader => device.GpuDrivenCullShader;

	ShaderDrawer cullDrawer;
	ShaderAttributes cullAttributes;
	byte[] cullConstants;
	readonly List<(int Bucket, BufferHandle Page, IndexFormat Format, int First, int Count)> cullRuns = new();

	const int CullViewBytes = 128;
	const int CullBatchBytes = 32;

	/// <summary>
	/// Draw the buckets with every fragment of each batch's aggregate culled on the GPU (<c>basalt_gpu_driven_cull_cs</c>):
	/// the CPU writes the view and a 32-byte record per batch, and a dispatch writes every fragment's instance record and
	/// command, with no instance where the frustum or size test fails. The fragments' bounds and draw records are written
	/// once per aggregate (<see cref="GpuDrivenTables.FragmentsOf"/>). With <see cref="BackendDevice.GpuDrivenVisibility"/>,
	/// a view's depth prepass also draws ids and marks the triangles that own a sample, and its forward pass draws only
	/// those (<see cref="DrawIds"/>).
	/// </summary>
	void DrawGpuCulled( ReadOnlySpan<GpuDrivenBatch> batches, TransformBuffer transforms, in GpuDrivenCull cull, int totalFragments, Format color, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver, Span<bool> drawn )
	{
		var tables = device.GpuDrivenTables;
		var cache = device.MaterialCache;
		var r = recording;

		var batchCount = 0;
		for ( int b = 0; b < gpuDrivenBucketCount; b++ ) batchCount += gpuDrivenBuckets[b].Batches.Count;

		// The visibility-driven forward pass: the view's ids and lists, compacting only
		var compact = device.GpuDrivenCompaction && device.SupportsDrawIndirectCount;
		var shaders = compact && device.GpuDrivenVisibility && cull.View is not null && (cull.Ids || cull.Visibility) ? device.GpuDrivenVisibilityShaders : default;
		var visibility = shaders.Ids is not null && shaders.Mark is not null && r.Depth is not null ? device.VisibilityFor( cull.View, Gpu.InfoOf( r.Depth.Handle ) ) : null;
		var ids = visibility is not null && cull.Ids && r.Color is null;
		var lists = visibility is not null && cull.Visibility && visibility.StampsCleared;

		// One region: the view, the batches, the multi-draws (first slot and visible count, when compacting; a run of
		// compacted triangles per bucket after its own), the compacted indices' cursor, then a slot per fragment (twice,
		// with lists) for its instance record (8 bytes), its fragment (4, for ids) and command (20)
		var slotCount = totalFragments * (lists ? 2 : 1);
		var runsOffset = CullViewBytes + batchCount * CullBatchBytes;
		var cursorOffset = runsOffset + (compact ? (batchCount + gpuDrivenBucketCount) * 8 : 0);
		var instanceOffset = cursorOffset + 16;
		var fragmentOffset = instanceOffset + slotCount * GpuDrivenTables.InstanceStride;
		var commandOffset = (fragmentOffset + (ids ? slotCount * 4 : 0) + 15) & ~15;
		var frameBytes = commandOffset + slotCount * GpuDrivenTables.CommandStride;
		var (frameBuffer, frameSlot, frameOffset) = tables.Allocate( frameBytes );
		var data = Gpu.Map( frameBuffer ).Slice( (int)frameOffset, frameBytes );
		var firstInstance = (uint)((frameOffset + instanceOffset) / GpuDrivenTables.InstanceStride);
		var compacted = lists ? visibility.Compacted[device.GpuDrivenFrame % visibility.Compacted.Length] : default;

		var view = MemoryMarshal.Cast<byte, float>( data[..CullViewBytes] );
		Span<Vector4> planes = [cull.Left, cull.Right, cull.Bottom, cull.Top, cull.Near, cull.Far, cull.CameraAndThreshold];
		for ( int p = 0; p < planes.Length; p++ )
		{
			view[p * 4] = planes[p].x;
			view[p * 4 + 1] = planes[p].y;
			view[p * 4 + 2] = planes[p].z;
			view[p * 4 + 3] = planes[p].w;
		}

		MemoryMarshal.Cast<byte, uint>( data[..CullViewBytes] )[28] = cull.Shadow ? 1u : 0u;
		MemoryMarshal.Cast<byte, uint>( data.Slice( cursorOffset, 16 ) ).Clear();
		var batchRecords = MemoryMarshal.Cast<byte, uint>( data.Slice( CullViewBytes, batchCount * CullBatchBytes ) );

		// Each batch's record and its fragments' slots, bucket by bucket and page by page, and the multi-draws they make
		cullRuns.Clear();
		var written = 0;
		var compactWritten = totalFragments;
		var record = 0;
		for ( int b = 0; b < gpuDrivenBucketCount; b++ )
		{
			var bucket = gpuDrivenBuckets[b];
			SetObjectAttributes( null, bucket.Attributes );
			NextPassState( cache );

			// The prepass draws a bucket's ids when every batch's depth is what the ids shader draws: depth_only's
			bucket.Ids = ids && bucket.Classic is not null && bucket.Variant.State.Cull != CullMode.None;
			var bucketLists = lists && !bucket.DepthOnly;

			var currentPage = default( BufferHandle );
			var currentFormat = IndexFormat.UInt16;
			var runStart = written;

			bucket.Batches.Sort( static ( a, b ) => a.Page != b.Page ? a.Page.CompareTo( b.Page ) : a.Batch.CompareTo( b.Batch ) );
			foreach ( var (_, index) in bucket.Batches )
			{
				ref readonly var batch = ref batches[index];
				var geometry = ((IBackendGeometry)batch.Mesh.Geometry).For( batch.Draw.Mesh, batch.Draw.DrawCall );
				if ( bucket.Ids && !ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) bucket.Ids = false;
			}

			foreach ( var (_, index) in bucket.Batches )
			{
				ref readonly var batch = ref batches[index];
				var aggregate = (AggregateObject)batch.Owner;
				var material = (BackendMaterial)batch.Material;
				var geometry = ((IBackendGeometry)batch.Mesh.Geometry).For( batch.Draw.Mesh, batch.Draw.DrawCall );
				var pooled = geometry.Pooled.Value;

				// Depth only, from the positions-only copy when the vertex shader reads nothing else these vertices have
				if ( bucket.Classic is not null && (device.PositionStreams || bucket.Ids) && pooled.PositionsRecord != 0 && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };

				var vertexMaterial = MaterialRecord( tables, material, bucket.Variant.Vertex, geometry.UncompressedTangentFrame );
				var pixelMaterial = bucket.DepthOnly ? 0 : MaterialRecord( tables, material, bucket.Variant.Pixel, geometry.UncompressedTangentFrame );
				var flags = (geometry.UncompressedTangentFrame ? 1u : 0u) | (pooled.IndexFormat == IndexFormat.UInt32 ? 2u : 0u);

				if ( pooled.IndexPage != currentPage )
				{
					if ( written > runStart ) cullRuns.Add( (b, currentPage, currentFormat, runStart, written - runStart) );
					currentPage = pooled.IndexPage;
					currentFormat = pooled.IndexFormat;
					runStart = written;
				}

				var fragments = aggregate.Fragments;
				var first = tables.FragmentsOf( aggregate, pooled.GeometryRecord, vertexMaterial, pixelMaterial, fragments.Length,
					(Tables: tables, Aggregate: aggregate, Pooled: pooled, Vertex: vertexMaterial, Pixel: pixelMaterial, Flags: flags),
					static ( s, bytes ) => WriteFragments( s.Tables, s.Aggregate, s.Pooled, s.Vertex, s.Pixel, s.Flags, bytes ) );

				var at = record * (CullBatchBytes / 4);
				batchRecords[at] = first;
				batchRecords[at + 1] = (uint)fragments.Length;
				batchRecords[at + 2] = (uint)transforms.InstanceSlot( batch.FirstInstance );
				batchRecords[at + 3] = compact ? (uint)cullRuns.Count : (uint)written;
				batchRecords[at + 4] = visibility is not null ? tables.VisibilityFirst( aggregate, fragments.Length, aggregate, static ( a, i ) => a.Fragments[i].Args.IndexCount / 3 ) : GpuDrivenTables.NoVisibility;
				batchRecords[at + 5] = 0;
				batchRecords[at + 6] = (bucket.Ids ? 1u : 0u) | (bucketLists ? 2u : 0u);
				batchRecords[at + 7] = 0;
				record++;
				written += fragments.Length;
				drawn[index] = true;
			}

			if ( written > runStart ) cullRuns.Add( (b, currentPage, currentFormat, runStart, written - runStart) );

			// Forward's compacted triangles: one more multi-draw for the bucket, from the compacted index buffer
			if ( bucketLists )
			{
				var compactRun = cullRuns.Count;
				cullRuns.Add( (b, compacted, IndexFormat.UInt32, compactWritten, bucket.Fragments) );
				compactWritten += bucket.Fragments;
				for ( int i = record - bucket.Batches.Count; i < record; i++ ) batchRecords[i * (CullBatchBytes / 4) + 5] = (uint)compactRun;
			}
		}

		SetObjectAttributes( null );

		// Each multi-draw's first slot, and its count from 0, which the culling counts up
		var runs = MemoryMarshal.Cast<byte, uint>( data.Slice( runsOffset, compact ? cullRuns.Count * 8 : 0 ) );
		for ( int i = 0; i < runs.Length / 2; i++ )
		{
			runs[i * 2] = (uint)cullRuns[i].First;
			runs[i * 2 + 1] = 0;
		}

		// Outside the rendering scope: the prepass clears the view's triangle mask (and, the first time, its stamps)
		var slots = tables.Slots;
		CloseScope( flushClears: false );
		if ( ids )
		{
			r.List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
			r.List.FillBuffer( visibility.Mask, 0, 0, Math.Max( (tables.Visibility.Bits / 8 + 3) & ~3L, 4 ) );
			if ( !visibility.StampsCleared ) r.List.FillBuffer( visibility.Stamps, 0 );
			visibility.StampsCleared = true;
			r.List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		}

		// Cull and write the commands, then draw them
		cullDrawer ??= new ShaderDrawer( device );
		cullAttributes ??= new ShaderAttributes();
		cullConstants ??= new byte[64];
		var constants = MemoryMarshal.Cast<byte, uint>( cullConstants.AsSpan() );
		constants.Clear();
		constants[0] = (uint)frameOffset;
		constants[1] = (uint)batchCount;
		constants[2] = (uint)(frameOffset + instanceOffset);
		constants[3] = (uint)(frameOffset + commandOffset);
		constants[4] = firstInstance;
		constants[5] = (uint)tables.FragmentSlot;
		constants[6] = (uint)slots.Draws;
		constants[7] = compact ? (uint)(frameOffset + runsOffset) : 0;
		constants[8] = device.GpuDrivenFrame;
		constants[9] = (uint)tables.Visibility.Slot;
		constants[10] = (uint)(frameOffset + cursorOffset);
		constants[11] = BackendDevice.VisibilityCompactedIndices;
		constants[12] = (uint)(frameOffset + fragmentOffset) - firstInstance * 4;
		cullAttributes.Clear();
		cullAttributes.Set( "BasaltCullFrame", frameBuffer );
		if ( visibility is not null )
		{
			cullAttributes.Set( "BasaltCullStamps", visibility.Stamps );
			cullAttributes.Set( "BasaltCullMask", visibility.Mask );
			cullAttributes.Set( "BasaltCullCompacted", lists ? compacted : visibility.Compacted[0] );
		}

		cullAttributes.SetData( "BasaltGpuDrivenCull_t", cullConstants );

		// Another pass of the view this frame may still be drawing from the compacted indices this one writes over
		if ( lists ) r.List.Barrier( PipelineStage.VertexInput, MemoryAccess.IndexRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite );
		cullDrawer.Dispatch( r.List, CullShader, cullAttributes, batchCount * 64, 1, 1 );

		// Only the culling's writes before the draws' reads of them: the draws before it needn't drain
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.VertexInput | PipelineStage.VertexShader, MemoryAccess.IndirectRead | MemoryAccess.IndexRead | MemoryAccess.ShaderRead );
		r.Pipeline = default;
		r.Binder = null;

		SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials } );

		// The prepass's id buckets first, into the ids and depth, then the triangles they show are marked
		if ( ids ) DrawIds( visibility, shaders, frameBuffer, frameOffset, commandOffset, runsOffset, (uint)(frameOffset + fragmentOffset) - firstInstance * 4, depth, samples, resolver );

		if ( !EnsureScope() ) return;

		var bound = false;
		var lastBucket = -1;
		for ( int i = 0; i < cullRuns.Count; i++ )
		{
			var run = cullRuns[i];
			var bucket = gpuDrivenBuckets[run.Bucket];
			if ( bucket.Ids ) continue;
			if ( run.Bucket != lastBucket )
			{
				lastBucket = run.Bucket;
				SetObjectAttributes( bucket.PixelMaterial != 0 ? device.GpuDrivenMaterialParameters( bucket.PixelMaterial ) : null, bucket.Attributes );
				NextPassState( cache );
				resolver.Begin( passEpoch );
				bound = false;
			}

			DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, run.Page, run.Format, frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, run.Count, compact ? frameOffset + runsOffset + i * 8 + 4 : -1 );
		}

		SetObjectAttributes( null );
		r.Binder = null;
	}

	static readonly StringToken IdsConstantsName = new( "BasaltGpuDrivenIds_t" );
	byte[] markConstants;

	/// <summary>
	/// The prepass's id buckets: depth and ids (<c>basalt_gpu_driven_ids</c>: (fragment + 1) &lt;&lt; 16 | triangle, at the
	/// depth target's samples) in a rendering scope of their own, then <c>basalt_gpu_driven_mark_cs</c> sets the bit of every
	/// triangle that owns a sample in the view's mask, for its forward pass.
	/// </summary>
	void DrawIds( BackendDevice.VisibilityView visibility, (BackendMaterial Ids, ShaderFile Mark) shaders, BufferHandle frameBuffer, long frameOffset, int commandOffset, int runsOffset, uint fragmentWords, Format depth, int samples, PassResolver resolver )
	{
		var r = recording;
		var cache = device.MaterialCache;
		var depthTexture = r.Depth;

		r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		r.List.BeginRendering( [new ColorTarget { Texture = visibility.Ids, Load = LoadOp.Clear, Store = StoreOp.Store, ClearColor = default }], new DepthTarget { Texture = depthTexture.Handle, Layer = bound.DepthSlice, Load = LoadOp.Load, Store = StoreOp.Store } );
		var rect = bound.Viewport;
		var (width, height) = r.List.RenderSize;
		if ( rect.Width <= 0 || rect.Height <= 0 ) rect = new Rect( 0, 0, width, height );
		r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, bound.MaxZ );
		r.List.SetScissor( (int)rect.Left, (int)rect.Top, (int)MathF.Ceiling( rect.Width ), (int)MathF.Ceiling( rect.Height ) );
		r.Pipeline = default;

		SetConstants( IdsConstantsName, new System.Numerics.Vector4( BitConverter.UInt32BitsToSingle( fragmentWords ), 0, 0, 0 ) );
		var variant = Variant( shaders.Ids, "Default" );
		for ( int i = 0; i < cullRuns.Count; i++ )
		{
			var run = cullRuns[i];
			var bucket = gpuDrivenBuckets[run.Bucket];
			if ( !bucket.Ids || variant is null ) continue;

			SetObjectAttributes( null, bucket.Attributes );
			NextPassState( cache );
			resolver.Begin( passEpoch );
			var key = new MaterialObjectCache.Key( shaders.Ids, variant, device.PulledLayout, Format.R32Uint, depth, samples, false, true );
			var materialObject = cache.Get( key, immediate: false, (device, key), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.device.PulledLayout, s.key.Color, s.key.Depth, s.key.Samples, false, l.Sets ) );
			resolver.LayerEpoch = layerEpoch;
			materialObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
			r.List.SetIndexBuffer( run.Page, run.Format, 0 );
			r.List.DrawIndexedIndirectCount( frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, frameBuffer, frameOffset + runsOffset + i * 8 + 4, run.Count, GpuDrivenTables.CommandStride );
			gpuDrivenRuns++;
		}

		r.List.EndRendering();
		r.Pipeline = default;
		r.Binder = null;
		SetObjectAttributes( null );

		// Mark: every distinct id of each pixel's samples sets its triangle's bit
		r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.ColorTarget, ImageLayout.ShaderRead, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead ) );
		var (_, _, fragments) = device.GpuDrivenTables.Visibility;
		var markConstants = this.markConstants ??= new byte[32];
		var words = MemoryMarshal.Cast<byte, uint>( markConstants.AsSpan() );
		words[0] = (uint)visibility.Width;
		words[1] = (uint)visibility.Height;
		words[2] = (uint)visibility.Samples;
		words[3] = (uint)device.GpuDrivenTables.Visibility.Slot;
		words[4] = (uint)fragments;
		cullAttributes.Clear();
		cullAttributes.Set( "BasaltIds", visibility.Ids );
		cullAttributes.Set( "BasaltCullMask", visibility.Mask );
		cullAttributes.SetCombo( "D_MSAA", visibility.Samples > 1 ? 1 : 0 );
		cullAttributes.SetData( "BasaltGpuDrivenMark_t", markConstants );
		cullDrawer.Dispatch( r.List, shaders.Mark, cullAttributes, visibility.Width, visibility.Height, 1 );

		// The mask and stamps for the forward pass's culling, in a later list
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
	}
	/// <summary>
	/// An aggregate's fragment records: centre and draw record, extents and fragment index (bit 31 set when it casts no
	/// shadow), making each fragment's draw record.
	/// </summary>
	static void WriteFragments( GpuDrivenTables tables, AggregateObject aggregate, PooledGeometry pooled, uint vertexMaterial, uint pixelMaterial, uint flags, Span<byte> bytes )
	{
		var records = MemoryMarshal.Cast<byte, uint>( bytes );
		var fragments = aggregate.Fragments;
		var centers = aggregate.Centers;
		var extents = aggregate.Extents;
		for ( int i = 0; i < fragments.Length; i++ )
		{
			var arg = fragments[i].Args;
			var firstIndex = pooled.FirstIndex + arg.FirstIndex;
			var at = i * (GpuDrivenTables.FragmentStride / 4);
			records[at] = BitConverter.SingleToUInt32Bits( centers[i].x );
			records[at + 1] = BitConverter.SingleToUInt32Bits( centers[i].y );
			records[at + 2] = BitConverter.SingleToUInt32Bits( centers[i].z );
			records[at + 3] = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot );
			records[at + 4] = BitConverter.SingleToUInt32Bits( extents[i].x );
			records[at + 5] = BitConverter.SingleToUInt32Bits( extents[i].y );
			records[at + 6] = BitConverter.SingleToUInt32Bits( extents[i].z );
			records[at + 7] = arg.FirstInstance | ((fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ? 0x80000000u : 0u);
		}
	}

	/// <summary>Multi-draws recorded by the current <see cref="DrawGpuDriven"/>, for the timings.</summary>
	int gpuDrivenRuns;

	GpuDrivenBucket FindBucket( ShaderVariant variant, ShaderParameters attributes, bool depthOnly, bool noColor, uint pixelMaterial, BackendMaterial material, string mode )
	{
		for ( int i = 0; i < gpuDrivenBucketCount; i++ )
		{
			var bucket = gpuDrivenBuckets[i];
			if ( ReferenceEquals( bucket.Variant, variant ) && ReferenceEquals( bucket.Attributes, attributes ) && bucket.DepthOnly == depthOnly && bucket.NoColor == noColor && bucket.PixelMaterial == pixelMaterial ) return bucket;
		}

		if ( gpuDrivenBucketCount == gpuDrivenBuckets.Count ) gpuDrivenBuckets.Add( new GpuDrivenBucket() );
		var made = gpuDrivenBuckets[gpuDrivenBucketCount++];
		made.Variant = variant;
		made.Attributes = attributes;
		made.DepthOnly = depthOnly;
		made.Classic = depthOnly ? Variant( material, mode ) : null;
		made.NoColor = noColor;
		made.PixelMaterial = pixelMaterial;
		made.Material = material;
		made.Batches.Clear();
		made.Commands = 0;
		made.Ids = false;
		made.Fragments = 0;
		return made;
	}

	/// <summary>
	/// One multi-draw: the bucket's pipeline and sets (bound once, through any of its materials' material object: a
	/// GPU-driven variant binds no material resources), the page's indices, and its run of commands: all of them, or as
	/// many as the count at <paramref name="countOffset"/> in the same buffer when it's 0 or more.
	/// </summary>
	void DrawRun( ref bool bound, GpuDrivenBucket bucket, Format color, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver,
		BufferHandle page, IndexFormat format, BufferHandle commands, long offset, int count, long countOffset = -1 )
	{
		var r = recording;
		if ( !bound )
		{
			var cache = device.MaterialCache;
			var perDraw = bucket.PixelMaterial != 0;
			var layout = perDraw ? device.PulledLayoutMaterialPerDraw : device.PulledLayout;
			var key = new MaterialObjectCache.Key( bucket.Material, bucket.Variant, layout, bucket.NoColor ? Format.Unknown : color, depth, samples, bucket.DepthOnly, true );
			var materialObject = cache.Get( key, immediate: false, (device, layout, key, perDraw), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.layout, s.key.Color, s.key.Depth, s.key.Samples, s.key.DepthOnly, l.Sets, s.perDraw ) );
			resolver.LayerEpoch = layerEpoch;
			materialObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
			if ( bucket.NoColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
			if ( bucket.Variant.State.StencilTest && r.Depth is not null && BackendDevice.FormatOf( r.Depth ).HasStencil() ) r.List.SetStencilReference( (byte)bucket.Variant.State.StencilReference );
			bound = true;
		}

		r.List.SetIndexBuffer( page, format, 0 );
		if ( countOffset >= 0 ) r.List.DrawIndexedIndirectCount( commands, offset, commands, countOffset, count, GpuDrivenTables.CommandStride );
		else r.List.DrawIndexedIndirect( commands, offset, count, GpuDrivenTables.CommandStride );
		gpuDrivenRuns++;
	}

	/// <summary>
	/// A stage's material record: its constants as the classic path fills its constant buffer (the material's values,
	/// this context's attributes, the draw's tangent format, the shader's defaults), then each material texture's bindless
	/// slot. Made once per material, stage and attribute values.
	/// </summary>
	uint MaterialRecord( GpuDrivenTables tables, BackendMaterial material, ShaderStage stage, bool uncompressedTangentFrame )
	{
		if ( stage is null ) return 0;

		var key = new GpuDrivenTables.MaterialRecordKey( material, material.Version, stage, uncompressedTangentFrame, AttributeHash( stage ) );
		var size = Math.Max( stage.MaterialBuffer.Size, 4 );
		foreach ( var (_, offset) in stage.RecordTextures ) size = Math.Max( size, offset + 4 );

		return tables.Material( key, size, (Context: this, Material: material, Stage: stage, Uncompressed: uncompressedTangentFrame), static ( s, record ) => s.Context.WriteMaterialRecord( s.Material, s.Stage, s.Uncompressed, record ) );
	}

	readonly Dictionary<ShaderStage, (long PassEpoch, long LayerEpoch, int Hash)> attributeHashes = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// What a stage's attribute-sourced constants read now, as part of a material record's key: the same for every
	/// material, so it's worked out once per stage, pass state and object attributes.
	/// </summary>
	int AttributeHash( ShaderStage stage )
	{
		if ( attributeHashes.TryGetValue( stage, out var known ) && known.PassEpoch == passEpoch && known.LayerEpoch == layerEpoch ) return known.Hash;

		var hash = new HashCode();
		foreach ( var (variable, _) in stage.MaterialConstants )
		{
			if ( variable.Source != VariableSource.Attribute || variable.Attribute == "UncompressedTangentFrame" ) continue;
			hash.Add( Lookup( Token( variable.Attribute ) ) );
		}

		var made = hash.ToHashCode();
		attributeHashes[stage] = (passEpoch, layerEpoch, made);
		return made;
	}

	void WriteMaterialRecord( BackendMaterial material, ShaderStage stage, bool uncompressedTangentFrame, Span<byte> record )
	{
		var constants = Math.Min( stage.MaterialBuffer.Size, record.Length );
		if ( constants > 0 )
		{
			stage.WriteMaterialConstants( record[..constants], variable => variable.Source switch
			{
				VariableSource.Material or VariableSource.MaterialAndExpression => material.Value( variable.Name ),
				VariableSource.ShaderVersion => [material.ShaderVersion],
				VariableSource.Attribute when variable.Attribute == "UncompressedTangentFrame" => [uncompressedTangentFrame ? 1 : 0],
				VariableSource.Attribute => Floats( Lookup( Token( variable.Attribute ) ) ),
				_ => null,
			}, material.Features );
		}

		foreach ( var (variable, offset) in stage.RecordTextures )
		{
			material.Textures.TryGetValue( variable.Name, out var texture );
			var srgb = variable.RenderStateId == 1;
			int slot;
			if ( texture is not null && Gpu.BindlessIndex( texture.Handle ) is >= 0 and var index )
			{
				// SrgbRead( true ) textures read the sRGB view, one after the linear one, where the format has one
				slot = index + (srgb && Gpu.InfoOf( texture.Handle ).Format.HasSrgb() ? 1 : 0);
			}
			else
			{
				slot = device.MissingTextureViewIndex( srgb );
			}

			BinaryPrimitives.WriteInt32LittleEndian( record[offset..], slot );
		}
	}
}
