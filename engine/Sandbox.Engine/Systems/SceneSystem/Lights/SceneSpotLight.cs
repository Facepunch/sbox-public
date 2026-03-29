
using NativeEngine;

namespace Sandbox;

/// <summary>
/// A simple spot light scene object for use in a <see cref="SceneWorld"/>.
/// </summary>
[Expose]
public sealed class SceneSpotLight : SceneLight
{
	/// <summary>
	/// The inner cone of the spotlight, in half angle degrees.
	/// </summary>
	public float ConeInner
	{
		get { return lightNative.GetTheta(); }
		set { lightNative.SetTheta( value ); }
	}

	/// <summary>
	/// The outer cone of the spotlight, in half angle degrees
	/// </summary>
	public float ConeOuter
	{
		get { return lightNative.GetPhi(); }
		set { lightNative.SetPhi( value ); }
	}

	public float FallOff
	{
		get { return lightNative.GetFallOff(); }
		set { lightNative.SetFallOff( value ); }
	}

	public SceneSpotLight( SceneWorld world, Vector3 position, Color color ) : base()
	{
		Assert.IsValid( world );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateSpotLight( world );
		}

		LightColor = color;
		Position = position;
		QuadraticAttenuation = 1.0f;
		Radius = 100;
	}

	public SceneSpotLight( SceneWorld world ) : this( world, Vector3.Zero, Color.White * 10.0f )
	{

	}
}
