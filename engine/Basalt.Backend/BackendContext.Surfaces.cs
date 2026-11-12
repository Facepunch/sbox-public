using Basalt.Gpu;
using Basalt.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.Buffers.Binary;

namespace Basalt.Backend;

/// <summary>
/// Drawing new-tier materials (<see cref="SurfaceMaterial"/>): the pass's pipeline, sets 0 and 1 bound by name from this
/// context's attributes as classic draws bind them, the material's record at an offset pushed per draw. Forward and
/// alpha-tested forward in colour passes; depth (vertex shader only) or alpha-tested depth where there's no colour.
/// See docs/managed/shader-format-replacement.md.
/// </summary>
internal sealed partial class BackendContext
{
	const string ForwardAlphaTestPass = ShaderPasses.Forward + "AlphaTest";

	static readonly StringToken LightmapCombo = new( "D_BAKED_LIGHTING_FROM_LIGHTMAP" );

	/// <summary><c>kMaterialOffsetPerPixel</c> in sbox/material.slang.</summary>
	const uint MaterialOffsetPerPixelId = 2;

	bool DrawSurface( SurfaceMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, bool skinned, in IndirectDraws indirect )
	{
		if ( mesh.Geometry is not IBackendGeometry source || skinned ) return false;
		if ( (indirect.InstanceIds ?? InstanceIds?.Resource as BackendBuffer) is not { } instanceIds ) return false;
		if ( device.Surfaces.For( material ) is not { } program ) return false;
		if ( !EnsureScope() ) return false;

		// The targets are this scope's only once it's open
		var r = recording;
		var noColor = stripPixelShader || r.Color is null;
		string name;
		if ( noColor )
		{
			name = material.AlphaTest ? ShaderPasses.DepthAlphaTest : ShaderPasses.Depth;
		}
		else if ( shaderMode.Value == ForwardMode.Value )
		{
			name = material.AlphaTest && program.Pass( ForwardAlphaTestPass ) is not null ? ForwardAlphaTestPass : ShaderPasses.Forward;
		}
		else
		{
			return false;
		}

		// A lightmapped object's draws (today's D_BAKED_LIGHTING_FROM_LIGHTMAP combo) pass the mesh's lightmap uv on
		var pass = (LookupCombo( LightmapCombo ) is > 0 ? program.Pass( name + ShaderPasses.Lightmapped ) : null) ?? program.Pass( name );

		if ( pass is null ) return false;

		var color = noColor ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var depth = r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth );
		var geometry = source.For( draw.Mesh, draw.DrawCall );
		var vertexConstants = SurfaceConstants( pass.Pass.Vertex, out var vertexKey );
		ulong pixelKey = 0;
		var pixelConstants = pass.Pass.Pixel is { } pixel ? SurfaceConstants( pixel, out pixelKey ) : null;
		var pipeline = program.Pipeline( pass, geometry.Layout, color, depth, (r.Color ?? r.Depth).Samples, vertexConstants, pixelConstants, vertexKey * 31 + pixelKey );
		if ( r.Pipeline != pipeline )
		{
			r.List.SetPipeline( pipeline );
			r.Pipeline = pipeline;
		}

		uncompressedTangentFrame = geometry.UncompressedTangentFrame;
		var record = device.Surfaces.Records.Offset( material );
		BindSurface( pass, pass.Pass.Vertex, ShaderAbi.VertexSet );
		if ( pass.Pass.Pixel is not null ) BindSurface( pass, pass.Pass.Pixel, ShaderAbi.FragmentSet );
		r.List.PushConstants( record );
		r.Binder = null;

		if ( noColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
		RecordDraw( r.List, geometry, instanceIds, draw, firstInstance, instances, indirect );
		return true;
	}

	/// <summary>
	/// The GPU-driven bucket a surface batch draws in (BackendContext.GpuDriven.cs), made when none fits: its GPU-driven
	/// pass for the mode, alpha test and lightmap combo, under the batch's object attributes. Null, so it draws the classic
	/// way, when the surface has no GPU-driven passes or the mode isn't one a surface draws.
	/// </summary>
	GpuDrivenBucket SurfaceBucket( SurfaceMaterial material, StringToken shaderMode, bool noColor, ShaderParameters attributes, long key )
	{
		if ( !noColor && shaderMode.Value != ForwardMode.Value ) return null;
		if ( device.Surfaces.For( material ) is not { } program ) return null;
		if ( program.GpuDrivenPass( noColor, material.AlphaTest, LookupCombo( LightmapCombo ) is > 0 ) is not { } pass ) return null;

		var depthOnly = pass.Pass.Pixel is null;
		for ( int i = 0; i < gpuDrivenBucketCount; i++ )
		{
			var bucket = gpuDrivenBuckets[i];
			if ( ReferenceEquals( bucket.Surface, pass ) && bucket.AttributesKey == key && bucket.NoColor == noColor ) return bucket;
		}

		if ( gpuDrivenBucketCount == gpuDrivenBuckets.Count ) gpuDrivenBuckets.Add( new GpuDrivenBucket() );
		var made = gpuDrivenBuckets[gpuDrivenBucketCount++];
		made.Variant = null;
		made.Material = null;
		made.Classic = null;
		made.Surface = pass;
		made.SurfaceProgram = program;
		made.SurfacePositionsOnly = !program.Displaces;
		made.Attributes = attributes;
		made.AttributesKey = key;
		made.DepthOnly = depthOnly;
		made.NoColor = noColor;
		made.PixelMaterial = 0;
		made.Batches.Clear();
		made.Commands = 0;
		made.Ids = false;
		made.Fragments = 0;
		return made;
	}

	/// <summary>
	/// A draw's vertex and pixel material records: a surface's one record (its <c>[Material]</c> struct) for both stages,
	/// or a variant's per stage.
	/// </summary>
	(uint Vertex, uint Pixel) Records( GpuDrivenTables tables, GpuDrivenBucket bucket, GpuMaterial material, bool uncompressedTangentFrame )
	{
		if ( bucket.Surface is not null )
		{
			var surface = (SurfaceMaterial)material;
			var key = new GpuDrivenTables.MaterialRecordKey( surface, surface.Version, null, false, 0 );
			var size = (Math.Max( surface.RecordSize, 4 ) + 15) & ~15;
			var record = tables.Material( key, size, surface, static ( s, bytes ) =>
			{
				bytes.Clear();
				s.WriteRecord( bytes, static ( texture, srgb ) => texture.ViewIndex( srgb, TextureViewDimension.Default ) );
			} );
			return (record, record);
		}

		var classic = (BackendMaterial)material;
		return (MaterialRecord( tables, classic, bucket.Variant.Vertex, uncompressedTangentFrame ), bucket.DepthOnly ? 0 : MaterialRecord( tables, classic, bucket.Variant.Pixel, uncompressedTangentFrame ));
	}

	/// <summary>
	/// A surface bucket's pipeline (vertices pulled, so no vertex layout) and sets 0 and 1, bound by name from this
	/// context's attributes as for its classic draws; with <paramref name="ids"/>, for the visibility prepass's targets.
	/// </summary>
	void BindSurfaceGpuDriven( GpuDrivenBucket bucket, Format color, Format depth, int samples, bool ids )
	{
		var r = recording;
		var pass = bucket.Surface;
		var vertexConstants = SurfaceConstants( pass.Pass.Vertex, out var vertexKey );
		ulong pixelKey = 0;
		var pixelConstants = pass.Pass.Pixel is { } pixel ? SurfaceConstants( pixel, out pixelKey ) : null;
		var pipeline = bucket.SurfaceProgram.Pipeline( pass, device.PulledLayout, color, depth, samples, vertexConstants, pixelConstants, vertexKey * 31 + pixelKey, ids );
		if ( r.Pipeline != pipeline )
		{
			r.List.SetPipeline( pipeline );
			r.Pipeline = pipeline;
		}

		BindSurface( pass, pass.Pass.Vertex, ShaderAbi.VertexSet );
		if ( pass.Pass.Pixel is not null ) BindSurface( pass, pass.Pass.Pixel, ShaderAbi.FragmentSet );
		r.Binder = null;
	}

	/// <summary>
	/// A stage's specialization constants (<c>SpecializedConstant()</c> globals: <c>ShadowFilterQuality</c>,
	/// <c>DDGI_Enabled</c>, <c>ToolsVisMode</c>), each from its attribute or its default, as classic variants take them;
	/// <paramref name="key"/> tells their values apart for the pipeline cache.
	/// </summary>
	SpecializationConstant[] SurfaceConstants( CompiledStage stage, out ulong key )
	{
		// Where the pixel stage reads the material record's offset (sbox/material.slang): passed on per pixel, or the draw's
		var perPixel = device.Surfaces.MaterialOffsetPerPixel;
		key = perPixel ? 1u : 2u;
		var constants = new SpecializationConstant[stage.Specializations.Count + 1];
		constants[^1] = new SpecializationConstant( MaterialOffsetPerPixelId, perPixel ? 1u : 0u );
		for ( int i = 0; i < stage.Specializations.Count; i++ )
		{
			var slot = stage.Specializations[i];
			var value = Integer( Lookup( Token( slot.Attribute ) ) ) ?? (int)(slot.Annotations?.Default is { Length: > 0 } d ? d[0] : 0);
			constants[i] = new SpecializationConstant( (uint)slot.Id, (uint)value );
			key = key * 1000003 + (uint)value + 1;
		}

		return constants;
	}

	/// <summary>One stage's set: every binding its SPIR-V uses, by the name the engine binds it by.</summary>
	void BindSurface( SurfaceProgram.PassObjects pass, CompiledStage stage, int set )
	{
		if ( !pass.Sets[set].IsValid ) return;
		Span<Binding> bindings = stackalloc Binding[64];
		var count = 0;
		foreach ( var b in stage.Bindings )
		{
			if ( b.Set != set ) continue;
			if ( count == bindings.Length ) throw new InvalidOperationException( $"{stage.Entry} binds more than {bindings.Length} resources in set {set}" );
			bindings[count++] = SurfaceBinding( stage, b );
		}

		recording.List.SetBindings( set, pass.Sets[set], bindings[..count] );
	}

	Binding SurfaceBinding( CompiledStage stage, ShaderBinding b )
	{
		var defaults = device.Defaults;
		switch ( b.Kind )
		{
			case BindingKind.Globals:
				return SurfaceGlobals( stage.Globals, b.Binding );

			case BindingKind.MaterialRecords:
				return Binding.Storage( b.Binding, device.Surfaces.Records.Buffer );

			case BindingKind.ConstantBuffer:
				{
					var data = Lookup( Token( b.Name ) ) as byte[];
					var size = (Math.Max( data?.Length ?? 0, b.Size ) + 15) & ~15;
					if ( size == 0 ) return Binding.Uniform( b.Binding, defaults.Uniform, 0, ShaderDefaults.UniformSize );
					var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
					allocation.Data[..size].Clear();
					data?.CopyTo( allocation.Data );
					return Binding.Uniform( b.Binding, allocation.Buffer, allocation.Offset, size );
				}

			case BindingKind.Buffer:
				return Lookup( Token( b.Name ) ) is GpuBuffer { Resource: BackendBuffer buffer } ? Binding.Storage( b.Binding, buffer.Handle ) : Binding.Storage( b.Binding, defaults.Storage );

			case BindingKind.Sampler:
				return Binding.SamplerState( b.Binding, defaults.Sampler( SurfaceSampler( b.Annotations ) ) );

			case BindingKind.StorageTexture:
				return Binding.StorageTexture( b.Binding, defaults.Texture( Dimension( b.Shape ) ) );

			default:
				{
					var expected = b.Shape switch
					{
						TextureShape.Cube => TextureDimension.TextureCube,
						TextureShape.CubeArray => TextureDimension.TextureCubeArray,
						TextureShape.Texture3D => TextureDimension.Texture3D,
						TextureShape.Texture2DArray => TextureDimension.Texture2DArray,
						_ => TextureDimension.Texture2D,
					};

					if ( Lookup( Token( b.Name ) ) is not BackendTexture texture || texture.Dimension != expected || texture.Samples > 1 || SampledLayoutOf( texture ) != ImageLayout.ShaderRead )
						return Binding.Sampled( b.Binding, defaults.Texture( Dimension( b.Shape ) ) );

					var srgb = (b.Annotations?.SrgbRead ?? false) && Gpu.InfoOf( texture.Handle ).Format.HasSrgb();
					return Binding.Sampled( b.Binding, texture.Handle, srgb );
				}
		}
	}

	static SpirvImageDimension Dimension( TextureShape shape ) => shape switch
	{
		TextureShape.Cube => SpirvImageDimension.Cube,
		TextureShape.CubeArray => SpirvImageDimension.CubeArray,
		TextureShape.Texture3D => SpirvImageDimension.Texture3D,
		TextureShape.Texture2DArray => SpirvImageDimension.Texture2DArray,
		_ => SpirvImageDimension.Texture2D,
	};

	/// <summary>
	/// The stage's <c>$Globals</c>: each member from the attribute of its name (its annotation's), else its annotated
	/// default, in upload memory. <c>UncompressedTangentFrame</c> comes from the draw's vertex layout, as for classic draws.
	/// </summary>
	Binding SurfaceGlobals( GlobalConstants globals, int binding )
	{
		var size = Math.Max( globals?.Size ?? 16, 16 );
		var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
		var data = allocation.Data[..size];
		data.Clear();
		foreach ( var member in globals?.Members ?? [] )
		{
			var value = member.Attribute == "UncompressedTangentFrame" ? [uncompressedTangentFrame ? 1 : 0] : Floats( Lookup( Token( member.Attribute ) ) ) ?? member.Annotations?.Default;
			if ( value is null ) continue;
			for ( int i = 0; i < member.Components && i < value.Length && member.Offset + i * 4 + 4 <= size; i++ )
			{
				var target = data[(member.Offset + i * 4)..];
				switch ( member.Kind )
				{
					case ScalarKind.Float:
						BinaryPrimitives.WriteSingleLittleEndian( target, value[i] );
						break;
					case ScalarKind.Bool:
						BinaryPrimitives.WriteInt32LittleEndian( target, value[i] != 0 ? 1 : 0 );
						break;
					default:
						BinaryPrimitives.WriteInt32LittleEndian( target, (int)MathF.Round( value[i] ) );
						break;
				}
			}
		}

		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// A sampler from vfx's sampler-state annotations, as classic shaders' sampler states make them: <c>Filter</c>
	/// (ANISOTROPIC, BILINEAR, TRILINEAR, POINT or a D3D filter name, COMPARISON_ for comparison), <c>AddressU/V/W</c>,
	/// <c>MaxAniso</c>, <c>ComparisonFunc</c>; trilinear wrap without any.
	/// </summary>
	internal static SamplerInfo SurfaceSampler( Annotations annotations )
	{
		var sampler = SamplerInfo.Linear( AddressMode.Wrap );
		if ( annotations is null ) return sampler;

		if ( annotations.Filter is { } filter )
		{
			var name = filter.Trim().ToUpperInvariant();
			var comparison = name.StartsWith( "COMPARISON_", StringComparison.Ordinal );
			if ( comparison ) name = name["COMPARISON_".Length..];

			switch ( name )
			{
				case "ANISOTROPIC" or "ANISO":
					sampler.MaxAnisotropy = annotations.MaxAniso > 0 ? annotations.MaxAniso : 16;
					break;
				case "BILINEAR":
					sampler.MipFilter = Filter.Nearest;
					break;
				case "TRILINEAR":
					break;
				case "POINT" or "NEAREST":
					sampler.MinFilter = sampler.MagFilter = sampler.MipFilter = Filter.Nearest;
					break;
				default:
					// MIN_<f>_MAG_<f>_MIP_<f>, MIN_MAG_<f>_MIP_<f>, MIN_MAG_MIP_<f>
					var min = Part( name, "MIN_" );
					var mag = Part( name, "MAG_" );
					var mip = Part( name, "MIP_" );
					if ( name.StartsWith( "MIN_MAG_MIP_", StringComparison.Ordinal ) ) min = mag = mip;
					else if ( name.StartsWith( "MIN_MAG_", StringComparison.Ordinal ) ) min = mag;
					sampler.MinFilter = min;
					sampler.MagFilter = mag;
					sampler.MipFilter = mip;
					break;
			}

			if ( comparison ) sampler.Compare = CompareFunction.LessEqual;
		}

		if ( annotations.AddressU is { } u ) sampler.AddressU = Address( u );
		if ( annotations.AddressV is { } v ) sampler.AddressV = Address( v );
		if ( annotations.AddressW is { } w ) sampler.AddressW = Address( w );
		if ( annotations.Comparison is { } compare ) sampler.Compare = Compare( compare );
		return sampler;
	}

	static Filter Part( string name, string prefix )
	{
		var at = name.IndexOf( prefix, StringComparison.Ordinal );
		if ( at < 0 ) return Filter.Linear;
		var rest = name[(at + prefix.Length)..];
		while ( rest.StartsWith( "MAG_", StringComparison.Ordinal ) || rest.StartsWith( "MIP_", StringComparison.Ordinal ) ) rest = rest[4..];
		return rest.StartsWith( "POINT", StringComparison.Ordinal ) ? Filter.Nearest : Filter.Linear;
	}

	static AddressMode Address( string mode ) => mode.Trim().ToUpperInvariant() switch
	{
		"CLAMP" => AddressMode.Clamp,
		"BORDER" => AddressMode.Border,
		"MIRROR" or "MIRROR_ONCE" => AddressMode.Mirror,
		_ => AddressMode.Wrap,
	};

	static CompareFunction Compare( string function ) => function.Trim().ToUpperInvariant() switch
	{
		"NEVER" => CompareFunction.Never,
		"LESS" => CompareFunction.Less,
		"EQUAL" => CompareFunction.Equal,
		"LESS_EQUAL" => CompareFunction.LessEqual,
		"GREATER" => CompareFunction.Greater,
		"NOT_EQUAL" => CompareFunction.NotEqual,
		"GREATER_EQUAL" => CompareFunction.GreaterEqual,
		_ => CompareFunction.Always,
	};
}
