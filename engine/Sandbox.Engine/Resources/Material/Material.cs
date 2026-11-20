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

		// Borealis.Gpu lowers attributes itself: it draws with a managed copy (a post process blits with its material's
		// attributes: Unicycle Frenzy's speed lines)
		Attributes.KeepManagedCopy();
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
			copy.FilePath = FilePath;
			copy.MadeFromShader = MadeFromShader;
			copy.ShaderSetInCode = ShaderSetInCode;
			copy.SetInCode = SetInCode;
			copy.SetInCodeVersion = SetInCodeVersion;
			// The native copy merges raw buffer pointers, so the copy needs its own bindings
			Attributes?.MergeUsedBuffersTo( copy.Attributes );
		}

		return copy;
	}

	/// <summary>
	/// Made by <see cref="CreateCopy"/>: nothing shares it unless handed on, so what made it can free it (<see cref="ReleaseCopy"/>).
	/// </summary>
	internal bool IsCopy { get; private set; }

	/// <summary>The compiled file this material reads, a copy's being its original's.</summary>
	internal string FilePath
	{
		get => filePath ?? ResourcePath;
		private set => filePath = value;
	}

	string filePath;

	/// <summary>
	/// The shader a material made in code has (<see cref="Create(string, string, bool)"/>), with no file of its own; null for one
	/// read from a file.
	/// </summary>
	internal string MadeFromShader { get; private set; }

	bool releaseQueued;

	/// <summary>
	/// Free a copy made by <see cref="CreateCopy"/> now, rather than when the garbage collector finalizes it. It goes at the
	/// end of this frame, after everything has rendered, so draws this frame may still use it; nothing should draw with it
	/// after that (delete its scene object, or give it another material). Does nothing for a material that isn't a copy, such
	/// as one from <see cref="Load(string)"/>, which others share.
	/// </summary>
	public void ReleaseCopy()
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
