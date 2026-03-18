using NativeEngine;

namespace Sandbox;

public partial class Model
{
	internal static readonly Dictionary<long, WeakReference<Model>> Loaded = new();

	/// <summary>
	/// Cached <see cref="Model"/> instance from native, or creates
	/// </summary>
	internal static Model FromNative( IModel native, bool procedural = false, string name = null )
	{
		if ( native.IsNull || !native.IsStrongHandleValid() )
			return null;

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Model>( instanceId, out var model ) )
		{
			// If we're using a cached one we don't need this handle, we'll leak
			native.DestroyStrongHandle();

			return model;
		}

		model = new Model( native, name ?? native.GetModelName(), procedural );
		NativeResourceCache.Add( instanceId, model );

		// Keeping this because some legacy game loop depends on this for logic, can't be fucked solving for legacy.
		Loaded[instanceId] = new WeakReference<Model>( model );

		return model;
	}

	/// <summary>
	/// Returns a static <see cref="ModelBuilder"/> instance, allowing for runtime model creation.
	/// </summary>
	public static ModelBuilder Builder => new();

	/// <summary>
	/// A cube model
	/// </summary>
	public static Model Cube { get; internal set; } = Load( "models/dev/box.vmdl" );

	/// <summary>
	/// A sphere model
	/// </summary>
	public static Model Sphere { get; internal set; } = Load( "models/dev/sphere.vmdl" );

	/// <summary>
	/// A plane model
	/// </summary>
	public static Model Plane { get; internal set; } = Load( "models/dev/plane.vmdl" );

	/// <summary>
	/// An error model
	/// </summary>
	public static Model Error { get; internal set; } = Load( "models/dev/error.vmdl" );
}
