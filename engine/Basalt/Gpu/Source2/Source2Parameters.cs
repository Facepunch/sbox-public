using System.Runtime.CompilerServices;

namespace Basalt.Gpu.Source2;

/// <summary>
/// Source 2 shader parameters: the engine's <see cref="RenderAttributes"/>, wrapped once so identity holds.
/// </summary>
internal sealed class Source2Parameters : ShaderParameters
{
	public RenderAttributes Attributes { get; }

	Source2Parameters( RenderAttributes attributes )
	{
		Attributes = attributes;
	}

	static readonly ConditionalWeakTable<RenderAttributes, Source2Parameters> wrapped = new();

	/// <summary>
	/// The engine's attributes as Basalt's parameters, or null.
	/// </summary>
	public static ShaderParameters From( RenderAttributes attributes ) => attributes is null ? null : wrapped.GetValue( attributes, static a => new Source2Parameters( a ) );

	public override void Set( StringToken name, int value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, uint value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, float value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, bool value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, Vector2 value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, Vector3 value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, Vector4 value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, Vector2Int value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, Matrix value ) => Attributes.Set( name, value );
	public override void Set( StringToken name, GpuTexture value, int mip = -1 ) => Attributes.Set( name, value.Engine(), mip );
	public override void Set( StringToken name, GpuBuffer value ) => Attributes.Set( name, value.Engine() );
	public override void SetCombo( StringToken name, int value ) => Attributes.SetCombo( name, value );
	public override bool GetBool( StringToken name, bool fallback = false ) => Attributes.GetBool( name, fallback );
	public override void MergeTo( ShaderParameters target ) => Attributes.MergeTo( target.Engine() );
	public override void Clear() => Attributes.Clear();
}

/// <summary>
/// Basalt's parameters as the engine's attributes, while the rest of Basalt still records through engine APIs.
/// </summary>
internal static class Source2ParameterExtensions
{
	/// <summary>
	/// The engine attributes behind Basalt's parameters, or null.
	/// </summary>
	public static RenderAttributes Engine( this ShaderParameters parameters ) => (parameters as Source2Parameters)?.Attributes;
}
