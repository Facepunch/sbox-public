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
		public uint Instances, Draws, Geometry, Materials, PixelMaterial;
	}

	/// <summary>A bucket: what one multi-draw shares, and the batches in it.</summary>
	sealed class GpuDrivenBucket
	{
		public ShaderVariant Variant;
		public BackendMaterial Material;
		public ShaderParameters Attributes;
		public bool DepthOnly, NoColor;

		/// <summary>With <see cref="BackendDevice.GpuDrivenMaterialBuckets"/>, the one pixel material record its draws share; else 0.</summary>
		public uint PixelMaterial;
		/// <summary>Its batches, with the index page each draws from: sorted by page, a bucket is one multi-draw per page.</summary>
		public readonly List<(int Page, int Batch)> Batches = new();
		public int Commands;
	}

	readonly List<GpuDrivenBucket> gpuDrivenBuckets = new();
	int gpuDrivenBucketCount;

	internal override bool GpuDriven => device.GpuDriven && device.MaterialObjects && device.GpuDrivenTables is not null;

	internal override void DrawGpuDriven( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> args, TransformBuffer transforms, int depthBias, float slopeScaledDepthBias, Span<bool> drawn )
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
		for ( int i = 0; i < batches.Length; i++ )
		{
			ref readonly var batch = ref batches[i];
			if ( batch.Material is not BackendMaterial { Shader: { } shader } m || batch.Mesh?.Geometry is not IBackendGeometry source ) continue;
			if ( source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled is null ) continue;

			SetObjectAttributes( null, batch.ObjectAttributes );
			NextPassState( cache );
			var mode = CachedModeName( shader, shaderMode );
			if ( mode is null ) continue;

			comboLookup ??= name => LookupCombo( Token( name ) );
			specializationLookup ??= name => Integer( Lookup( Token( name ) ) );
			var variant = m.GpuDrivenVariant( device.Shaders, mode, ComboKey( m, mode ), comboLookup, specializationLookup );
			if ( variant?.Vertex?.Bytecode is null ) continue;

			var noColor = batch.StripPixelShader || r.Color is null;
			var depthOnly = StripsPixelShader( m, noColor, batch.StripPixelShader );
			if ( !depthOnly && variant.Pixel?.Bytecode is null ) continue;

			// One material per bucket where pixels read it, when asked: its record's offset is then the bucket's, uniform
			var pixelMaterial = 0u;
			if ( device.GpuDrivenMaterialBuckets && !depthOnly )
			{
				var uncompressed = source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).UncompressedTangentFrame;
				pixelMaterial = MaterialRecord( tables, m, variant.Pixel, uncompressed );
			}

			var bucket = FindBucket( variant, batch.ObjectAttributes, depthOnly, noColor, pixelMaterial, m );
			bucket.Batches.Add( (source.For( batch.Draw.Mesh, batch.Draw.DrawCall ).Pooled.Value.IndexPageSlot, i) );
			bucket.Commands += batch.Count;
			totalCommands += batch.Count;
		}

		SetObjectAttributes( null );
		if ( gpuDrivenBucketCount == 0 ) return;

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
		var constants = new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, PixelMaterial = uint.MaxValue };

		var written = 0;
		for ( int b = 0; b < gpuDrivenBucketCount; b++ )
		{
			var bucket = gpuDrivenBuckets[b];
			if ( b == 0 || bucket.PixelMaterial != constants.PixelMaterial )
			{
				constants.PixelMaterial = bucket.PixelMaterial;
				SetConstants( GpuDrivenConstantsName, constants );
			}

			SetObjectAttributes( null, bucket.Attributes );
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

				var fragments = args.Slice( batch.FirstArg, batch.Count );
				for ( int f = 0; f < fragments.Length; f++ )
				{
					var arg = fragments[f];
					var firstIndex = pooled.FirstIndex + arg.FirstIndex;
					var draw = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot );

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

				drawn[index] = true;
			}

			if ( written > runStart ) DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
			bucket.Batches.Clear();
		}

		SetObjectAttributes( null );
		r.Binder = null;

		if ( device.TimeDraws )
		{
			// A bucket is a bind and its first multi-draw; the rest of its multi-draws (one per index page) draws; its
			// commands fragment draws. The time is the whole call's, put on the first bind
			var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
			for ( int b = 0; b < gpuDrivenBucketCount; b++ )
			{
				device.DrawTimes.Add( b == 0 ? ticks : 0, bind: true, drawn: true );
				device.DrawTimes.AddIndirect( gpuDrivenBuckets[b].Commands );
			}

			for ( int i = gpuDrivenBucketCount; i < gpuDrivenRuns; i++ ) device.DrawTimes.Add( 0, bind: false, drawn: true );

			var drawnBatches = 0;
			foreach ( var d in drawn ) drawnBatches += d ? 1 : 0;
			device.DrawTimes.AddGpuDriven( drawnBatches, gpuDrivenBucketCount, gpuDrivenRuns, written );
		}

		gpuDrivenRuns = 0;
	}

	/// <summary>Multi-draws recorded by the current <see cref="DrawGpuDriven"/>, for the timings.</summary>
	int gpuDrivenRuns;

	GpuDrivenBucket FindBucket( ShaderVariant variant, ShaderParameters attributes, bool depthOnly, bool noColor, uint pixelMaterial, BackendMaterial material )
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
		made.NoColor = noColor;
		made.PixelMaterial = pixelMaterial;
		made.Material = material;
		made.Batches.Clear();
		made.Commands = 0;
		return made;
	}

	/// <summary>
	/// One multi-draw: the bucket's pipeline and sets (bound once, through any of its materials' material object: a
	/// GPU-driven variant binds no material resources), the page's indices, and its run of commands.
	/// </summary>
	void DrawRun( ref bool bound, GpuDrivenBucket bucket, Format color, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver,
		BufferHandle page, IndexFormat format, BufferHandle commands, long offset, int count )
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
		r.List.DrawIndexedIndirect( commands, offset, count, GpuDrivenTables.CommandStride );
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

		// What the attribute-sourced constants read now, as part of the key
		var hash = new HashCode();
		foreach ( var (variable, _) in stage.MaterialConstants )
		{
			if ( variable.Source != VariableSource.Attribute || variable.Attribute == "UncompressedTangentFrame" ) continue;
			if ( Floats( Lookup( Token( variable.Attribute ) ) ) is { } values )
			{
				foreach ( var v in values ) hash.Add( v );
			}
			else hash.Add( -1 );
		}

		var key = new GpuDrivenTables.MaterialRecordKey( material, material.Version, stage, uncompressedTangentFrame, hash.ToHashCode() );
		var size = Math.Max( stage.MaterialBuffer.Size, 4 );
		foreach ( var (_, offset) in stage.RecordTextures ) size = Math.Max( size, offset + 4 );

		return tables.Material( key, size, (Context: this, Material: material, Stage: stage, Uncompressed: uncompressedTangentFrame), static ( s, record ) => s.Context.WriteMaterialRecord( s.Material, s.Stage, s.Uncompressed, record ) );
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
