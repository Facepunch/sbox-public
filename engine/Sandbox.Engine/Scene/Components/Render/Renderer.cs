using Sandbox.Rendering;
using static Sandbox.SceneObject;

namespace Sandbox;

public abstract class Renderer : Component, SceneObjectCallbacks
{
	RenderAttributes _attributes;
	RenderOptions _renderOptions;
	SceneObject _sceneObject;

	[Property, MakeDirty, Order( -100 ), InlineEditor( Label = false ), Group( "Advanced Rendering", StartFolded = true )]
	public RenderOptions RenderOptions
	{
		get
		{
			_renderOptions ??= new( OnRenderOptionsChanged );
			return _renderOptions;
		}
	}

	protected virtual void OnRenderOptionsChanged()
	{

	}

	/// <summary>
	/// Copy everything from another renderer
	/// </summary>
	public virtual void CopyFrom( Renderer other )
	{
		//RenderOptions = other.RenderOptions;
	}

	CommandList _before;
	CommandList _after;

	/// <summary>
	/// Attributes that are applied to the renderer based on the current material and shader.
	/// If the renderer is disabled, the changes are deferred until it is enabled again.
	/// Attributes are not saved to disk, and are not cloned when copying the renderer.
	/// </summary>
	public RenderAttributes Attributes
	{
		get
		{
			if ( _sceneObject.IsValid() )
			{
				return _sceneObject.Attributes;
			}
			_attributes ??= new RenderAttributes();
			return _attributes;
		}
	}

	/// <summary>
	/// A command list which is executed immediately before rendering this
	/// </summary>
	public CommandList ExecuteBefore
	{
		get => _before;

		set
		{
			if ( _before == value ) return;
			_before = value;
			UpdateSceneObjectFlags();
		}
	}

	/// <summary>
	/// A command list which is executed immediately after rendering this
	/// </summary>
	public CommandList ExecuteAfter
	{
		get => _after;

		set
		{
			if ( _after == value ) return;
			_after = value;
			UpdateSceneObjectFlags();
		}
	}

	void SceneObjectCallbacks.OnBeforeObjectRender()
	{
		ExecuteBefore?.ExecuteOnRenderThread();
	}

	void SceneObjectCallbacks.OnAfterObjectRender()
	{
		ExecuteAfter?.ExecuteOnRenderThread();
	}

	void UpdateSceneObjectFlags()
	{
		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.Flags.WantsExecuteBefore = ExecuteBefore != null;
		_sceneObject.Flags.WantsExecuteAfter = ExecuteAfter != null;
	}

	/// <summary>
	/// Backup the specified RenderAttributes so we can restore them later with <see cref="RestoreRenderAttributes(RenderAttributes)"/>
	/// </summary>
	protected void BackupRenderAttributes( RenderAttributes attributes )
	{
		if ( attributes is null || !_sceneObject.IsValid() )
			return;

		_attributes ??= new RenderAttributes();
		attributes.MergeTo( _attributes );
	}

	/// <summary>
	/// Restore any attributes that were previously backed up with <see cref="BackupRenderAttributes(RenderAttributes)"/>
	/// </summary>
	protected void RestoreRenderAttributes( RenderAttributes attributes )
	{
		if ( _attributes is not null )
		{
			_attributes.MergeTo( attributes );
		}

		_attributes = null;
	}

	internal virtual void OnSceneObjectCreated( SceneObject obj )
	{
		_sceneObject = obj;
		_sceneObject.Tags.SetFrom( GameObject.Tags );
		_sceneObject.CallbackTarget = this;
		UpdateSceneObjectFlags();
		RestoreRenderAttributes( obj.Attributes );
	}

	/// <summary>
	/// Render the <see cref="SceneObject"/> of this renderer with the specified overrides.
	/// </summary>
	internal virtual void RenderSceneObject( RendererSetup rendererSetup = default )
	{
		if ( !_sceneObject.IsValid() )
			return;

		Graphics.Render( _sceneObject, rendererSetup.Transform, rendererSetup.Color, rendererSetup.Material );
	}
}
