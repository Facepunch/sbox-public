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
		if ( attributes.GetCombo( "D_CS_VERTEX_ANIMATION" ) is > 0 ) return false;
		if ( device.Surfaces.For( material ) is not { } program ) return false;
		var state = material.RenderState;
		if ( SurfacePass( program, state, color, mode, attributes.GetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP" ) is > 0 ) is not { } pass ) return false;

		var ids = InstanceIds.Buffer.IsValid;
		var vertexConstants = SurfaceStageConstants( pass.Pass.Vertex, attributes, out var vertexKey );
		ulong pixelKey = 0;
		var pixelConstants = pass.Pass.Pixel is { } pixel ? SurfaceStageConstants( pixel, attributes, out pixelKey ) : null;
		var fade = attributes.GetCombo( "D_OPAQUE_FADE" ) is > 0;
		var pipeline = program.ImmediatePipeline( pass, SurfaceLayoutOf( draw.Layout ), color, depth, samples, vertexConstants, pixelConstants, vertexKey * 31 + pixelKey, state, draw.Topology, ids, fade, ToolsPass );

		if ( BackendDevice.DrawMarkers ) list.Marker( $"{material.Name} {pass.Pass.Name} (engine draw)" );
		list.SetPipeline( pipeline );
		SetViewport( list, viewport );

		var record = device.Surfaces.Records.Offset( material, out var records );
		var tangent = attributes.Get( "UncompressedTangentFrame" ) is float[] { Length: > 0 } t && t[0] != 0;
		BindSurfaceStage( list, pass, pass.Pass.Vertex, ShaderAbi.VertexSet, attributes, records, tangent );
		if ( pass.Pass.Pixel is not null ) BindSurfaceStage( list, pass, pass.Pass.Pixel, ShaderAbi.FragmentSet, attributes, records, tangent );
		list.PushConstants( record );

		// Into depth alone, the depth bias is dynamic, as for classic draws (DepthBias)
		if ( color == Format.Unknown && depth != Format.Unknown ) list.SetDepthBias( DepthBias.Constant, DepthBias.Slope );

		list.SetVertexBuffer( 0, draw.Vertices, draw.VertexOffset );
		if ( ids ) list.SetVertexBuffer( 1, InstanceIds.Buffer, InstanceIds.Offset );
		else
		{
			// Instance id 0 for every instance: the transform the draw binds first, as a classic draw reads it from the default stream
			var zero = list.AllocateUpload( 16, 16 );
			zero.Data[..16].Clear();
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
	SpecializationConstant[] SurfaceStageConstants( CompiledStage stage, ShaderAttributes attributes, out ulong key )
	{
		var perPixel = device.Surfaces.MaterialOffsetPerPixel;
		var fade = attributes.GetCombo( "D_OPAQUE_FADE" ) is > 0;
		var skybox = attributes.GetCombo( "D_SKYBOX" ) is > 0;
		key = (perPixel ? 1u : 2u) + (fade ? 4u : 0u) + (skybox ? 8u : 0u);

		var constants = new SpecializationConstant[stage.Specializations.Count + 3];
		for ( int i = 0; i < stage.Specializations.Count; i++ )
		{
			var slot = stage.Specializations[i];
			var value = attributes.Get( slot.Attribute ) is float[] { Length: > 0 } given ? (int)given[0]
				: attributes.GetCombo( slot.Attribute ) ?? (int)(slot.Annotations?.Default is { Length: > 0 } d ? d[0] : 0);
			constants[i] = new SpecializationConstant( (uint)slot.Id, (uint)value );
			key = key * 1000003 + (uint)value + 1;
		}

		constants[^3] = new SpecializationConstant( BackendContext.SkyboxId, skybox ? 1u : 0u );
		constants[^2] = new SpecializationConstant( BackendContext.MaterialOffsetPerPixelId, perPixel ? 1u : 0u );
		constants[^1] = new SpecializationConstant( BackendContext.OpaqueFadeId, fade ? 1u : 0u );
		return constants;
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
		foreach ( var b in stage.Bindings )
		{
			if ( b.Set != set ) continue;
			if ( count == bindings.Length ) throw new InvalidOperationException( $"{stage.Entry} binds more than {bindings.Length} resources in set {set}" );
			bindings[count++] = b.Kind switch
			{
				BindingKind.Globals => SurfaceGlobals( list, stage.Globals, b.Binding, attributes, tangent ),
				BindingKind.MaterialRecords => Binding.Storage( b.Binding, records ),
				BindingKind.ConstantBuffer => SurfaceConstantBuffer( list, b, attributes.Get( b.Name ) as byte[] ),
				BindingKind.Buffer => Binding.Storage( b.Binding, attributes.Get( b.Name ) is BufferHandle { IsValid: true } buffer ? buffer : defaults.Storage ),
				BindingKind.Sampler => Binding.SamplerState( b.Binding, defaults.Sampler( BackendContext.SurfaceSampler( b.Annotations ) ) ),
				BindingKind.StorageTexture => Binding.StorageTexture( b.Binding, defaults.StorageTexture( BackendContext.Dimension( b.Shape ) ) ),
				_ => attributes.Get( b.Name ) switch
				{
					TextureHandle { IsValid: true } texture => Binding.Sampled( b.Binding, texture, (b.Annotations?.SrgbRead ?? false) && gpu.InfoOf( texture ).Format.HasSrgb() ),
					TextureViewHandle { IsValid: true } view => Binding.Sampled( b.Binding, view ),
					_ => Binding.Sampled( b.Binding, defaults.Texture( BackendContext.Dimension( b.Shape ) ) ),
				},
			};
		}

		list.SetBindings( set, pass.Sets[set], bindings[..count] );
	}

	Binding SurfaceConstantBuffer( CommandList list, ShaderBinding b, byte[] data )
	{
		var size = (Math.Max( data?.Length ?? 0, b.Size ) + 15) & ~15;
		if ( size == 0 ) return Binding.Uniform( b.Binding, defaults.Uniform, 0, Borealis.Source2.Shaders.ShaderDefaults.UniformSize );
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		allocation.Data[..size].Clear();
		data?.CopyTo( allocation.Data );
		return Binding.Uniform( b.Binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>A stage's <c>$Globals</c> for a draw: the attribute-bound members from the draw's attributes, the rest their defaults.</summary>
	Binding SurfaceGlobals( CommandList list, GlobalConstants globals, int binding, ShaderAttributes attributes, bool tangent )
	{
		var size = Math.Max( globals?.Size ?? 16, 16 );
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		var data = allocation.Data[..size];
		data.Clear();
		if ( globals is null ) return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );

		var layout = BackendContext.GlobalLayouts.GetValue( globals, static g => new BackendContext.SurfaceGlobalsLayout( g ) );
		layout.Defaults.AsSpan( 0, Math.Min( layout.Defaults.Length, size ) ).CopyTo( data );
		Span<float> one = stackalloc float[1];
		foreach ( var index in layout.Bound )
		{
			var member = globals.Members[index];
			if ( member.Attribute == "UncompressedTangentFrame" )
			{
				one[0] = tangent ? 1 : 0;
				BackendContext.SurfaceGlobalsLayout.Write( data, member, one );
			}
			else if ( attributes.Get( member.Attribute ) is float[] value )
			{
				BackendContext.SurfaceGlobalsLayout.Write( data, member, value );
			}
		}

		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
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
