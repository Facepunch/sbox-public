namespace Editor.AnimGraph;

/// <summary>
/// Marker types for typed connections between AnimGraph nodes.
/// Each type defines a distinct wire color and connection compatibility.
/// </summary>
public record struct PoseLink;
public record struct FloatLink;
public record struct BoolLink;
public record struct IDLink;
public record struct VectorLink;
public record struct TargetLink;
public record struct BoneMaskLink;
public record struct LayerLink;
