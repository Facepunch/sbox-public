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
				KeepFixedBounds( infinite: true );
			}
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
			if ( BorealisGpuErrors.ShouldLog( e ) ) Log.Error( e );
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

	internal override bool OwnsRenderObject => true;

	internal override Borealis.RenderObject MakeRenderObject( Rendering.ISceneWorldRender render ) => new Rendering.BridgeCustomObject( this );

	internal override void WriteRender( Borealis.RenderObject obj, Rendering.SceneObjectChange change, Rendering.ISceneWorldRender render )
	{
		base.WriteRender( obj, change, render );
		if ( obj is Borealis.CustomObject custom ) WriteCustom( custom, change );
	}
}
