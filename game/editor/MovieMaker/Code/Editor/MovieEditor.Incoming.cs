using Sandbox.Diagnostics;

namespace Editor.MovieMaker;

#nullable enable

partial class MovieEditor : EditorEvent.ISceneEdited
{
	void EditorEvent.ISceneEdited.GameObjectPreEdited( GameObject go, string propertyPath )
	{
		if ( GetOrCreateTrack( go, propertyPath ) is { } view )
		{
			PreChange( view );
		}
	}

	void EditorEvent.ISceneEdited.ComponentPreEdited( Component cmp, string propertyPath )
	{
		if ( GetOrCreateTrack( cmp, propertyPath ) is { } view )
		{
			PreChange( view );
		}
	}

	void EditorEvent.ISceneEdited.GameObjectEdited( GameObject go, string propertyPath )
	{
		if ( GetOrCreateTrack( go, propertyPath ) is { } view )
		{
			PostChange( view );
		}
	}

	void EditorEvent.ISceneEdited.ComponentEdited( Component cmp, string propertyPath )
	{
		if ( GetOrCreateTrack( cmp, propertyPath ) is { } view )
		{
			PostChange( view );
		}
	}

	private string NormalizeGameObjectProperty( string propertyName )
	{
		const string transformPrefix = "Transform.";

		if ( propertyName.StartsWith( transformPrefix, StringComparison.Ordinal ) )
		{
			return propertyName[transformPrefix.Length..];
		}

		return propertyName;
	}

	private bool CanRecord( Type targetType, ref string propertyPath )
	{
		if ( targetType == typeof( GameObject ) )
		{
			propertyPath = NormalizeGameObjectProperty( propertyPath );
		}

		if ( propertyPath.Length > 2 && propertyPath[^2..] is ".x" or ".y" or ".z" or ".w" )
		{
			propertyPath = propertyPath[..^2];
		}

		if ( targetType == typeof( GameObject ) )
		{
			if ( propertyPath is not ("LocalScale" or "LocalRotation" or "LocalPosition") )
			{
				return false;
			}
		}

		return true;
	}

	private TrackView? GetOrCreateTrack( GameObject go, string propertyPath )
	{
		if ( !CanRecord( typeof( GameObject ), ref propertyPath ) ) return null;

		try
		{
			if ( Session?.EditMode?.AllowTrackCreation is not true )
			{
				return Session?.TrackList.Find( go )?.Find( propertyPath );
			}

			var track = Session.GetOrCreateTrack( go, propertyPath );

			Session.TrackList.Update();

			return Session.TrackList.Find( track );
		}
		catch
		{
			// Track not editable
			return null;
		}
	}

	private TrackView? GetOrCreateTrack( Component cmp, string propertyPath )
	{
		if ( !CanRecord( cmp.GetType(), ref propertyPath ) ) return null;

		try
		{
			if ( Session?.EditMode?.AllowTrackCreation is not true )
			{
				return Session?.TrackList.Find( cmp )?.Find( propertyPath );
			}

			var track = Session.GetOrCreateTrack( cmp, propertyPath );

			Session.TrackList.Update();

			return Session.TrackList.Find( track );
		}
		catch
		{
			// Track not editable
			return null;
		}
	}

	private bool PreChange( TrackView view )
	{
		if ( view.IsLocked ) return false;

		if ( Session?.EditMode?.PreChange( view ) is true )
		{
			view.MarkValueChanged();
			return true;
		}

		return false;
	}

	private bool PostChange( TrackView view )
	{
		if ( view.IsLocked ) return false;

		if ( Session?.EditMode?.PostChange( view ) is true )
		{
			view.MarkValueChanged();
			return true;
		}

		return false;
	}
}
