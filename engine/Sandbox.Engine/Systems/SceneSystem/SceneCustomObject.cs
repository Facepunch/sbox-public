using NativeEngine;

namespace Sandbox;

/// <summary>
/// A scene object that allows custom rendering within a scene world.
/// </summary>
public class SceneCustomObject : SceneObject
{
	internal NativeEngine.CManagedSceneObject managedNative;

	internal SceneCustomObject( HandleCreationData _ ) { }


	public SceneCustomObject( SceneWorld sceneWorld )
	{
		Assert.IsValid( sceneWorld );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CManagedSceneObject.Create( sceneWorld );

			if ( native.IsValid )
			{
				// Start off with infinite bounds so it will actually get rendered
				// if the user forgets/doesn't need bounds.
				native.SetBoundsInfinite();
			}

			// Initialize Transform so it will render (default Transform has scale 0)
			Transform = Transform.Zero;
		}
	}

	internal unsafe override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		managedNative = (NativeEngine.CManagedSceneObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		managedNative = default;
		base.OnNativeDestroy();
	}

	internal void RenderInternal()
	{
		if ( !this.IsValid() )
			return;

		try
		{
			RenderSceneObject();
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}
	}

	/// <summary>
	/// Called by default version of <see cref="RenderSceneObject"/>.
	/// </summary>
	public Action<SceneObject> RenderOverride;

	/// <summary>
	/// Called when this scene object needs to be rendered.
	/// Invokes <see cref="RenderOverride"/> by default. See the <see cref="Graphics" /> library for a starting point.
	/// </summary>
	public virtual void RenderSceneObject()
	{
		RenderOverride?.Invoke( this );
	}
}

internal static class SceneCustomObjectRender
{
	internal static void RenderObject( ManagedRenderSetup_t setup, SceneCustomObject obj )
	{
		if ( obj is null )
			return;

		using ( new Graphics.Scope( in setup ) )
		{
			obj.RenderInternal();
		}
	}
}
