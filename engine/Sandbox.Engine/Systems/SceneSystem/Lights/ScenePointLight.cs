
using NativeEngine;

namespace Sandbox;

/// <summary>
/// A point light scene object for use in a <see cref="SceneWorld"/>.
/// </summary>
[Expose]
public sealed class ScenePointLight : SceneLight
{
	public ScenePointLight( SceneWorld sceneWorld, Vector3 position, float radius, Color color )
	{
		Assert.IsValid( sceneWorld );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreatePointLight( sceneWorld );
		}

		Position = position;
		Radius = radius;
		LightColor = color;
		QuadraticAttenuation = 1.0f;
	}

	public ScenePointLight( SceneWorld sceneWorld ) : this( sceneWorld, Vector3.Zero, 100, Color.White * 10.0f )
	{

	}
}
