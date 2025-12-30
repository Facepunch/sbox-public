namespace Sandbox;

public partial class Terrain
{
	/// <summary>
	/// Information about terrain materials at a specific position
	/// </summary>
	public struct TerrainMaterialInfo
	{
		/// <summary>
		/// The base (primary) material index at this position
		/// </summary>
		public int BaseTextureId { get; set; }

		/// <summary>
		/// The overlay (secondary) material index at this position
		/// </summary>
		public int OverlayTextureId { get; set; }

		/// <summary>
		/// Blend factor between base and overlay (0-1, where 0 = full base, 1 = full overlay)
		/// </summary>
		public float BlendFactor { get; set; }

		/// <summary>
		/// Whether this position is marked as a hole
		/// </summary>
		public bool IsHole { get; set; }

		/// <summary>
		/// The base terrain material resource (if available)
		/// </summary>
		public TerrainMaterial BaseMaterial { get; set; }

		/// <summary>
		/// The overlay terrain material resource (if available)
		/// </summary>
		public TerrainMaterial OverlayMaterial { get; set; }

		/// <summary>
		/// Gets the dominant material at this position based on blend factor
		/// </summary>
		public TerrainMaterial GetDominantMaterial() => BlendFactor < 0.5f ? BaseMaterial : OverlayMaterial;

		/// <summary>
		/// Gets the dominant material index at this position based on blend factor
		/// </summary>
		public int GetDominantMaterialIndex() => BlendFactor < 0.5f ? BaseTextureId : OverlayTextureId;
	}

	/// <summary>
	/// Gets terrain material information at a world position.
	/// Returns null if the position is outside terrain bounds.
	/// </summary>
	public TerrainMaterialInfo? GetMaterialAtWorldPosition( Vector3 worldPosition )
	{
		if ( Storage is null || Storage.ControlMap is null )
			return null;

		// Convert world position to local terrain space
		var localPosition = WorldTransform.PointToLocal( worldPosition );

		// Convert local position to UV
		var uv = new Vector2(
			localPosition.x / Storage.TerrainSize,
			localPosition.y / Storage.TerrainSize
		);

		// We're outside of the plane
		if ( uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 )
			return null;

		// Convert texture coordinates
		var x = (int)(uv.x * Storage.Resolution);
		var y = (int)(uv.y * Storage.Resolution);

		// Get the packed material
		var index = y * Storage.Resolution + x;
		var packedMaterial = Storage.ControlMap[index];
		var material = new CompactTerrainMaterial( packedMaterial );

		// Create the result
		var result = new TerrainMaterialInfo
		{
			BaseTextureId = material.BaseTextureId,
			OverlayTextureId = material.OverlayTextureId,
			BlendFactor = material.BlendFactor / 255.0f,
			IsHole = material.IsHole
		};

		if ( material.BaseTextureId < Storage.Materials.Count )
			result.BaseMaterial = Storage.Materials[material.BaseTextureId];

		if ( material.OverlayTextureId < Storage.Materials.Count )
			result.OverlayMaterial = Storage.Materials[material.OverlayTextureId];

		return result;
	}
}
