using NativeEngine;

namespace Sandbox;

public partial class RenderAttributes
{
	// Keep a list of textures we've used, CRenderAttributes doesn't use strong handles.
	// This is mainly here to keep textures alive when stored in long lived attributes like SceneObject.Attributes
	// Otherwise the GC may collect them while still in use by native code.
	// It could be done on CRenderAttributes natively, but we tried that before and fucked it, it's complicated.

	private Dictionary<StringToken, ITexture> usedTextures; // lazy allocated

	// Shared attributes like SceneObject.Attributes get textures set from views rendering in parallel
	private readonly System.Threading.Lock usedTexturesLock = new();

	// CRenderAttributes stores raw buffer pointers, hold a binding so Dispose can't free them while bound
	private Dictionary<StringToken, GpuBuffer> usedBuffers; // lazy allocated

	private RenderBufferHandle_t SetUsedBuffer( StringToken k, GpuBuffer buffer )
	{
		usedBuffers ??= [];

		if ( usedBuffers.TryGetValue( k, out var old ) )
		{
			// Same buffer again keeps the binding we already hold
			if ( buffer is not null && ReferenceEquals( old, buffer ) )
				return buffer.native;

			old?.ReleaseAttributeBinding();
		}

		var native = buffer?.AddAttributeBinding() ?? default;

		// Null entries are kept so merging this into other attributes releases their old binding too
		usedBuffers[k] = native != IntPtr.Zero ? buffer : null;

		return native;
	}

	// MergeToPtr copies the raw buffer pointers, so the target needs its own bindings
	internal void MergeUsedBuffersTo( RenderAttributes target )
	{
		if ( target == this || usedBuffers is null )
			return;

		foreach ( var (k, buffer) in usedBuffers )
		{
			target.usedBuffers ??= [];
			target.usedBuffers.TryGetValue( k, out var old );
			if ( ReferenceEquals( old, buffer ) )
				continue;

			buffer?.RetainAttributeBinding();
			old?.ReleaseAttributeBinding();
			target.usedBuffers[k] = buffer;
		}
	}

	private void ClearUsedBuffers()
	{
		if ( usedBuffers is null )
			return;

		foreach ( var buffer in usedBuffers.Values )
		{
			buffer?.ReleaseAttributeBinding();
		}
		usedBuffers.Clear();
	}

	private void SetUsedTexture( StringToken k, Texture texture )
	{
		usedTextures ??= [];

		var native = texture?.native ?? default;

		// Is this key already holding a texture
		if ( usedTextures.TryGetValue( k, out var old ) )
		{
			// Does the existing data point to the same as the new data?
			// If so, nothing to do!
			if ( native.IsValid && old.GetBindingPtr() == native.GetBindingPtr() )
				return;

			// Otherwise, kill our old handle, we're replacing it
			old.DestroyStrongHandle();
		}

		// If our new handle is null, don't write it, just remove any possible entry
		if ( native.IsNull )
		{
			usedTextures.Remove( k );
			return;
		}

		// Finally COPY a strong handle, otherwise we're at the mercy of Texture.Dispose()
		usedTextures[k] = native.CopyStrongHandle();
	}

	/// <summary>
	/// Clear used textures, destroying any strong handles we've created.
	/// These handles are mainly used for storage RenderAttributes like SceneObject.Attributes
	/// </summary>
	private void ClearUsedTextures()
	{
		ClearUsedBuffers();

		lock ( usedTexturesLock )
		{
			if ( usedTextures is null )
				return;

			// Clean up any strong handles
			foreach ( var tex in usedTextures.Values )
			{
				if ( !tex.IsValid ) continue;
				tex.DestroyStrongHandle();
			}
			usedTextures.Clear();
		}
	}
}
