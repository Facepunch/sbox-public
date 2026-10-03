using NativeEngine;

namespace Sandbox;

public partial class RenderAttributes
{
	// Keep a list of textures we've used, CRenderAttributes doesn't use strong handles.
	// This is mainly here to keep textures alive when stored in long lived attributes like SceneObject.Attributes
	// Otherwise the GC may collect them while still in use by native code.
	// It could be done on CRenderAttributes natively, but we tried that before and fucked it, it's complicated.

	private Dictionary<StringToken, ITexture> usedTextures; // lazy allocated

	private void SetUsedTexture( StringToken k, Texture texture )
	{
		if ( usedTextures is null )
		{
			usedTextures = [];

			// We're holding strong texture handles now, so the finalizer has real cleanup to do.
			if ( finalizeSuppressed )
			{
				GC.ReRegisterForFinalize( this );
				finalizeSuppressed = false;
			}
		}

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
