using NativeEngine;
using System.Runtime.CompilerServices;

namespace Basalt.Gpu.Source2;

/// <summary>
/// A Source 2 material: the engine's <see cref="Material"/>, wrapped once so identity holds. Flags are read live, so a
/// reloaded material's new values show.
/// </summary>
internal sealed class Source2Material : GpuMaterial
{
	public Material Material { get; }

	Source2Material( Material material )
	{
		Material = material;
	}

	static readonly ConditionalWeakTable<Material, Source2Material> wrapped = new();

	/// <summary>
	/// The engine material as Basalt's, or null.
	/// </summary>
	public static GpuMaterial From( Material material ) => material is null ? null : wrapped.GetValue( material, static m => new Source2Material( m ) );

	public override string Name => Material.ResourcePath ?? Material.Name;
	public override bool IsTranslucent => Material.native.GetIntAttributeOrDefault( "translucent", 0 ) != 0;
	public override bool IsAlphaTest => Material.native.GetIntAttributeOrDefault( "alphatest", 0 ) != 0;
	public override bool ReadsFrameBuffer => Material.native.GetIntAttributeOrDefault( "bWantsFBCopyTexture", 0 ) != 0;
	public override bool ShadowFastPath => Material.native.GetBoolAttributeOrDefault( "ShadowFastPath", false );
	public override bool NeedsLightProbe => Material.Flags.GetBool( "NeedsLightProbe", true ) && Material.Flags.GetBool( "UsesBakedLightingFromProbe", true );
	public override ulong SimilarityKey => RenderTools.GetMaterialSimilarityKey( Material.native );
	public override void Dispose() => Material.Destroy();
}

/// <summary>
/// Basalt's materials as the engine's, while the rest of Basalt still records through engine APIs.
/// </summary>
internal static class Source2MaterialExtensions
{
	/// <summary>
	/// The engine material behind a Basalt one, or null.
	/// </summary>
	public static Material Engine( this GpuMaterial material ) => (material as Source2Material)?.Material;

	/// <summary>
	/// The native material behind a Basalt one, or an invalid handle.
	/// </summary>
	public static IMaterial Native( this GpuMaterial material ) => material is Source2Material m ? m.Material.native : default;
}
