using System;

namespace Editor.MapEditor;

public static partial class Hammer
{
	/// <summary>
	/// Names of every Hammer command <see cref="RunCommand"/> can run - the same commands key
	/// bindings and menus trigger, like "Undo", "GroupSelection" or "FrameAllViews".
	/// </summary>
	public static IReadOnlyList<string> Commands
	{
		get
		{
			AssertAppValid();

			return App.GetCommandNames()
				.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
				.Distinct( StringComparer.OrdinalIgnoreCase )
				.ToArray();
		}
	}

	/// <summary>
	/// Run a Hammer command by name on the active map, exactly as if its key binding was pressed.
	/// Returns false if no command has that name. See <see cref="Commands"/> for what exists.
	/// </summary>
	public static bool RunCommand( string name )
	{
		AssertAppValid();
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		return App.InvokeCommand( name );
	}

	/// <summary>
	/// Whether the active map has changes that haven't been saved.
	/// </summary>
	public static bool HasUnsavedChanges
	{
		get
		{
			AssertAppValid();
			return App.IsActiveMapModified();
		}
	}

	/// <summary>
	/// Save the active map to its file. Never prompts - returns false if the map has never been
	/// saved, so has no file to save to, or the save failed.
	/// </summary>
	public static bool Save()
	{
		AssertAppValid();
		return App.SaveActiveMap();
	}

	/// <summary>
	/// Every view of the active map, 2D and 3D.
	/// </summary>
	public static IEnumerable<MapView> MapViews
	{
		get
		{
			AssertAppValid();

			var count = App.GetMapViewCount();
			for ( int i = 0; i < count; i++ )
			{
				var view = App.GetMapView( i );
				if ( view is not null ) yield return view;
			}
		}
	}

	/// <summary>
	/// The view of the active map the user interacted with most recently.
	/// </summary>
	public static MapView ActiveMapView
	{
		get
		{
			AssertAppValid();
			return App.GetActiveMapView();
		}
	}
}
