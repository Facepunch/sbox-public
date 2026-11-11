using Basalt.Gpu;
using Basalt.Source2.Shaders;

namespace Basalt.Backend;

/// <summary>
/// Drawing through material objects (<see cref="BackendDevice.MaterialObjects"/>): the same bindings as
/// <see cref="DrawModel"/> makes by reflection, but resolved once per material, way of drawing and pass state, into
/// persistent sets bound with dynamic offsets. See docs/managed/basalt-materials.md.
/// </summary>
internal sealed partial class BackendContext
{
	/// <summary>
	/// The pass's bindings for a material object: attribute constant buffers copied once per pass state into the cache's
	/// pass buffer, buffers and textures by attribute name.
	/// </summary>
	internal sealed class PassResolver( BackendContext context ) : PassBindings
	{
		long epoch = -1;
		readonly Dictionary<(string Name, int Size), (Binding Binding, uint Offset)> constants = new();
		readonly Dictionary<(string Name, ShaderResourceKind Kind, SpirvImageDimension Dimension, int RenderStateId), Binding> resources = new();

		public void Begin( long passEpoch )
		{
			if ( passEpoch == epoch ) return;
			epoch = passEpoch;
			constants.Clear();
			resources.Clear();
		}

		public override float[] Floats( string attribute ) => BackendContext.Floats( context.attributes.Get( Token( attribute ) ) );

		public override float[] LayerFloats( string attribute ) => context.LayerValue( Token( attribute ), out _ ) is { } value ? BackendContext.Floats( value ) : null;

		/// <summary>
		/// What the object's attributes bind for a pass resource: constant buffers copied once a frame per object (at a
		/// dynamic offset when the slot is dynamic), buffers and textures by name.
		/// </summary>
		public override bool ResolveLayer( in ShaderResource resource, ShaderStage stage, bool dynamic, out Binding binding, out uint dynamicOffset )
		{
			binding = default;
			dynamicOffset = 0;
			if ( resource.Name is not { } name ) return false;

			var token = Token( name );
			var value = context.LayerValue( token, out var from );
			if ( value is null ) return false;

			var device = context.device;
			switch ( resource.Kind )
			{
				case ShaderResourceKind.ConstantBuffer when value is byte[] data:
					{
						var size = data.Length;
						foreach ( var layout in stage.Program.ConstantBuffers )
						{
							if ( layout.Name == name ) size = Math.Max( size, layout.Size );
						}

						size = (size + 15) & ~15;
						var cache = device.MaterialCache;
						var offset = ObjectLayer.Of( from, cache ).Constants( cache, token.Value, size, data, dynamic, from.Version );
						if ( dynamic )
						{
							binding = new Binding { Slot = resource.Binding, Type = BindingType.UniformBufferDynamic, Buffer = cache.PassBuffer, Size = size };
							dynamicOffset = (uint)offset;
						}
						else
						{
							binding = Binding.Uniform( resource.Binding, cache.MaterialBuffer, offset, size );
						}

						return true;
					}

				case ShaderResourceKind.Buffer when value is GpuBuffer { Resource: BackendBuffer buffer }:
					binding = Binding.Storage( resource.Binding, buffer.Handle );
					return true;

				case ShaderResourceKind.Texture when value is BackendTexture texture:
					binding = TextureBinding( device.Gpu, device.Defaults, texture, context.SampledLayoutOf( texture ), resource );
					return true;

				default:
					return false;
			}
		}

		public override Binding Resolve( in ShaderResource resource, ShaderStage stage, bool dynamic, out uint dynamicOffset )
		{
			dynamicOffset = 0;
			var device = context.device;
			switch ( resource.Kind )
			{
				case ShaderResourceKind.ConstantBuffer:
					{
						var data = context.attributes.Get( Token( resource.Name ) ) as byte[];
						var size = data?.Length ?? 0;
						foreach ( var layout in stage.Program.ConstantBuffers )
						{
							if ( layout.Name == resource.Name ) size = Math.Max( size, layout.Size );
						}

						if ( size == 0 )
						{
							var placeholder = device.Defaults.For( resource );
							if ( dynamic ) placeholder.Type = BindingType.UniformBufferDynamic;
							return placeholder;
						}

						size = (size + 15) & ~15;
						if ( !dynamic )
						{
							// Past the device's dynamic limit: kept by content, so the set pointing at it stays valid
							Span<byte> padded = stackalloc byte[size];
							padded.Clear();
							data?.CopyTo( padded );
							return Binding.Uniform( resource.Binding, device.MaterialCache.MaterialBuffer, device.MaterialCache.KeepPassConstants( padded, size ), size );
						}

						if ( !constants.TryGetValue( (resource.Name, size), out var found ) )
						{
							var offset = device.MaterialCache.AllocatePassConstants( size, out var target );
							target.Clear();
							data?.CopyTo( target );
							found = (new Binding { Slot = resource.Binding, Type = BindingType.UniformBufferDynamic, Buffer = device.MaterialCache.PassBuffer, Size = size }, (uint)offset);
							constants[(resource.Name, size)] = found;
						}

						dynamicOffset = found.Offset;
						return found.Binding with { Slot = resource.Binding };
					}

				default:
					{
						var key = (resource.Name, resource.Kind, resource.Dimension, resource.Variable?.RenderStateId ?? 0);
						if ( !resources.TryGetValue( key, out var found ) )
						{
							found = resource.Kind == ShaderResourceKind.Buffer
								? context.attributes.Get( Token( resource.Name ) ) is GpuBuffer { Resource: BackendBuffer buffer } ? Binding.Storage( resource.Binding, buffer.Handle ) : device.Defaults.For( resource )
								: context.AttributeTexture( resource );
							resources[key] = found;
						}

						return found with { Slot = resource.Binding };
					}
			}
		}
	}

	PassResolver passResolver;
	long passEpoch;

	// What the previous draw drew with, so the timings can tell a bind from a draw of the same material again
	GpuMaterial timedMaterial;
	uint timedMode;
	long timedLayer;

	public override bool DrawModel( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false, int textureSize = TextureSizes.All )
		=> Draw( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, default );

	/// <summary>A model draw, direct or indirect, through material objects or by reflection; timed when the device times draws.</summary>
	bool Draw( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, bool skinned, in IndirectDraws indirect )
	{
		if ( !device.TimeDraws )
		{
			return device.MaterialObjects
				? DrawWithMaterialObject( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, indirect )
				: DrawByReflection( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, indirect );
		}

		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		var drawn = device.MaterialObjects
			? DrawWithMaterialObject( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, indirect )
			: DrawByReflection( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, indirect );
		var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;

		// A bind: a different material or mode from the draw before, as Source 2's context would rebind. Of the rest, an
		// object change: the same material with other object attributes (Source 2 rebinds then too)
		var bind = !ReferenceEquals( material, timedMaterial ) || shaderMode.Value != timedMode;
		var objectChange = !bind && layerEpoch != timedLayer;
		(timedMaterial, timedMode, timedLayer) = (material, shaderMode.Value, layerEpoch);
		device.DrawTimes.Add( ticks, bind, drawn, objectChange );
		if ( !drawn ) device.DrawTimes.Fail( material?.Name );
		else if ( indirect.Count > 0 ) device.DrawTimes.AddIndirect( indirect.Count );
		return drawn;
	}

	// The last draw's lookups, which the next draw of the same material the same way reuses
	BackendMaterial lastMaterial;
	uint lastMode;
	long lastEpoch = -1;
	string lastLayerCombos = "";
	ShaderVariant lastVariant;
	MaterialObjectCache.Key lastKey;
	MaterialObject lastObject;
	readonly Dictionary<(ShaderFile Shader, uint Mode), string> modeNames = new();
	Func<string, int?> comboLookup, specializationLookup;
	readonly Dictionary<ShaderFile, (long Epoch, string Layer, string Key)> comboKeys = new( ReferenceEqualityComparer.Instance );

	/// <summary>An attribute's texture for a pass binding, readable where this context records, else the default.</summary>
	Binding AttributeTexture( in ShaderResource resource )
	{
		var texture = attributes.Get( Token( resource.Name ) ) as BackendTexture;
		return TextureBinding( device.Gpu, device.Defaults, texture, texture is null ? ImageLayout.Undefined : SampledLayoutOf( texture ), resource );
	}

	/// <summary>
	/// A texture binding with the checks <see cref="Texture"/> makes: ready to sample (<paramref name="layout"/> is where
	/// the recording has it) and the shape the shader expects, else the default.
	/// </summary>
	internal static Binding TextureBinding( Device gpu, ShaderDefaults defaults, BackendTexture texture, ImageLayout layout, in ShaderResource resource )
	{
		var expected = resource.Dimension switch
		{
			SpirvImageDimension.Cube => TextureDimension.TextureCube,
			SpirvImageDimension.CubeArray => TextureDimension.TextureCubeArray,
			SpirvImageDimension.Texture3D => TextureDimension.Texture3D,
			SpirvImageDimension.Texture2DArray => TextureDimension.Texture2DArray,
			_ => TextureDimension.Texture2D,
		};

		if ( texture is null || layout != ImageLayout.ShaderRead || texture.Dimension != expected || texture.Samples > 1 ) return defaults.For( resource );

		var srgb = resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture.Handle ).Format.HasSrgb();
		return Binding.Sampled( resource.Binding, texture.Handle, srgb );
	}

	bool DrawWithMaterialObject( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, bool skinned, in IndirectDraws indirect = default )
	{
		if ( material is not BackendMaterial { Shader: { } shader } m || mesh.Geometry is not IBackendGeometry source ) return false;
		if ( (indirect.InstanceIds ?? InstanceIds?.Resource as BackendBuffer) is not { } instanceIds ) return false;
		if ( !EnsureScope() ) return false;

		var r = recording;
		var cache = device.MaterialCache;
		var resolver = Resolver( cache );

		// A new pass state whenever the attributes changed or a scope opened (a texture may have changed layout)
		NextPassState( cache );

		resolver.Begin( passEpoch );

		ShaderVariant variant;
		if ( ReferenceEquals( m, lastMaterial ) && shaderMode.Value == lastMode && passEpoch == lastEpoch && ReferenceEquals( layerCombos, lastLayerCombos ) )
		{
			variant = lastVariant;
		}
		else
		{
			var mode = CachedModeName( shader, shaderMode );
			if ( mode is null ) return false;
			comboLookup ??= name => LookupCombo( Token( name ) );
			specializationLookup ??= name => Integer( Lookup( Token( name ) ) );
			variant = m.Variant( device.Shaders, mode, ComboKey( m, mode ), comboLookup, specializationLookup );
			(lastMaterial, lastMode, lastEpoch, lastLayerCombos, lastVariant) = (m, shaderMode.Value, passEpoch, layerCombos, variant);
		}

		if ( variant?.Vertex?.Bytecode is null ) return false;

		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var noColor = stripPixelShader || r.Color is null;
		var depthOnly = StripsPixelShader( m, noColor, stripPixelShader );
		var geometry = GeometryFor( source, draw );
		var key = new MaterialObjectCache.Key( m, variant, geometry.Layout, noColor ? Format.Unknown : color, r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth ), (r.Color ?? r.Depth).Samples, depthOnly, geometry.UncompressedTangentFrame );

		var materialObject = lastObject is not null && key.Same( lastKey ) && !lastObject.Stale ? lastObject : cache.Get( key, immediate: false, (device, geometry.Layout, key), static ( s, layout ) => s.device.Pipeline( s.key.Variant, s.Layout, s.key.Color, s.key.Depth, s.key.Samples, s.key.DepthOnly, layout.Sets ) );
		(lastKey, lastObject) = (key, materialObject);

		resolver.LayerEpoch = layerEpoch;
		materialObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
		r.Binder = null;

		if ( noColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
		if ( variant.State.StencilTest && r.Depth is not null && BackendDevice.FormatOf( r.Depth ).HasStencil() ) r.List.SetStencilReference( (byte)variant.State.StencilReference );

		RecordDraw( r.List, geometry, instanceIds, draw, firstInstance, instances, indirect );
		return true;
	}

	/// <summary>This context's pass resolver, with its slot in the cache's objects.</summary>
	PassResolver Resolver( MaterialObjectCache cache )
	{
		if ( passResolver is not null ) return passResolver;
		return passResolver = new PassResolver( this ) { Slot = cache.NewSlot() };
	}

	/// <summary>A new pass state when the attributes changed since the last one, or a scope opened.</summary>
	void NextPassState( MaterialObjectCache cache )
	{
		BindViewSet( recording.List );
		var version = attributes.Version;
		if ( recording.BindVersion == version ) return;
		recording.BindVersion = version;
		passEpoch = cache.NextEpoch();
	}

	/// <summary>
	/// Run a compute shader through its material object: the variant's sets, persistent, with constants written once
	/// per value, as <see cref="DrawWithMaterialObject"/> binds draws.
	/// </summary>
	void DispatchWithMaterialObject( BackendMaterial material, ShaderVariant variant, int x, int y, int z )
	{
		var r = recording;
		var cache = device.MaterialCache;
		var resolver = Resolver( cache );
		NextPassState( cache );
		resolver.Begin( passEpoch );

		var key = new MaterialObjectCache.Key( material, variant, null, Format.Unknown, Format.Unknown, 0, false, false );
		var materialObject = cache.Get( key, immediate: false, (device, variant), static ( s, layout ) => s.device.ComputePipeline( s.variant, layout.Sets ) );
		var bound = default( PipelineHandle );
		resolver.LayerEpoch = 0;
		materialObject.Bind( r.List, ref bound, passEpoch, resolver );
		r.Pipeline = default;
		r.Binder = null;

		var (gx, gy, gz) = variant.Compute.Bytecode.ThreadGroupSize;
		r.List.Dispatch( (x + Math.Max( gx, 1 ) - 1) / Math.Max( gx, 1 ), (y + Math.Max( gy, 1 ) - 1) / Math.Max( gy, 1 ), (z + Math.Max( gz, 1 ) - 1) / Math.Max( gz, 1 ) );
	}

	string CachedModeName( ShaderFile shader, StringToken mode )
	{
		if ( modeNames.TryGetValue( (shader, mode.Value), out var name ) ) return name;
		return modeNames[(shader, mode.Value)] = ModeName( shader, mode );
	}

	/// <summary>The variant key <see cref="Variant"/> builds, made once per shader, pass state and object combos.</summary>
	string ComboKey( BackendMaterial material, string mode )
	{
		if ( comboKeys.TryGetValue( material.Shader, out var cached ) && cached.Epoch == passEpoch && ReferenceEquals( cached.Layer, layerCombos ) && cached.Key.StartsWith( mode, StringComparison.Ordinal ) && (cached.Key.Length == mode.Length || cached.Key[mode.Length] == ';') ) return cached.Key;

		var key = new System.Text.StringBuilder( mode );
		foreach ( var type in (ReadOnlySpan<ShaderProgramType>)[ShaderProgramType.Vertex, ShaderProgramType.Pixel] )
		{
			foreach ( var combo in material.Shader[type]?.DynamicCombos ?? [] )
			{
				if ( LookupCombo( Token( combo.Name ) ) is int value && value != 0 ) key.Append( ';' ).Append( combo.Name ).Append( '=' ).Append( value );
			}
		}

		AppendSpecialization( key, material );
		var made = key.ToString();
		comboKeys[material.Shader] = (passEpoch, layerCombos, made);
		return made;
	}
}
