using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
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

		/// <summary>Where each instance's first vertex bit is, a word per instance (wraps); the instance records where there are no ids.</summary>
		public uint VertexBases;
		/// <summary>The lighting table's slot: a draw's lighting record is its flags' top 24 bits, in 16 bytes.</summary>
		public uint Lighting;
		public uint Pad1, Pad2;
	}

	/// <summary>A bucket: what one multi-draw shares, and the batches in it.</summary>
	sealed class GpuDrivenBucket
	{
		public ShaderVariant Variant;
		public BackendMaterial Material;

		/// <summary>A surface's GPU-driven pass and program, for a bucket of surface materials (<see cref="Variant"/> and <see cref="Material"/> are null then).</summary>
		public SurfaceProgram.PassObjects Surface;
		public SurfaceProgram SurfaceProgram;

		/// <summary>A surface bucket whose vertices need only their positions (it doesn't displace them): the ids prepass may pull those alone.</summary>
		public bool SurfacePositionsOnly;
		public ShaderParameters Attributes;
		public bool DepthOnly, NoColor;

		/// <summary>Its attributes but for their per-draw lighting (<see cref="BackendParameters.LightingFreeKey"/>): what it's found by.</summary>
		public long AttributesKey;

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

		/// <summary>With <see cref="Ids"/>: its variant has no pixel shader, so <c>basalt_gpu_driven_ids</c> draws it.</summary>
		public bool IdsShader;
	}

	readonly List<GpuDrivenBucket> gpuDrivenBuckets = new();

	/// <summary>Each batch's lighting record (<see cref="GpuDrivenTables.Lighting"/>), by its index in the call.</summary>
	uint[] batchLighting = [];
	static readonly StringToken BakedLightingToken = new( "BakedLightingConstantBuffer_t" );
	static readonly StringToken ProbeVolumeToken = new( "PerBatchLightProbeVolumeConstantBuffer_t" );
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
		ShaderParameters lastAttributes = null;
		var lastAttributesSet = false;
		var lastLighting = 0u;
		var lastKey = 0L;
		for ( int i = 0; i < batches.Length; i++ )
		{
			ref readonly var batch = ref batches[i];

			// A surface material: its own GPU-driven passes, in buckets of its own (BackendContext.Surfaces.cs)
			if ( batch.Material is SurfaceMaterial surface )
			{
				if ( batch.Mesh?.Geometry is IBackendGeometry surfaceSource && surfaceSource.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled is not null )
				{
					SetObjectAttributes( null, batch.ObjectAttributes );
					NextPassState( cache );
					if ( batchLighting.Length < batches.Length ) Array.Resize( ref batchLighting, Math.Max( batches.Length, batchLighting.Length * 2 ) );
					if ( !lastAttributesSet || !ReferenceEquals( batch.ObjectAttributes, lastAttributes ) )
					{
						(lastAttributes, lastAttributesSet) = (batch.ObjectAttributes, true);
						lastLighting = tables.Lighting( Lookup( BakedLightingToken ) as byte[], Lookup( ProbeVolumeToken ) as byte[] );
						lastKey = AttributesKey( batch.ObjectAttributes );
					}

					batchLighting[i] = lastLighting;
					if ( SurfaceBucket( surface, shaderMode, batch.StripPixelShader || r.Color is null, batch.ObjectAttributes, lastKey ) is { } surfaceBucket )
					{
						surfaceBucket.Batches.Add( (surfaceSource.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled.Value.IndexPageSlot, i) );
						device.Surfaces.CountGpuDriven();
						surfaceBucket.Commands += batch.Count;
						totalCommands += batch.Count;
						var surfaceFragments = batch.Instances ? batch.Count : batch.Owner is AggregateObject surfaceAggregate ? surfaceAggregate.Fragments.Length : 0;
						gpuCull &= surfaceFragments > 0 && (batch.Instances || transforms.InstanceSlot( batch.FirstInstance + surfaceFragments - 1 ) == transforms.InstanceSlot( batch.FirstInstance ) + surfaceFragments - 1);
						surfaceBucket.Fragments += surfaceFragments;
						totalFragments += surfaceFragments;
					}
				}

				continue;
			}

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

			// Its lighting constants as the draw would read them (the object's, else the pass's), as a lighting record
			if ( batchLighting.Length < batches.Length ) Array.Resize( ref batchLighting, Math.Max( batches.Length, batchLighting.Length * 2 ) );
			// Consecutive batches mostly share their attributes (the world's lightmap, a probe volume): looked up once for them
			if ( !lastAttributesSet || !ReferenceEquals( batch.ObjectAttributes, lastAttributes ) )
			{
				(lastAttributes, lastAttributesSet) = (batch.ObjectAttributes, true);
				lastLighting = tables.Lighting( Lookup( BakedLightingToken ) as byte[], Lookup( ProbeVolumeToken ) as byte[] );
				lastKey = AttributesKey( batch.ObjectAttributes );
			}

			batchLighting[i] = lastLighting;

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

			var bucket = FindBucket( variant, batch.ObjectAttributes, lastKey, depthOnly, noColor, pixelMaterial, m, mode );
			bucket.Batches.Add( (source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled.Value.IndexPageSlot, i) );
			bucket.Commands += batch.Count;
			totalCommands += batch.Count;

			// A mesh draw's instances are its fragments, each with its own transform slot; an aggregate's need theirs in order
			var fragmentCount = batch.Instances ? batch.Count : batch.Owner is AggregateObject aggregate ? aggregate.Fragments.Length : 0;
			gpuCull &= fragmentCount > 0 && (batch.Instances || transforms.InstanceSlot( batch.FirstInstance + fragmentCount - 1 ) == transforms.InstanceSlot( batch.FirstInstance ) + fragmentCount - 1);
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
		SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, VertexBases = (uint)frameOffset - firstInstance * 4, Lighting = (uint)slots.Lighting } );

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
				var material = batch.Material;
				var geometry = ((IBackendGeometry)batch.Mesh.Geometry).For( batch.Draw.Mesh, batch.Draw.DrawCall );
				var pooled = geometry.Pooled.Value;

				// Depth only, from the positions-only copy when the vertex shader reads nothing else these vertices have
				if ( bucket.Classic is not null && device.PositionStreams && pooled.PositionsRecord != 0 && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };

				var (vertexMaterial, pixelMaterial) = Records( tables, bucket, material, geometry.UncompressedTangentFrame );
				var flags = (geometry.UncompressedTangentFrame ? 1u : 0u) | batchLighting[index] << 8;

				if ( pooled.IndexPage != currentPage )
				{
					if ( written > runStart ) DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
					currentPage = pooled.IndexPage;
					currentFormat = pooled.IndexFormat;
					runStart = written;
				}

				// An owner's fragments keep their draw records by instance; anything else looks them up by key. The owner's
				// lock is held only around its records and the draw table, which takes no other lock, and is let go whatever
				// throws, so a worker drawing the same owner in another segment can't wait on it forever
				var owned = batch.Owner is null ? null : tables.DrawsOf( batch.Owner, pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags );
				var ownedTaken = false;
				try
				{
					if ( owned is not null ) System.Threading.Monitor.Enter( owned, ref ownedTaken );

					for ( int f = 0; f < batch.Count; f++ )
					{
						var arg = batch.Instances ? InstanceArguments( batch, geometry, f ) : args[batch.FirstArg + f];
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
				}
				finally
				{
					if ( ownedTaken ) System.Threading.Monitor.Exit( owned );
				}

				drawn[index] = true;
			}

			if ( written > runStart ) DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
			bucket.Batches.Clear();
		}

		SetObjectAttributes( null );
		r.Binder = null;
		TimeGpuDriven( start, drawn, written );
	}

	/// <summary>Instance <paramref name="instance"/> of a mesh draw's batch, as the arguments an aggregate's fragment has.</summary>
	static GpuBuffer.IndirectDrawIndexedArguments InstanceArguments( in GpuDrivenBatch batch, in DrawGeometry geometry, int instance ) => new()
	{
		IndexCount = (uint)batch.Draw.IndexCount,
		InstanceCount = 1,
		FirstIndex = (uint)batch.Draw.StartIndex,
		BaseVertex = geometry.BaseVertex,
		FirstInstance = (uint)instance,
	};

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
		var instanceFragments = 0;
		for ( int b = 0; b < gpuDrivenBucketCount; b++ )
		{
			batchCount += gpuDrivenBuckets[b].Batches.Count;
			foreach ( var (_, index) in gpuDrivenBuckets[b].Batches )
			{
				if ( batches[index].Instances ) instanceFragments += batches[index].Count;
			}
		}

		// The visibility-driven forward pass: the view's ids and lists, compacting only
		var compact = device.GpuDrivenCompaction && device.SupportsDrawIndirectCount;
		var shaders = compact && device.GpuDrivenVisibility && cull.View is not null && (cull.Ids || cull.Visibility) ? device.GpuDrivenVisibilityShaders : default;
		var visibility = shaders.Mark is not null && shaders.Compact is not null && r.Depth is not null ? device.VisibilityFor( cull.View, Gpu.InfoOf( r.Depth.Handle ) ) : null;
		// Only once its state was cleared this frame, before any call that writes into it; the first frame draws plainly
		var ready = visibility is not null && visibility.ClearedFrame == device.GpuDrivenFrame;
		var ids = ready && cull.Ids && r.Color is null;
		var lists = ready && cull.Visibility;

		// One region: the view, the batches, the multi-draws (first slot and visible count, when compacting; a run of
		// compacted triangles per bucket after its own), the listed fragments, mesh draws'
		// instances' transform slots, then a slot per fragment (twice, with lists) for its instance record (8 bytes), its
		// fragment (4, for ids) and command (20)
		var slotCount = totalFragments * (lists ? 2 : 1);
		var runsOffset = CullViewBytes + batchCount * CullBatchBytes;
		var listedOffset = (runsOffset + (compact ? (batchCount + gpuDrivenBucketCount) * 8 : 0) + 15) & ~15;
		var transformSlotsOffset = listedOffset + (lists ? 16 + totalFragments * 32 : 0);
		var instanceOffset = transformSlotsOffset + ((instanceFragments * 4 + 15) & ~15);
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
		if ( lists )
		{
			// The compaction's dispatch: a group per listed fragment, counted up by the culling
			var arguments = MemoryMarshal.Cast<byte, uint>( data.Slice( listedOffset, 16 ) );
			arguments[0] = 0;
			arguments[1] = 1;
			arguments[2] = 1;
		}
		var batchRecords = MemoryMarshal.Cast<byte, uint>( data.Slice( CullViewBytes, batchCount * CullBatchBytes ) );
		var transformSlots = MemoryMarshal.Cast<byte, uint>( data.Slice( transformSlotsOffset, instanceFragments * 4 ) );
		var slotsWritten = 0;

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

			// The prepass draws ids with each bucket's own variant, whose pixel shader writes them where it covers; a depth
			// bucket without one (opaque depth_only) with basalt_gpu_driven_ids, when its vertices need only their positions
			var ownPixel = bucket.Surface is not null ? bucket.Surface.Pass.Pixel is not null : bucket.Variant.Pixel?.Bytecode is not null && bucket.Variant.Pixel.GpuDriven;
			if ( ids && !ownPixel && shaders.Ids is not null ) idsShaderVariant ??= Variant( shaders.Ids, "Default" );
			bucket.IdsShader = ids && !ownPixel && (bucket.Classic is not null || bucket.SurfacePositionsOnly) && shaders.Ids is not null && idsShaderVariant is not null;
			bucket.Ids = ids && (ownPixel || bucket.IdsShader);
			var bucketLists = lists && !bucket.DepthOnly;

			var currentPage = default( BufferHandle );
			var currentFormat = IndexFormat.UInt16;
			var runStart = written;

			bucket.Batches.Sort( static ( a, b ) => a.Page != b.Page ? a.Page.CompareTo( b.Page ) : a.Batch.CompareTo( b.Batch ) );
			foreach ( var (_, index) in bucket.Batches )
			{
				if ( !bucket.IdsShader ) break;
				ref readonly var batch = ref batches[index];
				var geometry = ((IBackendGeometry)batch.Mesh.Geometry).For( batch.Draw.Mesh, batch.Draw.DrawCall );
				if ( bucket.Classic is not null && !ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) bucket.Ids = bucket.IdsShader = false;
			}

			foreach ( var (_, index) in bucket.Batches )
			{
				ref readonly var batch = ref batches[index];
				var material = batch.Material;
				var geometry = ((IBackendGeometry)batch.Mesh.Geometry).For( batch.Draw.Mesh, batch.Draw.DrawCall );
				var pooled = geometry.Pooled.Value;

				// Depth only, from the positions-only copy when the vertex shader reads nothing else these vertices have
				if ( bucket.Classic is not null && (device.PositionStreams || bucket.IdsShader) && pooled.PositionsRecord != 0 && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };

				var (vertexMaterial, pixelMaterial) = Records( tables, bucket, material, geometry.UncompressedTangentFrame );
				var flags = (geometry.UncompressedTangentFrame ? 1u : 0u) | (pooled.IndexFormat == IndexFormat.UInt32 ? 2u : 0u) | batchLighting[index] << 8;

				if ( pooled.IndexPage != currentPage )
				{
					if ( written > runStart ) cullRuns.Add( (b, currentPage, currentFormat, runStart, written - runStart) );
					currentPage = pooled.IndexPage;
					currentFormat = pooled.IndexFormat;
					runStart = written;
				}

				var at = record * (CullBatchBytes / 4);
				uint visibilityFirst, vertexBase, kind;
				int fragmentCount;
				if ( batch.Instances )
				{
					// A mesh draw's instances: its draw record, and each instance's transform slot in the frame's region; the
					// caller culled them, and each has a run of vertex bits of its own, a stride apart, made for this frame
					fragmentCount = batch.Count;
					batchRecords[at] = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, pooled.FirstIndex + (uint)batch.Draw.StartIndex, (uint)batch.Draw.IndexCount, geometry.BaseVertex, pooled.IndexPageSlot );
					batchRecords[at + 2] = (uint)(frameOffset + transformSlotsOffset + slotsWritten * 4);
					for ( int k = 0; k < fragmentCount; k++ ) transformSlots[slotsWritten++] = (uint)transforms.InstanceSlot( batch.FirstInstance + k );

					var stride = (uint)pooled.VertexCount;
					(visibilityFirst, vertexBase) = visibility is not null && stride < 1u << 24
						? tables.FrameVisibility( (batch.FirstInstance, batch.Mesh, batch.Draw.Mesh, batch.Draw.DrawCall), fragmentCount, pooled.VertexCount )
						: (GpuDrivenTables.NoVisibility, GpuDrivenTables.NoVisibility);
					kind = 4u | stride << 8;
				}
				else
				{
					var aggregate = (AggregateObject)batch.Owner;
					var fragments = aggregate.Fragments;
					fragmentCount = fragments.Length;
					batchRecords[at] = tables.FragmentsOf( aggregate, pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, fragments.Length,
						(Tables: tables, Aggregate: aggregate, Pooled: pooled, Vertex: vertexMaterial, Pixel: pixelMaterial, Flags: flags),
						static ( s, bytes ) => WriteFragments( s.Tables, s.Aggregate, s.Pooled, s.Vertex, s.Pixel, s.Flags, bytes ) );
					batchRecords[at + 2] = (uint)transforms.InstanceSlot( batch.FirstInstance );
					(visibilityFirst, vertexBase) = visibility is not null ? tables.Visibility( aggregate, fragments.Length, pooled.VertexCount ) : (GpuDrivenTables.NoVisibility, GpuDrivenTables.NoVisibility);
					kind = 0;
				}

				batchRecords[at + 1] = (uint)fragmentCount;
				batchRecords[at + 3] = compact ? (uint)cullRuns.Count : (uint)written;
				batchRecords[at + 4] = vertexBase != GpuDrivenTables.NoVisibility ? visibilityFirst : GpuDrivenTables.NoVisibility;
				batchRecords[at + 5] = 0;
				batchRecords[at + 6] = (bucket.Ids ? 1u : 0u) | (bucketLists ? 2u : 0u) | kind;
				batchRecords[at + 7] = vertexBase;
				record++;
				written += fragmentCount;
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

		// The view's mask and stamps were cleared before the frame's segments (BeginGpuDrivenView)
		var slots = tables.Slots;
		CloseScope( flushClears: false );

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
		constants[9] = 0;
		constants[10] = 0;
		constants[11] = BackendDevice.VisibilityCompactedIndices;
		constants[12] = (uint)(frameOffset + fragmentOffset) - firstInstance * 4;
		constants[13] = lists ? (uint)(frameOffset + listedOffset) : 0;
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
		var timer = device.PassTimer;
		var cullScope = timer?.Begin( r.List, ids ? "GPU-driven cull (ids)" : lists ? "GPU-driven cull (lists)" : "GPU-driven cull", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		cullDrawer.Dispatch( r.List, CullShader, cullAttributes, batchCount * 64, 1, 1 );
		timer?.End( r.List, cullScope );

		// Forward's listed fragments: a group each, which counts its triangles that own a sample and draws them or the whole
		if ( lists )
		{
			r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.ComputeShader, MemoryAccess.IndirectRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
			var compactScope = timer?.Begin( r.List, "GPU-driven compact", PassTimer.ScopeKind.Own, count: false ) ?? -1;
			cullDrawer.DispatchIndirect( r.List, shaders.Compact, cullAttributes, frameBuffer, frameOffset + listedOffset );
			timer?.End( r.List, compactScope );
		}

		// Only the culling's writes before the draws' reads of them: the draws before it needn't drain
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.VertexInput | PipelineStage.VertexShader, MemoryAccess.IndirectRead | MemoryAccess.IndexRead | MemoryAccess.ShaderRead );
		r.Pipeline = default;
		r.Binder = null;

		var vertexBases = ids ? (uint)(frameOffset + fragmentOffset) - firstInstance * 4 : (uint)(frameOffset + instanceOffset) - firstInstance * 4;
		SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, VertexBases = vertexBases, Lighting = (uint)slots.Lighting } );

		// The prepass's id buckets first, into the ids and depth, then the triangles they show are marked
		if ( ids ) DrawIds( visibility, shaders, frameBuffer, frameOffset, commandOffset, runsOffset, depth, samples, depthBias, slopeScaledDepthBias, resolver );

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

		// Debug only: the forward pass's multi-draws again, into the figure's targets
		if ( device.Figure is { } figure && r.Color is not null && !cull.Shadow && compact && device.FigureMaterial is { } figureMaterial )
			DrawFigure( figure, figureMaterial, visibility, frameBuffer, frameOffset, commandOffset, runsOffset, lists ? listedOffset : -1, depth, resolver );

		SetObjectAttributes( null );
		r.Binder = null;
	}

	/// <summary>
	/// Before the frame's segments: clear <paramref name="view"/>'s triangle mask (the bits aggregates keep, and those the
	/// last frame's mesh draws took; stale bits beyond only draw a triangle more) and, the first time, its stamps, so every
	/// GPU-driven call of its passes, whichever worker records it, marks into the same cleared mask.
	/// </summary>
	internal override void BeginGpuDrivenView( object view )
	{
		if ( !GpuDriven || !device.GpuDrivenVisibility || recording is null || device.ExistingVisibility( view ) is not { } visibility ) return;
		var frame = device.GpuDrivenFrame;
		if ( visibility.ClearedFrame == frame ) return;
		visibility.ClearedFrame = frame;

		var tables = device.GpuDrivenTables;
		var r = recording;
		CloseScope( flushClears: false );
		r.List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
		r.List.FillBuffer( visibility.Mask, 0, 0, Math.Max( (tables.VisibilityBits / 8 + 3) & ~3L, 4 ) );
		var meshBits = Math.Max( tables.LastFrameVisibilityBits, 1L << 16 );
		r.List.FillBuffer( visibility.Mask, 0, GpuDrivenTables.FrameVisibilityBitBase / 8, Math.Min( (meshBits / 8 + 4) & ~3L, (GpuDrivenTables.MaxVisibilityBits - GpuDrivenTables.FrameVisibilityBitBase) / 8 ) );
		if ( !visibility.StampsCleared ) r.List.FillBuffer( visibility.Stamps, 0 );
		visibility.StampsCleared = true;

		// This frame's compacted buffer's cursor, past itself: the forward pass's calls take their triangles' ranges from it
		r.List.FillBuffer( visibility.Compacted[frame % visibility.Compacted.Length], BackendDevice.VisibilityView.CompactedCursorIndices, 0, 4 );
		r.List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
	}

	static readonly StringToken FigureConstantsName = new( "BasaltGpuDrivenFigure_t" );
	static readonly ConditionalWeakTable<ShaderVariant, object[]> FigureLayouts = new();
	ShaderVariant figureVariant;

	/// <summary>
	/// <c>basaltlab -capture-visibility</c>'s figure (<see cref="BackendDevice.Figure"/>): this forward pass's multi-draws
	/// again with <c>basalt_gpu_driven_figure</c>, a count per fragment into its count target, then its triangles into its
	/// colour target, flat over the view's depth with dark edges or as faint edges with no depth test. Only for a view of the
	/// figure's size and one sample.
	/// </summary>
	void DrawFigure( VisibilityFigure figure, BackendMaterial figureMaterial, BackendDevice.VisibilityView visibility, BufferHandle frameBuffer, long frameOffset, int commandOffset, int runsOffset, int listedOffset, Format depth, PassResolver resolver )
	{
		var r = recording;
		var cache = device.MaterialCache;
		var target = Gpu.InfoOf( figure.Count );
		var depthInfo = Gpu.InfoOf( r.Depth.Handle );
		if ( depthInfo.Width != target.Width || depthInfo.Height != target.Height || depthInfo.Samples != 1 ) return;
		if ( (figureVariant ??= Variant( figureMaterial, "Default" )) is null ) return;

		// Where its commands and counts are, for the figure's counts once the frame is done
		var runs = new List<(int First, long CountOffset, int Max)>();
		for ( int i = 0; i < cullRuns.Count; i++ ) runs.Add( (cullRuns[i].First, frameOffset + runsOffset + i * 8 + 4, cullRuns[i].Count) );
		figure.Passes.Add( (frameBuffer, frameOffset + commandOffset, runs, listedOffset >= 0 ? frameOffset + listedOffset : -1, -1) );
		// The ids of the view with the most fragments (the main view's, not the 3D skybox's)
		var fragments = 0;
		foreach ( var run in cullRuns ) fragments += run.Count;
		if ( visibility is not null && fragments > figure.VisibilityFragments )
		{
			figure.VisibilityFragments = fragments;
			device.FigureVisibility = (visibility.Ids, visibility.Mask, device.GpuDrivenTables.VisibilityBits, device.GpuDrivenTables.FrameVisibilityBits);
		}

		CloseScope( flushClears: false );
		var first = !figure.Cleared;
		figure.Cleared = true;

		Span<int> modes = figure.DepthTestedTriangles ? [0, 1, 2] : [0, 3];
		foreach ( var mode in modes )
		{
			// The count target for the count, the colour target for the rest (cleared before its first mode)
			var texture = mode == 0 ? figure.Count : figure.Colour;
			var clear = first && mode <= 1 || first && mode == 3;
			if ( clear ) r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			else r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( texture, ImageLayout.ColorTarget, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite | MemoryAccess.ColorTargetRead ) );
			r.List.BeginRendering(
				[new ColorTarget { Texture = texture, Load = clear ? LoadOp.Clear : LoadOp.Load, Store = StoreOp.Store, ClearColor = mode == 0 ? default : new System.Numerics.Vector4( 0.02f, 0.025f, 0.035f, 1 ) }],
				new DepthTarget { Texture = r.Depth.Handle, Layer = bound.DepthSlice, Load = LoadOp.Load, Store = StoreOp.Store } );
			var rect = bound.Viewport;
			var (width, height) = r.List.RenderSize;
			if ( rect.Width <= 0 || rect.Height <= 0 ) rect = new Rect( 0, 0, width, height );
			r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, bound.MaxZ );
			r.List.SetScissor( (int)rect.Left, (int)rect.Top, (int)MathF.Ceiling( rect.Width ), (int)MathF.Ceiling( rect.Height ) );
			r.Pipeline = default;

			SetConstants( FigureConstantsName, new System.Numerics.Vector4( BitConverter.UInt32BitsToSingle( (uint)mode ), 0, 0, 0 ) );
			var lastBucket = -1;
			for ( int i = 0; i < cullRuns.Count; i++ )
			{
				var run = cullRuns[i];
				var bucket = gpuDrivenBuckets[run.Bucket];
				if ( bucket.Variant is null ) continue; // surfaces aren't in the figure
				if ( run.Bucket != lastBucket )
				{
					lastBucket = run.Bucket;
					SetObjectAttributes( null, bucket.Attributes );
					NextPassState( cache );
					resolver.Begin( passEpoch );
					var layout = FigureLayouts.GetValue( bucket.Variant, static _ => [new object(), new object(), new object(), new object()] )[mode];
					var key = new MaterialObjectCache.Key( figureMaterial, figureVariant, layout, mode == 0 ? Format.R16Float : Format.RGBA8Unorm, depth, 1, false, true );
					var materialObject = cache.Get( key, immediate: false, (device, key, stateFrom: bucket.Variant, mode), static ( s, l ) => s.device.FigurePipeline( s.key.Variant, s.stateFrom, s.mode, s.key.Depth, l.Sets ) );
					resolver.LayerEpoch = layerEpoch;
					materialObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
				}

				r.List.SetIndexBuffer( run.Page, run.Format, 0 );
				r.List.DrawIndexedIndirectCount( frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, frameBuffer, frameOffset + runsOffset + i * 8 + 4, run.Count, GpuDrivenTables.CommandStride );
			}

			r.List.EndRendering();
			r.Pipeline = default;
			r.Binder = null;
		}

		SetObjectAttributes( null );
	}

	byte[] markConstants;
	ShaderVariant idsShaderVariant;

	/// <summary>The material object key's layout for <c>basalt_gpu_driven_ids</c> drawing a variant's depth: one object per variant.</summary>
	sealed record IdsLayout( VertexLayout Layout, ShaderVariant StateFrom );
	static readonly ConditionalWeakTable<ShaderVariant, IdsLayout> IdsLayouts = new();

	/// <summary>
	/// The prepass's buckets with ids, in a rendering scope of their own: each with its own variant, whose pixel shader
	/// writes the id (the bit of its triangle's first vertex + 1) at target 1 to exactly the samples it covers, its discards
	/// and alpha to coverage included; target 0 takes the authored output, which only alpha to coverage reads. Then
	/// <c>basalt_gpu_driven_mark_cs</c> sets the bit of every id in the view's mask, for its forward pass.
	/// </summary>
	void DrawIds( BackendDevice.VisibilityView visibility, (BackendMaterial Ids, ShaderFile Mark, ShaderFile Compact) shaders, BufferHandle frameBuffer, long frameOffset, int commandOffset, int runsOffset, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver )
	{
		var r = recording;
		var cache = device.MaterialCache;
		var depthTexture = r.Depth;

		var timer = device.PassTimer;
		var idsScope = timer?.Begin( r.List, "GPU-driven ids", PassTimer.ScopeKind.Own ) ?? -1;
		r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( visibility.Scratch, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		r.List.BeginRendering(
			[new ColorTarget { Texture = visibility.Scratch, Load = LoadOp.DontCare, Store = StoreOp.DontCare }, new ColorTarget { Texture = visibility.Ids, Load = LoadOp.Clear, Store = StoreOp.Store, ClearColor = default }],
			new DepthTarget { Texture = depthTexture.Handle, Layer = bound.DepthSlice, Load = LoadOp.Load, Store = StoreOp.Store } );
		var rect = bound.Viewport;
		var (width, height) = r.List.RenderSize;
		if ( rect.Width <= 0 || rect.Height <= 0 ) rect = new Rect( 0, 0, width, height );
		r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, bound.MaxZ );
		r.List.SetScissor( (int)rect.Left, (int)rect.Top, (int)MathF.Ceiling( rect.Width ), (int)MathF.Ceiling( rect.Height ) );
		r.Pipeline = default;

		var lastBucket = -1;
		for ( int i = 0; i < cullRuns.Count; i++ )
		{
			var run = cullRuns[i];
			var bucket = gpuDrivenBuckets[run.Bucket];
			if ( !bucket.Ids ) continue;

			if ( run.Bucket != lastBucket )
			{
				lastBucket = run.Bucket;
				SetObjectAttributes( bucket.PixelMaterial != 0 ? device.GpuDrivenMaterialParameters( bucket.PixelMaterial ) : null, bucket.Attributes );
				NextPassState( cache );
				resolver.Begin( passEpoch );
				MaterialObject materialObject = null;
				if ( bucket.Surface is not null && !bucket.IdsShader )
				{
					// A surface's own GPU-driven pass, writing ids beside its coverage
					BindSurfaceGpuDriven( bucket, Format.Unknown, depth, samples, ids: true );
				}
				else if ( bucket.IdsShader )
				{
					// basalt_gpu_driven_ids with the bucket's culling, depth and stencil: keyed by a layout object per bucket variant
					var stateFrom = bucket.Variant ?? idsShaderVariant;
					var idsLayout = IdsLayouts.GetValue( stateFrom, v => new IdsLayout( device.PulledLayout, v ) );
					var key = new MaterialObjectCache.Key( shaders.Ids, idsShaderVariant, idsLayout, BackendDevice.IdsColorKey, depth, samples, false, true );
					materialObject = cache.Get( key, immediate: false, (device, key, stateFrom), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.device.PulledLayout, BackendDevice.IdsColorKey, s.key.Depth, s.key.Samples, false, l.Sets, ids: true, stateFrom: s.stateFrom ) );
				}
				else
				{
					var perDraw = bucket.PixelMaterial != 0;
					var layout = perDraw ? device.PulledLayoutMaterialPerDraw : device.PulledLayout;
					var key = new MaterialObjectCache.Key( bucket.Material, bucket.Variant, layout, BackendDevice.IdsColorKey, depth, samples, false, true );
					materialObject = cache.Get( key, immediate: false, (device, layout, key, perDraw), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.layout, BackendDevice.IdsColorKey, s.key.Depth, s.key.Samples, false, l.Sets, s.perDraw, ids: true ) );
				}

				resolver.LayerEpoch = layerEpoch;
				materialObject?.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
				if ( bucket.NoColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
				if ( bucket.Variant is { State.StencilTest: true } && depthTexture is not null && BackendDevice.FormatOf( depthTexture ).HasStencil() ) r.List.SetStencilReference( (byte)bucket.Variant.State.StencilReference );
			}

			r.List.SetIndexBuffer( run.Page, run.Format, 0 );
			r.List.DrawIndexedIndirectCount( frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, frameBuffer, frameOffset + runsOffset + i * 8 + 4, run.Count, GpuDrivenTables.CommandStride );
			gpuDrivenRuns++;
		}

		r.List.EndRendering();
		timer?.End( r.List, idsScope );
		r.Pipeline = default;
		r.Binder = null;
		SetObjectAttributes( null );
		// Mark: every distinct id of each pixel's samples sets its triangle's bit
		r.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.ColorTarget, ImageLayout.ShaderRead, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead ) );
		var markConstants = this.markConstants ??= new byte[32];
		var words = MemoryMarshal.Cast<byte, uint>( markConstants.AsSpan() );
		words[0] = (uint)visibility.Width;
		words[1] = (uint)visibility.Height;
		words[2] = (uint)visibility.Samples;
		var frameBits = device.GpuDrivenTables.FrameVisibilityBits;
		words[3] = (uint)Math.Min( frameBits > 0 ? GpuDrivenTables.FrameVisibilityBitBase + frameBits : device.GpuDrivenTables.VisibilityBits, uint.MaxValue );
		cullAttributes.Clear();
		cullAttributes.Set( "BasaltIds", visibility.Ids );
		cullAttributes.Set( "BasaltCullMask", visibility.Mask );
		cullAttributes.SetCombo( "D_MSAA", visibility.Samples > 1 ? 1 : 0 );
		cullAttributes.SetData( "BasaltGpuDrivenMark_t", markConstants );
		var markScope = timer?.Begin( r.List, "GPU-driven mark", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		cullDrawer.Dispatch( r.List, shaders.Mark, cullAttributes, visibility.Width, visibility.Height, 1 );
		timer?.End( r.List, markScope );

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

	/// <summary>A bucket's key for attributes: the same for attributes that differ only in what each draw's lighting record holds.</summary>
	static long AttributesKey( ShaderParameters attributes ) => attributes switch { null => 0L, BackendParameters parameters => parameters.LightingFreeKey, _ => (uint)RuntimeHelpers.GetHashCode( attributes ) | 1L << 62 };

	GpuDrivenBucket FindBucket( ShaderVariant variant, ShaderParameters attributes, long key, bool depthOnly, bool noColor, uint pixelMaterial, BackendMaterial material, string mode )
	{
		// Attributes that differ only in their lighting share a bucket: the variants read it from each draw's lighting record
		for ( int i = 0; i < gpuDrivenBucketCount; i++ )
		{
			var bucket = gpuDrivenBuckets[i];
			if ( ReferenceEquals( bucket.Variant, variant ) && bucket.AttributesKey == key && bucket.DepthOnly == depthOnly && bucket.NoColor == noColor && bucket.PixelMaterial == pixelMaterial ) return bucket;
		}

		if ( gpuDrivenBucketCount == gpuDrivenBuckets.Count ) gpuDrivenBuckets.Add( new GpuDrivenBucket() );
		var made = gpuDrivenBuckets[gpuDrivenBucketCount++];
		made.Variant = variant;
		made.Surface = null;
		made.SurfaceProgram = null;
		made.SurfacePositionsOnly = false;
		made.Attributes = attributes;
		made.AttributesKey = key;
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
		if ( !bound && bucket.Surface is not null )
		{
			BindSurfaceGpuDriven( bucket, bucket.NoColor ? Format.Unknown : color, depth, samples, ids: false );
			if ( bucket.NoColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
			bound = true;
		}

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
			if ( bucket.Variant is { State.StencilTest: true } && r.Depth is not null && BackendDevice.FormatOf( r.Depth ).HasStencil() ) r.List.SetStencilReference( (byte)bucket.Variant.State.StencilReference );
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
