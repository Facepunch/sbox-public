using NativeEngine;

namespace Sandbox;

/// <summary>
/// Represents a ray tracing acceleration structure that contains geometry for efficient ray intersection testing.
/// This is used to organize scene geometry in a hierarchical structure optimized for ray tracing performance.
/// </summary>
public class RayTracingAccelerationStructure
{
	internal object native;

	/// <summary>
	/// Gets whether this acceleration structure is valid and can be used for ray tracing.
	/// </summary>
	public bool IsValid() => native != null;

	/// <summary>
	/// Create a ray tracing acceleration structure from native engine data.
	/// </summary>
	internal RayTracingAccelerationStructure( object nativeAccelerationStructure )
	{
		native = nativeAccelerationStructure;
	}

	/// <summary>
	/// Create a ray tracing acceleration structure from scene geometry.
	/// </summary>
	/// <param name="geometryData">The geometry data to build the acceleration structure from.</param>
	/// <returns>A new acceleration structure, or null if creation failed.</returns>
	public static RayTracingAccelerationStructure Create( object geometryData )
	{
		// This would typically interface with the native engine to build the acceleration structure
		// For now, this is a placeholder implementation
		if ( geometryData == null )
			return null;

		// In a real implementation, this would call into the native engine
		// to build a DXR acceleration structure from the provided geometry
		var nativeAS = geometryData; // Placeholder

		return new RayTracingAccelerationStructure( nativeAS );
	}

	/// <summary>
	/// Updates the acceleration structure with new geometry data.
	/// This is more efficient than rebuilding from scratch for dynamic geometry.
	/// </summary>
	/// <param name="geometryData">The updated geometry data.</param>
	public void Update( object geometryData )
	{
		if ( !IsValid() )
			throw new InvalidOperationException( "Cannot update invalid acceleration structure" );

		// This would call into the native engine to update the acceleration structure
		// with new geometry positions while preserving the hierarchical structure
	}

	/// <summary>
	/// Releases the native resources associated with this acceleration structure.
	/// </summary>
	public void Dispose()
	{
		native = null;
	}
}
