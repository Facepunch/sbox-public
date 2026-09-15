namespace Editor.MeshEditor;

/// <summary>
/// Shared material selection state. Access only from the editor thread.
/// </summary>
internal static class MaterialSelection
{
	static CookieContainer _projectCookie;
	static Material _current;
	static long _generation;
	static bool _initialized;

	static void CheckProject()
	{
		if ( ReferenceEquals( _projectCookie, ProjectCookie ) ) return;

		_projectCookie = ProjectCookie;
		_current = null;
		_initialized = false;
		_generation++;
	}

	internal static long BeginSelection()
	{
		CheckProject();
		return ++_generation;
	}

	internal static bool IsCurrent( long generation )
	{
		CheckProject();
		return generation == _generation;
	}

	internal static Material Current
	{
		get
		{
			CheckProject();
			if ( !_initialized )
			{
				_initialized = true;
				var path = ProjectCookie.Get( "MeshTool.ActiveMaterial", string.Empty );
				if ( !string.IsNullOrEmpty( path ) )
					_current = Material.Load( path );

				if ( _current is null || !_current.IsValid() )
					_current = Material.Load( "materials/dev/reflectivity_30.vmat" );
			}

			return _current;
		}
		set
		{
			// Re-selecting the same material must also supersede pending cloud requests.
			BeginSelection();
			_current = value;
			_initialized = true;
			if ( value is not null && value.IsValid() )
				ProjectCookie.Set( "MeshTool.ActiveMaterial", value.ResourcePath );
		}
	}
}
