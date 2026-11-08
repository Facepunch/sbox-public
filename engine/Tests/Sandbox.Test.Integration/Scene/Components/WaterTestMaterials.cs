using System.Runtime.CompilerServices;

namespace SceneTests.Components;

/// <summary>
/// A water body's look lives in its material. Tests change it on a copy the body owns, leaving the shared default
/// material alone.
/// </summary>
internal static class WaterTestMaterials
{
	static readonly ConditionalWeakTable<WaterBody, Material> Copies = [];

	/// <summary>
	/// The body's material, copied the first time so changes stay with this body. The copy lives as long as the body.
	/// </summary>
	public static Material MaterialCopy( this WaterBody water )
	{
		if ( !Copies.TryGetValue( water, out var copy ) || water.Material != copy )
		{
			copy = water.ActiveMaterial.CreateCopy();
			water.Material = copy;
			Copies.AddOrUpdate( water, copy );
		}
		return copy;
	}

	/// <summary>
	/// A smooth surface: no detail normals.
	/// </summary>
	public static void ClearDetail( this WaterBody water ) => water.MaterialCopy().Set( "DetailStrength", 0 );

	/// <summary>
	/// No refraction capture: opaque from both sides.
	/// </summary>
	public static void DisableRefraction( this WaterBody water )
	{
		water.MaterialCopy().Set( "Opacity", 1 );
		water.MaterialCopy().Set( "UndersideOpacity", 1 );
	}

	/// <summary>
	/// Turn the material's caustics on or off.
	/// </summary>
	public static void SetCaustics( this WaterBody water, bool enabled ) => water.MaterialCopy().SetFeature( "F_CAUSTICS", enabled ? 1 : 0 );
}
