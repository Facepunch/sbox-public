
using Sandbox.Engine;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Whether <see cref="Delete"/> was called on this panel.
	/// </summary>
	[Hide]
	public bool IsDeleting { get; internal set; }


	private bool IsDeleted { get; set; }
	HashSet<Panel> renderTreeDeletion;
	bool renderTreeDeletePending;

	internal void DeleteFromRenderTree( Panel outroParent, bool immediate )
	{
		if ( IsDeleted ) return;
		if ( IsDeleting )
		{
			if ( immediate )
			{
				// Its virtual Delete already ran when its independent outro started.
				Parent = null;
				IsVisible = false;
				OnDeleteRecursive();
			}
			return;
		}

		if ( outroParent is not null && outroParent != this && IsAncestor( outroParent ) )
		{
			// Keep physical content alive, including resources released by virtual Delete overrides.
			outroParent.renderTreeDeletion ??= new();
			outroParent.renderTreeDeletion.Add( this );
			renderTreeDeletePending = true;
			return;
		}

		renderTreeDeletePending = false;
		try
		{
			Delete( immediate );
		}
		catch ( System.Exception ex )
		{
			Log.Error( ex, "Error when deleting a render-tree panel" );
			// An override may throw before base.Delete. Finish base cleanup without replaying it,
			// and don't let this panel prevent its siblings or owner from being cleaned up.
			try
			{
				Parent = null;
				IsVisible = false;
				IsDeleting = true;
				OnDeleteRecursive();
			}
			catch ( System.Exception cleanupException )
			{
				Log.Error( cleanupException, "Error when cleaning up a failed render-tree panel deletion" );
			}
		}
	}

	/// <summary>
	/// Deletes the panel.
	/// </summary>
	/// <param name="immediate">If <see langword="true"/>, will skip any outros. (<c>:outro</c> CSS pseudo class)</param>
	public virtual void Delete( bool immediate = false )
	{
		if ( IsDeleted )
			return;

		_deleteTokenSource?.Cancel();

		if ( immediate )
		{
			Parent = null;
			IsVisible = false;
			IsDeleting = true;
			OnDeleteRecursive();
			return;
		}

		if ( IsDeleting )
			return;

		IsDeleting = true;
		Transitions.Clear(); // stop any intros
		Switch( PseudoClass.Outro, true );
		UISystem.AddDeferredDeletion( this );
	}

	/// <summary>
	/// Called when the panel is about to be deleted.
	/// </summary>
	public virtual void OnDeleted()
	{

	}

	/// <summary>
	/// Called on delete.
	/// </summary>
	internal void OnDeleteRecursive()
	{
		if ( IsDeleted ) return;
		IsDeleted = true;

		try
		{
			RemoveFromLists();
			RemoveFromSceneIndex();

			Task.Expire();

			// Clear logical ownership before physical recursion bypasses children's Delete overrides.
			renderTree?.Clear( immediate: true );
			renderTree = null;

			var pending = renderTreeDeletion;
			renderTreeDeletion = null;
			if ( pending is not null )
			{
				foreach ( var panel in pending )
					panel.DeleteFromRenderTree( null, true );
			}

			foreach ( var child in Children.ToArray() )
			{
				if ( child.renderTreeDeletePending && !child.IsDeleting )
					child.DeleteFromRenderTree( null, true );
				else
					child.OnDeleteRecursive();
			}

			try
			{

				OnDeleted();
			}
			catch ( System.Exception ex )
			{
				Log.Error( ex, "Error when calling OnDeleted" );
			}

			// Clear any focus we may have. UISystem is null for a panel that's already detached
			// in a context with no global fallback system, e.g. a panel window app.
			// TODO: Ideally this would cascade to parents who accept focus, but we'd need to change how Panels are removed.
			if ( UISystem is { } system && system.CurrentFocus == this )
			{
				system.ClearFocus( this );
			}

			if ( MouseCapture == this )
			{
				SetMouseCapture( false );
			}

			LayoutTree?.Dispose();
			LayoutTree = null;

			ComputedStyle = null;
			_paintCache = default;
			StyleSheet = default;
			GameObject = null;

			// Drop the PanelStyle — its _styleBlocks cache holds StyleBlock refs
			// whose Styles._backgroundImage/_maskImage keep textures alive.
			Style = null;

			_renderChildren = null;
			_childrenHash = null;
			_children = null;
			_parent = null;
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}
	}


}
