using Borealis.Gpu;
using System.Buffers.Binary;
using System.Numerics;

namespace Borealis.Source2.Shaders;

/// <summary>
/// What to bind where nothing better is known: black textures of every dimension, zeroed buffers, a default vertex
/// stream reading <c>(0, 0, 0, 1)</c> as native's input layouts do for streams a mesh doesn't have, and samplers made
/// from shaders' sampler states. Owned by a device; thread-safe.
/// </summary>
public sealed class ShaderDefaults : IDisposable
{
	readonly Device gpu;
	readonly Dictionary<SpirvImageDimension, TextureHandle> textures = new();
	readonly Dictionary<SpirvImageDimension, TextureHandle> storageTextures = new();
	readonly Dictionary<SamplerInfo, SamplerHandle> samplers = new();

	/// <summary>Bytes of zeros the zeroed storage buffer holds.</summary>
	public const int BufferSize = 64 * 1024;

	/// <summary>Bytes of zeros the zeroed uniform buffer holds: the smallest uniform range every device allows.</summary>
	public const int UniformSize = 16 * 1024;

	/// <summary>A zeroed uniform buffer.</summary>
	public BufferHandle Uniform { get; }

	/// <summary>A zeroed storage buffer.</summary>
	public BufferHandle Storage { get; }

	/// <summary>A 16-byte vertex stream, <c>(0, 0, 0, 1)</c> as floats, read with a stride of 0.</summary>
	public BufferHandle VertexDefaults { get; }

	/// <summary>Defaults on <paramref name="gpu"/>.</summary>
	public ShaderDefaults( Device gpu )
	{
		this.gpu = gpu;
		Uniform = gpu.CreateBuffer( new BufferInfo { Size = UniformSize, Usage = BufferUsage.Uniform, Name = "Default constants" } );
		Storage = gpu.CreateBuffer( new BufferInfo { Size = BufferSize, Usage = BufferUsage.Storage, Name = "Default storage" } );
		VertexDefaults = gpu.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Vertex, Name = "Default vertex stream" } );

		var zeros = new byte[BufferSize];
		gpu.WriteBuffer( Uniform, zeros.AsSpan( 0, UniformSize ) );
		gpu.WriteBuffer( Storage, zeros );
		var defaults = new byte[16];
		BinaryPrimitives.WriteSingleLittleEndian( defaults.AsSpan( 12 ), 1 );
		gpu.WriteBuffer( VertexDefaults, defaults );
	}

	/// <summary>A black (zero) texture of a dimension, made on first use.</summary>
	public TextureHandle Texture( SpirvImageDimension dimension )
	{
		dimension = dimension switch
		{
			SpirvImageDimension.Texture1D or SpirvImageDimension.Rect or SpirvImageDimension.None => SpirvImageDimension.Texture2D,
			_ => dimension,
		};

		lock ( textures )
		{
			if ( textures.TryGetValue( dimension, out var texture ) ) return texture;

			var info = TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: $"Default {dimension}" );
			info.Usage |= TextureUsage.Storage;
			if ( !gpu.Supports( Format.RGBA8Unorm, info.Usage ) ) info.Usage &= ~TextureUsage.Storage;
			switch ( dimension )
			{
				case SpirvImageDimension.Texture2DArray:
					info.Dimension = TextureDimension.Texture2DArray;
					break;
				case SpirvImageDimension.Cube:
					info.Dimension = TextureDimension.TextureCube;
					break;
				case SpirvImageDimension.CubeArray:
					info.Dimension = TextureDimension.TextureCubeArray;
					break;
				case SpirvImageDimension.Texture3D:
					info.Dimension = TextureDimension.Texture3D;
					break;
			}

			texture = gpu.CreateTexture( info );
			var layers = info.Dimension is TextureDimension.TextureCube or TextureDimension.TextureCubeArray ? 6 : 1;
			for ( int layer = 0; layer < layers; layer++ ) gpu.WriteTexture( texture, new byte[4], 0, layer );
			textures[dimension] = texture;
			return texture;
		}
	}

	/// <summary>
	/// A 1x1 texture of a dimension for a storage binding nothing fills, made on first use and kept in
	/// <see cref="ImageLayout.General"/>. Apart from <see cref="Texture"/>'s: a shader writing through it mustn't change the
	/// black that sampled bindings read, and those are kept readable rather than general.
	/// </summary>
	public TextureHandle StorageTexture( SpirvImageDimension dimension )
	{
		dimension = dimension switch
		{
			SpirvImageDimension.Texture1D or SpirvImageDimension.Rect or SpirvImageDimension.None => SpirvImageDimension.Texture2D,
			_ => dimension,
		};

		lock ( storageTextures )
		{
			if ( storageTextures.TryGetValue( dimension, out var texture ) ) return texture;

			var info = TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: $"Default storage {dimension}" );
			info.Usage |= TextureUsage.Storage;
			info.Dimension = dimension switch
			{
				SpirvImageDimension.Texture2DArray => TextureDimension.Texture2DArray,
				SpirvImageDimension.Cube => TextureDimension.TextureCube,
				SpirvImageDimension.CubeArray => TextureDimension.TextureCubeArray,
				SpirvImageDimension.Texture3D => TextureDimension.Texture3D,
				_ => TextureDimension.Texture2D,
			};

			texture = gpu.CreateTexture( info );
			var list = gpu.BeginCommandList( QueueType.Graphics, "Default storage texture" );
			list.Barrier( TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			list.ClearTexture( texture );
			list.Barrier( TextureBarrier.Transition( texture, ImageLayout.CopyDestination, ImageLayout.General, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite ) );
			gpu.Wait( gpu.Submit( list ) );
			storageTextures[dimension] = texture;
			return texture;
		}
	}

	/// <summary>A sampler, made once per distinct description.</summary>
	public SamplerHandle Sampler( in SamplerInfo info )
	{
		lock ( samplers )
		{
			if ( samplers.TryGetValue( info, out var sampler ) ) return sampler;
			sampler = gpu.CreateSampler( info );
			samplers[info] = sampler;
			return sampler;
		}
	}

	/// <summary>
	/// A placeholder binding for a resource nothing else fills: zeros, black, or its sampler.
	/// </summary>
	public Binding For( in ShaderResource resource ) => resource.Type switch
	{
		BindingType.UniformBuffer => Binding.Uniform( resource.Binding, Uniform, 0, UniformSize ),
		BindingType.StorageBuffer => Binding.Storage( resource.Binding, Storage ),
		BindingType.Sampler => Binding.SamplerState( resource.Binding, Sampler( resource.Sampler ) ),
		BindingType.StorageTexture => Binding.StorageTexture( resource.Binding, StorageTexture( resource.Dimension ) ),
		_ => Binding.Sampled( resource.Binding, Texture( resource.Dimension ) ),
	};

	/// <summary>
	/// Vertex attributes for a variant's inputs: those <paramref name="streams"/> supplies, and the rest from
	/// <see cref="VertexDefaults"/> at buffer <paramref name="defaultBuffer"/> (stride 0), as native does for streams
	/// a mesh lacks.
	/// </summary>
	public static VertexAttribute[] MapVertices( IReadOnlyList<VertexInput> inputs, Func<VertexInput, VertexAttribute?> streams, int defaultBuffer )
	{
		var attributes = new VertexAttribute[inputs.Count];
		for ( int i = 0; i < inputs.Count; i++ )
		{
			var input = inputs[i];
			attributes[i] = streams( input ) is { } attribute
				? attribute with { Location = input.Location }
				: new VertexAttribute( input.Location, defaultBuffer, input.Kind == SpirvScalar.Float ? VertexFormat.Float4 : VertexFormat.UInt4, 0 );
		}

		return attributes;
	}

	/// <summary>
	/// Write <paramref name="value"/> into a constant buffer laid out as <paramref name="layout"/> says, at the member
	/// named <paramref name="member"/>; false if there's no such member.
	/// </summary>
	public static bool Write( ConstantBufferLayout layout, Span<byte> buffer, string member, ReadOnlySpan<float> value )
	{
		foreach ( var m in layout.Members )
		{
			if ( !string.Equals( m.Name, member, StringComparison.Ordinal ) ) continue;
			for ( int i = 0; i < value.Length && m.Offset + i * 4 + 4 <= buffer.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( buffer[(m.Offset + i * 4)..], value[i] );
			return true;
		}

		return false;
	}

	/// <summary>A matrix as the 16 floats a constant buffer member takes (row-major, as Borealis's mirrors write them).</summary>
	public static float[] Floats( in Matrix4x4 m ) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

	/// <summary>Destroy what was made.</summary>
	public void Dispose()
	{
		lock ( textures )
		{
			foreach ( var texture in textures.Values ) gpu.Destroy( texture );
			textures.Clear();
		}

		lock ( storageTextures )
		{
			foreach ( var texture in storageTextures.Values ) gpu.Destroy( texture );
			storageTextures.Clear();
		}

		lock ( samplers )
		{
			foreach ( var sampler in samplers.Values ) gpu.Destroy( sampler );
			samplers.Clear();
		}

		gpu.Destroy( Uniform );
		gpu.Destroy( Storage );
		gpu.Destroy( VertexDefaults );
	}
}
