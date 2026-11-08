namespace Basalt;

/// <summary>
/// The graphics device Basalt renders with: it makes and loads resources, and records and submits work. A host supplies
/// one through <see cref="RenderHost.Device"/>.
/// </summary>
public abstract class GpuDevice
{
	/// <summary>
	/// Create a texture.
	/// </summary>
	public abstract GpuTexture CreateTexture( in TextureDesc desc, string name );

	/// <summary>
	/// Load a texture asset by path, or null if it doesn't exist.
	/// </summary>
	public abstract GpuTexture LoadTexture( string path );

	/// <summary>
	/// A 1x1 black texture.
	/// </summary>
	public abstract GpuTexture Black { get; }

	/// <summary>
	/// Fill a texture with a colour now, outside any recorded work.
	/// </summary>
	public abstract void Clear( GpuTexture texture, Color color );

	/// <summary>
	/// Create the device's side of a <see cref="GpuBuffer"/>.
	/// </summary>
	public abstract IDisposable CreateBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name );

	/// <summary>
	/// Copy bytes into a buffer now, outside any recorded work.
	/// </summary>
	public abstract void SetData( GpuBuffer buffer, ReadOnlySpan<byte> data );

	/// <summary>
	/// An empty set of shader parameters.
	/// </summary>
	public abstract ShaderParameters CreateParameters();

	/// <summary>
	/// The sprite sheets' metadata, which shaders that sample sheets read.
	/// </summary>
	public abstract GpuTexture SheetMetadata { get; }
}
