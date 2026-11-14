namespace Borealis;

/// <summary>
/// Named values and combos shaders read: per frame, per pass, per object. Made by the <see cref="GpuDevice"/>; equal sets
/// are the same object, so identity works as a cache and batching key.
/// </summary>
public abstract class ShaderParameters
{
	/// <summary>Set an integer.</summary>
	public abstract void Set( StringToken name, int value );

	/// <summary>Set an unsigned integer.</summary>
	public abstract void Set( StringToken name, uint value );

	/// <summary>Set a float.</summary>
	public abstract void Set( StringToken name, float value );

	/// <summary>Set a bool.</summary>
	public abstract void Set( StringToken name, bool value );

	/// <summary>Set a 2D vector.</summary>
	public abstract void Set( StringToken name, Vector2 value );

	/// <summary>Set a 3D vector.</summary>
	public abstract void Set( StringToken name, Vector3 value );

	/// <summary>Set a 4D vector.</summary>
	public abstract void Set( StringToken name, Vector4 value );

	/// <summary>Set a 2D integer vector.</summary>
	public abstract void Set( StringToken name, Vector2Int value );

	/// <summary>Set a matrix.</summary>
	public abstract void Set( StringToken name, Matrix value );

	/// <summary>Set a texture, or one of its mips.</summary>
	public abstract void Set( StringToken name, GpuTexture value, int mip = -1 );

	/// <summary>Set a buffer.</summary>
	public abstract void Set( StringToken name, GpuBuffer value );

	/// <summary>Set a shader combo.</summary>
	public abstract void SetCombo( StringToken name, int value );

	/// <summary>Set a struct of shader constants.</summary>
	public abstract void SetData<T>( StringToken name, in T value ) where T : unmanaged;

	/// <summary>Set a shader combo on or off.</summary>
	public void SetCombo( StringToken name, bool value ) => SetCombo( name, value ? 1 : 0 );

	/// <summary>A bool, or <paramref name="fallback"/> if it isn't set.</summary>
	public abstract bool GetBool( StringToken name, bool fallback = false );

	/// <summary>Copy every value into <paramref name="target"/>, overwriting what it has.</summary>
	public abstract void MergeTo( ShaderParameters target );

	/// <summary>Remove every value.</summary>
	public abstract void Clear();
}
