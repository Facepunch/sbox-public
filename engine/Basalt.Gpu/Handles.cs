namespace Basalt.Gpu;

/// <summary>
/// A buffer the <see cref="Device"/> owns. Default is no buffer.
/// </summary>
public readonly record struct BufferHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a buffer (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// A texture the <see cref="Device"/> owns. Default is no texture.
/// </summary>
public readonly record struct TextureHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a texture (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// An extra view of a texture (<see cref="Device.CreateView"/>). Default is no view.
/// </summary>
public readonly record struct TextureViewHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a view (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// A sampler the <see cref="Device"/> owns. Default is no sampler.
/// </summary>
public readonly record struct SamplerHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a sampler (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// A shader module (<see cref="Device.CreateShader"/>). Default is none.
/// </summary>
public readonly record struct ShaderHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a shader (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// The layout of one descriptor set (<see cref="Device.CreateBindingLayout"/>). Default is an empty set.
/// </summary>
public readonly record struct BindingLayoutHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a layout (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// A graphics or compute pipeline. Default is none.
/// </summary>
public readonly record struct PipelineHandle( uint Index, uint Generation )
{
	/// <summary>Whether this names a pipeline (it may since have been destroyed).</summary>
	public bool IsValid => Generation != 0;
}

/// <summary>
/// A point on a queue's timeline: the work submitted up to and including one submission. Default is "nothing", which is
/// always complete.
/// </summary>
public readonly record struct SyncPoint( QueueType Queue, ulong Value )
{
	/// <summary>Whether this names a submission.</summary>
	public bool IsValid => Value != 0;
}
