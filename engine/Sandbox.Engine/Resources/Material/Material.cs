using NativeEngine;

namespace Sandbox;

/// <summary>
/// A material. Uses several <see cref="Texture"/>s and a <see cref="Shader"/> with specific settings for more interesting visual effects.
/// </summary>
public sealed partial class Material : Resource
{
	internal IMaterial native;

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Name (or path) of the material.
	/// </summary>
	public string Name { get; internal set; }

	/// <summary>
	/// Access to all of the attributes of this material.
	/// </summary>
	public RenderAttributes Attributes { get; internal set; }

	/// <summary>
	/// Private constructor, use <see cref="FromNative(IMaterial, string)"/>
	/// </summary>
	/// <param name="native"></param>
	/// <param name="name"></param>
	/// <exception cref="System.Exception"></exception>
	private Material( IMaterial native, string name )
	{
		if ( native.IsNull ) throw new System.Exception( "Material pointer cannot be null!" );

		this.native = native;
		this.Name = name;

		RegisterWeakResourceId( name, native.GetGuid() );

		CRenderAttributes attributes = this.native.GetRenderAttributes();
		Attributes = new RenderAttributes( attributes );
	}

	internal override void Destroy()
	{
		// kill the native pointer - it does with the native material
		// we want to reduce the risk that someone is holding on to it.
		Attributes?.Set( default );
		Attributes = null;

		if ( !native.IsNull )
		{
			var n = native;
			native = default;

			// Evict from NativeResourceCache, as Texture does, so a material native makes later at the same address gets a
			// wrapper of its own rather than this dead one
			NativeResourceCache.Remove( n.GetBindingPtr().ToInt64() );

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}

		base.Destroy();
	}

	/// <summary>
	/// Create a copy of this material
	/// </summary>
	public Material CreateCopy( string name = null )
	{
		var copy = FromNative( MaterialSystem2.CreateProceduralMaterialCopy( native, 0, true ), name );
		if ( copy is not null )
		{
			copy.anonymousShader = anonymousShader;
			copy.IsCopy = true;
		}

		return copy;
	}

	/// <summary>
	/// Made by <see cref="CreateCopy"/>: nothing shares it unless handed on, so what made it can free it (<see cref="ReleaseCopy"/>).
	/// </summary>
	internal bool IsCopy { get; private set; }

	bool releaseQueued;

	/// <summary>
	/// Free a copy made by <see cref="CreateCopy"/> at the end of this frame, once everything has rendered, rather than whenever
	/// the GC finalizes it: its strong handle goes then, and with native's last reference its material and layers. What drew
	/// it must have let go of it by then (its scene object deleted, or given another material). Does nothing for a material
	/// that isn't a copy, which others share.
	/// </summary>
	internal void ReleaseCopy()
	{
		if ( !IsCopy || releaseQueued || native.IsNull ) return;

		releaseQueued = true;

		// Frame end disposes it
#pragma warning disable CA2000 // Dispose objects before losing scope
		EngineLoop.DisposeAtFrameEnd( new CopyRelease( this ) );
#pragma warning restore CA2000
	}

	sealed class CopyRelease( Material material ) : IDisposable
	{
		public void Dispose() => material.Destroy();
	}
}
