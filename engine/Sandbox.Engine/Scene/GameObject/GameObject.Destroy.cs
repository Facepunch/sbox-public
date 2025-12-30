namespace Sandbox;


public partial class GameObject : IValid
{
	bool _destroying;
	bool _destroyed;

	/// <summary>
	/// True if the GameObject is not destroyed
	/// </summary>
	public virtual bool IsValid => !_destroyed && Scene is not null;

	/// <summary>
	/// Actually destroy the object and its children. Turn off and destroy components.
	/// </summary>
	private void Term()
	{
		using var batch = CallbackBatch.Batch();
		_destroying = true;

		// Tell our children we're gonna die, give them a chance to become orphaned
		ForEachChild( "Children", true, c => c.OnParentBeingDestroyed() );

		Components.ForEach( "OnDestroy", true, c => c.Destroy() );
		ForEachChild( "Children", true, c => c.Term() );

		CallbackBatch.Add( CommonCallback.Term, TermFinal, this, "Term" );
	}

	/// <summary>
	/// Called on a child GameObject when its parent is being destroyed
	/// </summary>
	private void OnParentBeingDestroyed()
	{
		// don't call if we're shutting the scene down
		if ( Scene.IsDestroyed )
			return;

		// don't call if we're an editor scene
		if ( Scene.IsEditor )
			return;

		Components.ForEach( "OnParentDestroy", true, c => c.OnParentDestroyInternal() );
	}

	/// <summary>
	/// Destroy the object and its children and destroy any components without
	/// invoking any callbacks.
	/// </summary>
	private void TermSilent()
	{
		_destroying = true;
		ForEachChild( "Children", true, c => c.TermSilent() );
		TermFinal();
	}

	/// <summary>
	/// The last thing ever called.
	/// </summary>
	private void TermFinal()
	{
		if ( _destroyed )
			return;

		_destroyed = true;
		_net?.SendNetworkDestroy();

		ClearNetworking();
		Transform.Interpolate = false;

		Scene.Directory.Remove( this );
		Enabled = false;
		_parent?.RemoveChild( this );
		_parent = null;
		Scene = null;

		Children.RemoveAll( x => x is null );
		Components.RemoveNull();

		if ( Components.Count > 0 ) Log.Warning( $"Some components weren't deleted ({Components.Count})" );
		if ( Children.Count > 0 ) Log.Warning( $"Some children weren't deleted ({Children.Count})" );

		SceneMetrics.GameObjectsDestroyed++;
	}

	/// <summary>
	/// Destroy this object. Will actually be destroyed at the start of the next frame.
	/// </summary>
	[ActionGraphInclude]
	public virtual void Destroy()
	{
		ThreadSafe.AssertIsMainThread();

		if ( _destroying )
			return;

		_destroying = true;
		Scene?.QueueDelete( this );
	}

	/// <summary>
	/// Return true if this object is destroyed. This will also return true if the object is marked to be destroyed soon.
	/// </summary>
	public bool IsDestroyed => _destroying || _destroyed;

	/// <summary>
	/// Destroy this object immediately. Calling this might cause some problems if functions
	/// are expecting the object to still exist, so it's not always a good idea.
	/// </summary>
	public void DestroyImmediate()
	{
		Assert.False( this is Scene, "Don't call DestroyImmediate on a scene." );
		ThreadSafe.AssertIsMainThread();

		Term();
	}

	/// <summary>
	/// Remove all children
	/// </summary>
	/// <param name="child"></param>
	private void RemoveChild( GameObject child )
	{
		var i = Children.IndexOf( child );
		if ( i < 0 ) return;

		Children.RemoveAt( i );
	}

	/// <summary>
	/// Destroy all components and child objects
	/// </summary>
	[ActionGraphInclude]
	public void Clear() => Clear( true );

	/// <summary>
	/// True if this GameObject should survive a scene load
	/// </summary>
	private bool ShouldSurviveSceneTransition => Flags.Contains( GameObjectFlags.DontDestroyOnLoad );

	/// <summary>
	/// Destroy all components and child objects
	/// </summary>
	internal virtual void Clear( bool includeSaved )
	{
		// Delete all components (this might create new children)
		Components.ForEach( "OnDestroy", true, c => c.Destroy() );

		// First delete all of the children
		ForEachChild( "Children", true, c =>
		{
			if ( includeSaved || !c.ShouldSurviveSceneTransition )
				c.Term();
		} );

		// Destroy any newly created children silently
		ForEachChild( "Children", true, c =>
		{
			if ( includeSaved || !c.ShouldSurviveSceneTransition )
				c.TermSilent();
		} );

		Components.RemoveNull();
		Children.RemoveAll( x => x is null );

		Assert.AreEqual( 0, Components.Count, $"{Components.Count} components weren't deleted!" );

		if ( includeSaved )
		{
			Assert.AreEqual( 0, Children.Count, $"{Children.Count} children weren't deleted!" );
		}

		// Actually delete the objects
		Scene?.SceneWorld?.DeletePendingObjects();
	}
}
