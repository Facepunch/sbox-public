using Borealis.Gpu;
using Sandbox.ShaderCompiler;
using System.Runtime.CompilerServices;

namespace Borealis.Backend;

public sealed partial class ShaderDrawer
{
	/// <summary>
	/// Draw <paramref name="draw"/>'s geometry with a material whose shader is a surface (<see cref="SurfaceMaterial"/>: a
	/// shader with no compiled <c>.shader</c>, as standard, skin and glass are), as Borealis's scene draws it: the pass for
	/// the mode and the material's render state (forward, alpha-tested or translucent forward with colour; depth, alpha-tested
	/// or the surface's own dithered depth without), its pipeline for these vertices and topology, its stages' sets bound by
	/// name from <paramref name="attributes"/> as a classic draw's are, and the material's record. Instance ids are the lent
	/// ones (<see cref="InstanceIds"/>), else every instance reads id 0, as a classic draw's do. False, drawing nothing, when
	/// the drawer wasn't made over a device, the surface hasn't compiled yet, it has no pass for the mode, or the draw has no
	/// vertices of its own (pulled or from the vertex cache).
	/// </summary>
	public bool DrawSurface( CommandList list, SurfaceMaterial material, ShaderAttributes attributes, Format color, Format depth, int samples, in GeometryDraw draw, System.Numerics.Vector4 viewport, string mode = null )
	{
		if ( device is null || material is null || draw.Layout is null || !draw.Vertices.IsValid ) return false;
		if ( attributes.GetCombo( VertexAnimationId ) is > 0 ) return false;
		if ( device.Surfaces.For( material ) is not { } program ) return false;
		var state = material.RenderState;
		if ( SurfacePass( program, state, color, mode, attributes.GetCombo( LightmapId ) is > 0 ) is not { } pass ) return false;

		var ids = InstanceIds.Buffer.IsValid;
		var vertexConstants = SurfaceStageConstants( pass.Pass.Vertex, attributes );
		var pixelConstants = pass.Pass.Pixel is { } pixel ? SurfaceStageConstants( pixel, attributes ) : null;
		var fade = attributes.GetCombo( OpaqueFadeId ) is > 0;
		var pipeline = program.ImmediatePipeline( pass, SurfaceLayoutOf( draw.Layout ), color, depth, samples, vertexConstants, pixelConstants, state, draw.Topology, ids, fade, ToolsPass, OverlayStencil );

		if ( BackendDevice.DrawMarkers ) list.Marker( $"{material.Name} {pass.Pass.Name} (engine draw)" );
		list.SetPipeline( pipeline );
		BackendDevice.SetStencilReference( list, depth, OverlayStencil );
		SetViewport( list, viewport );

		var record = device.Surfaces.Records.Offset( material, out var records );
		var tangent = attributes.Get( TangentFrameId ) is float[] { Length: > 0 } t && t[0] != 0;
		BindSurfaceStage( list, pass, pass.Pass.Vertex, ShaderAbi.VertexSet, attributes, records, tangent );
		if ( pass.Pass.Pixel is not null ) BindSurfaceStage( list, pass, pass.Pass.Pixel, ShaderAbi.FragmentSet, attributes, records, tangent );
		list.PushConstants( record );

		// Into depth alone, the depth bias is dynamic, as for classic draws (DepthBias); with colour, the material's own
		if ( color == Format.Unknown && depth != Format.Unknown ) list.SetDepthBias( DepthBias.Constant, DepthBias.Slope );
		else if ( color != Format.Unknown ) SurfaceProgram.ColorDepthBias( list, state );

		list.SetVertexBuffer( 0, draw.Vertices, draw.VertexOffset );
		if ( ids ) list.SetVertexBuffer( 1, InstanceIds.Buffer, InstanceIds.Offset );
		else
		{
			// Instance id 0 for every instance: the transform the draw binds first, as a classic draw reads it from the default stream
			var zero = SurfaceUniform( list, zeroInstanceId );
			list.SetVertexBuffer( 1, zero.Buffer, zero.Offset );
		}

		list.SetVertexBuffer( 2, defaults.VertexDefaults );

		var indexed = draw.Indices.IsValid;
		if ( indexed ) list.SetIndexBuffer( draw.Indices, draw.IndexFormat, draw.IndexOffset );
		var instances = Math.Max( draw.Instances, 1 );
		var indirect = draw.Arguments.IsValid;
		if ( indirect && indexed ) list.DrawIndexedIndirect( draw.Arguments, draw.ArgumentsOffset, draw.DrawCount, draw.Stride > 0 ? draw.Stride : 20 );
		else if ( indirect ) list.DrawIndirect( draw.Arguments, draw.ArgumentsOffset, draw.DrawCount, draw.Stride > 0 ? draw.Stride : 16 );
		else if ( indexed ) list.DrawIndexed( draw.Count, instances, draw.First, draw.BaseVertex );
		else list.Draw( draw.Count, instances, draw.First );
		return true;
	}

	/// <summary>
	/// Whether <see cref="DrawSurface"/> of <paramref name="material"/> with these attributes, targets and mode reads its
	/// instance's index (<c>SV_InstanceID</c>), so leaving instances out would change what it draws; true when it can't tell.
	/// </summary>
	public bool SurfaceReadsInstanceIndex( SurfaceMaterial material, ShaderAttributes attributes, Format color, string mode = null )
	{
		if ( device is null || material is null || device.Surfaces.For( material ) is not { } program ) return true;
		return SurfacePass( program, material.RenderState, color, mode, attributes.GetCombo( LightmapId ) is > 0 ) is not { } pass || pass.ReadsInstanceIndex;
	}

	/// <summary>
	/// The pass a surface draws with: in a colour pass (no mode, or <c>Forward</c>) the forward pass for its render state; into
	/// depth alone (no colour target, or the <c>Depth</c> mode) its depth pass, or, where it writes no depth or draws in the
	/// translucent layer, the dithered depth of a surface with a shadow of its own. The lightmapped pass where the draw's
	/// object is lightmapped and the surface has one. Null for any other mode: the draw is skipped, as a .shader's is for a
	/// mode it lacks.
	/// </summary>
	static SurfaceProgram.PassObjects SurfacePass( SurfaceProgram program, SurfaceRenderState state, Format color, string mode, bool lightmapped )
	{
		var depthMode = string.Equals( mode, ShaderPasses.Depth, StringComparison.OrdinalIgnoreCase );
		if ( mode is not null && !depthMode && !string.Equals( mode, ShaderPasses.Forward, StringComparison.OrdinalIgnoreCase ) ) return null;

		string name;
		if ( color == Format.Unknown || depthMode )
		{
			if ( !state.DepthWrite || state.Translucent ) name = BackendContext.DepthTranslucentPass;
			else name = state.AlphaTest ? ShaderPasses.DepthAlphaTest : ShaderPasses.Depth;
		}
		else if ( state.DrawsTranslucent ) name = ShaderPasses.Forward + "Translucent";
		else name = state.AlphaTest && program.Pass( ShaderPasses.Forward + "AlphaTest" ) is not null ? ShaderPasses.Forward + "AlphaTest" : ShaderPasses.Forward;

		return (lightmapped ? program.Pass( name + ShaderPasses.Lightmapped ) : null) ?? program.Pass( name );
	}

	/// <summary>
	/// A surface stage's specialization constants for a draw: each slot's attribute, else the dynamic combo of that name, else
	/// its default, then the 3D skybox, where the pixel stage reads the record's offset, and fading, as scene draws make them.
	/// </summary>
	// Constants per stage and attribute values, kept for consecutive draws: their arrays key the pipeline and are costly to build
	struct StageConstants
	{
		public CompiledStage Stage;
		public ShaderAttributes Attributes;
		public int Version;
		public bool PerPixel;
		public SpecializationConstant[] Constants;
	}

	readonly StageConstants[] stageConstants = new StageConstants[4];
	int stageConstantsNext;
	static readonly int VertexAnimationId = Borealis.Source2.Shaders.AttributeIds.Of( "D_CS_VERTEX_ANIMATION" );
	static readonly int LightmapId = Borealis.Source2.Shaders.AttributeIds.Of( "D_BAKED_LIGHTING_FROM_LIGHTMAP" );
	static readonly int OpaqueFadeId = Borealis.Source2.Shaders.AttributeIds.Of( "D_OPAQUE_FADE" );
	static readonly int SkyboxComboId = Borealis.Source2.Shaders.AttributeIds.Of( "D_SKYBOX" );

	SpecializationConstant[] SurfaceStageConstants( CompiledStage stage, ShaderAttributes attributes )
	{
		var perPixel = device.Surfaces.MaterialOffsetPerPixel;
		if ( attributes.Stable )
		{
			foreach ( ref var kept in stageConstants.AsSpan() )
			{
				if ( ReferenceEquals( kept.Stage, stage ) && ReferenceEquals( kept.Attributes, attributes ) && kept.Version == attributes.Version && kept.PerPixel == perPixel )
					return kept.Constants;
			}
		}

		var made = MakeSurfaceStageConstants( stage, attributes, perPixel );
		if ( attributes.Stable )
		{
			stageConstants[stageConstantsNext] = new StageConstants { Stage = stage, Attributes = attributes, Version = attributes.Version, PerPixel = perPixel, Constants = made };
			stageConstantsNext = (stageConstantsNext + 1) & 3;
		}

		return made;
	}

	// Interned per stage so all drawers use the same identity for equal values in a program's pipeline cache
	static readonly ConditionalWeakTable<CompiledStage, List<SpecializationConstant[]>> internedConstants = new();

	[ThreadStatic] static SpecializationConstant[] constantsScratch;

	SpecializationConstant[] MakeSurfaceStageConstants( CompiledStage stage, ShaderAttributes attributes, bool perPixel )
	{
		var fade = attributes.GetCombo( OpaqueFadeId ) is > 0;
		var skybox = attributes.GetCombo( SkyboxComboId ) is > 0;

		var length = stage.Specializations.Count + 3;
		if ( constantsScratch is null || constantsScratch.Length < length ) constantsScratch = new SpecializationConstant[Math.Max( length, 16 )];
		var constants = constantsScratch.AsSpan( 0, length );
		for ( int i = 0; i < stage.Specializations.Count; i++ )
		{
			var slot = stage.Specializations[i];
			var value = attributes.Get( slot.Attribute ) is float[] { Length: > 0 } given ? (int)given[0]
				: attributes.GetCombo( slot.Attribute ) ?? (int)(slot.Annotations?.Default is { Length: > 0 } d ? d[0] : 0);
			constants[i] = new SpecializationConstant( (uint)slot.Id, (uint)value );
		}

		constants[^3] = new SpecializationConstant( BackendContext.SkyboxId, skybox ? 1u : 0u );
		constants[^2] = new SpecializationConstant( BackendContext.MaterialOffsetPerPixelId, perPixel ? 1u : 0u );
		constants[^1] = new SpecializationConstant( BackendContext.OpaqueFadeId, fade ? 1u : 0u );

		var known = internedConstants.GetValue( stage, static _ => new() );
		lock ( known )
		{
			foreach ( var kept in known )
			{
				if ( constants.SequenceEqual( kept ) ) return kept;
			}

			var made = constants.ToArray();
			known.Add( made );
			return made;
		}
	}

	/// <summary>
	/// One stage's set: every binding it uses, by the name the engine binds it by, from the draw's attributes or a placeholder;
	/// its <c>$Globals</c> from the members' attributes over their defaults; the records buffer holding the draw's record.
	/// </summary>
	void BindSurfaceStage( CommandList list, SurfaceProgram.PassObjects pass, CompiledStage stage, int set, ShaderAttributes attributes, BufferHandle records, bool tangent )
	{
		if ( !pass.Sets[set].IsValid ) return;
		Span<Binding> bindings = stackalloc Binding[64];
		var count = 0;
		var kept = surfaceNames.GetValue( stage, static s =>
		{
			var ids = new int[s.Bindings.Count];
			for ( int i = 0; i < ids.Length; i++ ) ids[i] = s.Bindings[i].Name is { } name ? Borealis.Source2.Shaders.AttributeIds.Of( name ) : -1;
			return new StageBindingNames { Ids = ids, Samplers = new Binding?[ids.Length] };
		} );
		var names = kept.Ids;

		for ( int i = 0; i < names.Length; i++ )
		{
			var b = stage.Bindings[i];
			if ( b.Set != set ) continue;
			if ( count == bindings.Length ) throw new InvalidOperationException( $"{stage.Entry} binds more than {bindings.Length} resources in set {set}" );
			bindings[count++] = b.Kind switch
			{
				BindingKind.Globals => SurfaceGlobals( list, stage.Globals, b.Binding, attributes, tangent ),
				BindingKind.MaterialRecords => Binding.Storage( b.Binding, records ),
				BindingKind.ConstantBuffer => SurfaceConstantBuffer( list, b, attributes.Get( names[i] ) as byte[] ),
				BindingKind.Buffer => Binding.Storage( b.Binding, attributes.Get( names[i] ) is BufferHandle { IsValid: true } buffer ? buffer : defaults.Storage ),
				BindingKind.Sampler => kept.Samplers[i] ??= Binding.SamplerState( b.Binding, defaults.Sampler( BackendContext.SurfaceSampler( b.Annotations ) ) ),
				BindingKind.StorageTexture => Binding.StorageTexture( b.Binding, defaults.StorageTexture( BackendContext.Dimension( b.Shape ) ) ),
				_ => attributes.Get( names[i] ) switch
				{
					TextureHandle { IsValid: true } texture => Binding.Sampled( b.Binding, texture, (b.Annotations?.SrgbRead ?? false) && gpu.InfoOf( texture ).Format.HasSrgb() ),
					TextureViewHandle { IsValid: true } view => Binding.Sampled( b.Binding, view ),
					_ => Binding.Sampled( b.Binding, defaults.Texture( BackendContext.Dimension( b.Shape ) ) ),
				},
			};
		}

		list.SetBindings( set, pass.Sets[set], bindings[..count] );
	}

	// The attribute ids of a stage's bindings, in order (-1 for one with no name), and the sampler bindings made for them
	sealed class StageBindingNames
	{
		public int[] Ids;
		public Binding?[] Samplers;
	}

	readonly ConditionalWeakTable<CompiledStage, StageBindingNames> surfaceNames = new();
	static readonly int TangentFrameId = Borealis.Source2.Shaders.AttributeIds.Of( "UncompressedTangentFrame" );

	Binding SurfaceConstantBuffer( CommandList list, ShaderBinding b, byte[] data )
	{
		var size = (Math.Max( data?.Length ?? 0, b.Size ) + 15) & ~15;
		if ( size == 0 ) return Binding.Uniform( b.Binding, defaults.Uniform, 0, Borealis.Source2.Shaders.ShaderDefaults.UniformSize );
		var bytes = Scratch( size );
		bytes.Clear();
		data?.CopyTo( bytes );
		var (buffer, offset) = SurfaceUniform( list, bytes );
		return Binding.Uniform( b.Binding, buffer, offset, size );
	}

	/// <summary>A stage's <c>$Globals</c> for a draw: the attribute-bound members from the draw's attributes, the rest their defaults.</summary>
	Binding SurfaceGlobals( CommandList list, GlobalConstants globals, int binding, ShaderAttributes attributes, bool tangent )
	{
		var size = Math.Max( globals?.Size ?? 16, 16 );
		var data = Scratch( size );
		data.Clear();
		if ( globals is not null )
		{
			var layout = BackendContext.GlobalLayouts.GetValue( globals, static g => new BackendContext.SurfaceGlobalsLayout( g ) );
			layout.Defaults.AsSpan( 0, Math.Min( layout.Defaults.Length, size ) ).CopyTo( data );
			Span<float> one = stackalloc float[1];
			for ( int a = 0; a < layout.Bound.Length; a++ )
			{
				var member = globals.Members[layout.Bound[a]];
				if ( layout.Ids[a] == TangentFrameId )
				{
					one[0] = tangent ? 1 : 0;
					BackendContext.SurfaceGlobalsLayout.Write( data, member, one );
				}
				else if ( attributes.Get( layout.Ids[a] ) is float[] value )
				{
					BackendContext.SurfaceGlobalsLayout.Write( data, member, value );
				}
			}
		}

		var (buffer, offset) = SurfaceUniform( list, data );
		return Binding.Uniform( binding, buffer, offset, size );
	}

	// A draw's uniform bytes are made here and only then copied out: upload memory is write-combined and very slow to read back
	byte[] uniformScratch = new byte[256];
	static readonly byte[] zeroInstanceId = new byte[16];

	Span<byte> Scratch( int size )
	{
		if ( uniformScratch.Length < size ) uniformScratch = new byte[Math.Max( size, uniformScratch.Length * 2 )];
		return uniformScratch.AsSpan( 0, size );
	}

	// Uniforms surface draws wrote into the list being recorded, by content. Consecutive draws with the same attributes write
	// the same $Globals and constant buffers: binding the copy already written keeps the stage's bindings as they were, so
	// the list keeps the descriptor set it wrote and bound for them instead of allocating, writing and binding another.
	sealed class SurfaceUpload
	{
		public byte[] Bytes = [];
		public int Size = -1;
		public BufferHandle Buffer;
		public long Offset;
	}

	const int SurfaceUploadsKept = 8;
	readonly SurfaceUpload[] surfaceUploads = new SurfaceUpload[SurfaceUploadsKept];
	int surfaceUploadNext;

	// The recording the kept uploads are in: a list recycled within a frame (Device.TryRecycle) was submitted in between
	CommandList uploadList;
	ulong uploadFrame;
	long uploadSubmit;

	/// <summary>Where <paramref name="data"/> is in the list's upload memory: a copy written earlier in this recording with the same bytes, else a new one.</summary>
	(BufferHandle Buffer, long Offset) SurfaceUniform( CommandList list, ReadOnlySpan<byte> data )
	{
		var frame = gpu.FrameNumber;
		if ( !ReferenceEquals( list, uploadList ) || frame != uploadFrame || list.SubmitOrder != uploadSubmit )
		{
			(uploadList, uploadFrame, uploadSubmit) = (list, frame, list.SubmitOrder);
			foreach ( var kept in surfaceUploads )
			{
				if ( kept is not null ) kept.Size = -1;
			}
		}

		foreach ( var kept in surfaceUploads )
		{
			if ( kept is not null && kept.Size == data.Length && data.SequenceEqual( kept.Bytes.AsSpan( 0, kept.Size ) ) ) return (kept.Buffer, kept.Offset);
		}

		var allocation = list.AllocateUpload( data.Length, gpu.Capabilities.UniformAlignment );
		data.CopyTo( allocation.Data );

		var made = surfaceUploads[surfaceUploadNext] ??= new SurfaceUpload();
		surfaceUploadNext = (surfaceUploadNext + 1) % SurfaceUploadsKept;
		if ( made.Bytes.Length < data.Length ) made.Bytes = new byte[data.Length];
		data.CopyTo( made.Bytes );
		(made.Size, made.Buffer, made.Offset) = (data.Length, allocation.Buffer, allocation.Offset);
		return (allocation.Buffer, allocation.Offset);
	}

	readonly ConditionalWeakTable<VertexStreamLayout, VertexLayout> surfaceLayouts = new();

	/// <summary>The engine's vertex layout as a scene mesh's, which surface pipelines are made for.</summary>
	VertexLayout SurfaceLayoutOf( VertexStreamLayout layout ) => surfaceLayouts.GetValue( layout, static l =>
	{
		var fields = new VertexLayout.Field[l.Fields.Count];
		for ( int i = 0; i < fields.Length; i++ ) fields[i] = new VertexLayout.Field( l.Fields[i].Semantic, l.Fields[i].Index, l.Fields[i].Format, l.Fields[i].Offset );
		return new VertexLayout( l.Stride, fields );
	} );
}
