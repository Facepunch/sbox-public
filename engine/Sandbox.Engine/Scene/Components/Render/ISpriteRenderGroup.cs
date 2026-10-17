namespace Sandbox;

/// <summary>
/// Base interface for components that can be grouped for sprite rendering.
/// Contains the properties needed for render group classification.
/// </summary>
public interface ISpriteRenderGroup
{
	bool Opaque { get; }
	bool Additive { get; }
	bool Shadows { get; }
	bool IsSorted { get; }
}
