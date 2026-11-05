namespace SceneTests.Components;

internal static class WaterTestExtensions
{
	/// <summary>
	/// Switch reflected light on the generated surface off or on, isolating its colour and transmission.
	/// The component leaves the shader's default in place, so this override lasts until the surface is rebuilt.
	/// </summary>
	internal static void SetReflectionsEnabled( this WaterBody water, bool enabled )
	{
		var surface = water.GetComponentsInChildren<ModelRenderer>( true ).Single( x => x.Tags.Has( WaterBody.SurfaceTag ) );
		surface.Attributes.Set( "ReflectionEnabled", enabled );
	}
}
