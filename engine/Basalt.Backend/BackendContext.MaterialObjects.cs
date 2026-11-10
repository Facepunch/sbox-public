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
	internal sealed class PassResolver( BackendContext context )
	{
		long epoch = -1;
		readonly Dictionary<(string Name, int Size), (Binding Binding, uint Offset)> constants = new();

		public void Begin( long passEpoch )
		{
			if ( passEpoch == epoch ) return;
			epoch = passEpoch;
			constants.Clear();
		}

		public float[] Floats( string attribute ) => BackendContext.Floats( context.attributes.Get( Token( attribute ) ) );

		public Binding Resolve( in ShaderResource resource, ShaderStage stage, bool dynamic, out uint dynamicOffset )
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

				case ShaderResourceKind.Buffer:
					return context.attributes.Get( Token( resource.Name ) ) is GpuBuffer { Resource: BackendBuffer buffer }
						? Binding.Storage( resource.Binding, buffer.Handle )
						: device.Defaults.For( resource );

				default:
					return TextureBinding( device, context.attributes.Get( Token( resource.Name ) ) as BackendTexture, resource );
			}
		}
	}

	PassResolver passResolver;
	long passEpoch;

	// The last draw's lookups, which the next draw of the same material the same way reuses
	BackendMaterial lastMaterial;
	uint lastMode;
	long lastEpoch = -1;
	ShaderVariant lastVariant;
	MaterialObjectCache.Key lastKey;
	MaterialObject lastObject;
	readonly Dictionary<(ShaderFile Shader, uint Mode), string> modeNames = new();
	readonly Dictionary<ShaderFile, (long Epoch, string Key)> comboKeys = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// A texture binding with the checks <see cref="Texture"/> makes: ready to sample and the shape the shader expects,
	/// else the default.
	/// </summary>
	internal static Binding TextureBinding( BackendDevice device, BackendTexture texture, in ShaderResource resource )
	{
		var expected = resource.Dimension switch
		{
			SpirvImageDimension.Cube => TextureDimension.TextureCube,
			SpirvImageDimension.CubeArray => TextureDimension.TextureCubeArray,
			SpirvImageDimension.Texture3D => TextureDimension.Texture3D,
			SpirvImageDimension.Texture2DArray => TextureDimension.Texture2DArray,
			_ => TextureDimension.Texture2D,
		};

		if ( texture is null || texture.Layout != ImageLayout.ShaderRead || texture.Dimension != expected || texture.Samples > 1 ) return device.Defaults.For( resource );

		var srgb = resource.Variable.RenderStateId == 1 && device.Gpu.InfoOf( texture.Handle ).Format.HasSrgb();
		return Binding.Sampled( resource.Binding, texture.Handle, srgb );
	}

	bool DrawWithMaterialObject( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, bool skinned )
	{
		if ( material is not BackendMaterial { Shader: { } shader } m || mesh.Geometry is not IBackendGeometry source || skinned ) return false;
		if ( InstanceIds?.Resource is not BackendBuffer instanceIds ) return false;
		if ( !EnsureScope() ) return false;

		var r = recording;
		var cache = device.MaterialCache;
		passResolver ??= new PassResolver( this );

		// A new pass state whenever the attributes changed or a scope opened (a texture may have changed layout)
		var version = attributes.Version;
		if ( r.BindVersion != version )
		{
			r.BindVersion = version;
			passEpoch = cache.NextEpoch();
		}

		passResolver.Begin( passEpoch );

		ShaderVariant variant;
		if ( ReferenceEquals( m, lastMaterial ) && shaderMode.Value == lastMode && passEpoch == lastEpoch )
		{
			variant = lastVariant;
		}
		else
		{
			var mode = CachedModeName( shader, shaderMode );
			if ( mode is null ) return false;
			variant = m.Variant( device.Shaders, mode, ComboKey( m, mode ), name => attributes.GetCombo( Token( name ) ), name => Integer( attributes.Get( Token( name ) ) ) );
			(lastMaterial, lastMode, lastEpoch, lastVariant) = (m, shaderMode.Value, passEpoch, variant);
		}

		if ( variant?.Vertex?.Bytecode is null ) return false;

		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var depthOnly = stripPixelShader || r.Color is null;
		var geometry = source.For( draw.Mesh, draw.DrawCall );
		var key = new MaterialObjectCache.Key( m, variant, geometry.Layout, depthOnly ? Format.Unknown : color, r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth ), (r.Color ?? r.Depth).Samples, depthOnly, geometry.UncompressedTangentFrame );

		var materialObject = lastObject is not null && key == lastKey ? lastObject : cache.Get( key );
		(lastKey, lastObject) = (key, materialObject);

		materialObject.Bind( r.List, ref r.Pipeline, passEpoch, passResolver );
		r.Binder = null;

		if ( depthOnly ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
		if ( variant.State.StencilTest && r.Depth is not null && BackendDevice.FormatOf( r.Depth ).HasStencil() ) r.List.SetStencilReference( (byte)variant.State.StencilReference );

		r.List.SetVertexBuffer( 0, geometry.Vertices, geometry.VertexOffset );
		r.List.SetVertexBuffer( 1, instanceIds.Handle, firstInstance * 4L );
		r.List.SetVertexBuffer( 2, device.Defaults.VertexDefaults );
		r.List.SetIndexBuffer( geometry.Indices, geometry.IndexFormat, geometry.IndexOffset );
		r.List.DrawIndexed( draw.IndexCount, instances, draw.StartIndex, geometry.BaseVertex );
		return true;
	}

	string CachedModeName( ShaderFile shader, StringToken mode )
	{
		if ( modeNames.TryGetValue( (shader, mode.Value), out var name ) ) return name;
		return modeNames[(shader, mode.Value)] = ModeName( shader, mode );
	}

	/// <summary>The variant key <see cref="Variant"/> builds, made once per shader and pass state.</summary>
	string ComboKey( BackendMaterial material, string mode )
	{
		if ( comboKeys.TryGetValue( material.Shader, out var cached ) && cached.Epoch == passEpoch && cached.Key.StartsWith( mode, StringComparison.Ordinal ) && (cached.Key.Length == mode.Length || cached.Key[mode.Length] == ';') ) return cached.Key;

		var key = new System.Text.StringBuilder( mode );
		foreach ( var type in (ReadOnlySpan<ShaderProgramType>)[ShaderProgramType.Vertex, ShaderProgramType.Pixel] )
		{
			foreach ( var combo in material.Shader[type]?.DynamicCombos ?? [] )
			{
				if ( attributes.GetCombo( Token( combo.Name ) ) is int value && value != 0 ) key.Append( ';' ).Append( combo.Name ).Append( '=' ).Append( value );
			}
		}

		var made = key.ToString();
		comboKeys[material.Shader] = (passEpoch, made);
		return made;
	}
}
