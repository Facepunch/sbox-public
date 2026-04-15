
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
		GlobalContext.Current.UISystem.AddDeferredDeletion( this );
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
		IsDeleted = true;

		try
		{
			RemoveFromLists();

			Task.Expire();

			foreach ( var child in Children )
			{
				child?.OnDeleteRecursive();
			}

			try
			{

				OnDeleted();
			}
			catch ( System.Exception ex )
			{
				Log.Error( ex, "Error when calling OnDeleted" );
			}

			// Clear any focus we may have
			// TODO: Ideally this would cascade to parents who accept focus, but we'd need to change how Panels are removed.
			if ( InputFocus.Current == this )
			{
				InputFocus.Clear( this );
			}

			if ( MouseCapture == this )
			{
				SetMouseCapture( false );
			}

			YogaNode?.Dispose();
			YogaNode = null;

			// Destroy the razor render tree — Block.ElementPanel holds strong refs to
			// dynamically-created child panels whose Style.StyleBlocks keep parsed
			// stylesheet textures (gradients, masks, etc.) alive past shutdown.
			renderTree?.Clear();
			renderTree = null;

			if ( CachedDescriptors != null )
			{
				RenderLayer.Return( CachedDescriptors );
				CachedDescriptors = null;
			}

			ComputedStyle = null;
			StyleSheet = default;
			GameObject = null;

			// Drop the PanelStyle — its StyleBlocks[] cache holds StyleBlock refs
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
