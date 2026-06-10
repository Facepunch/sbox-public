namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// A list of CSS classes applied to this panel.
	/// </summary>
	[Hide]
	public IEnumerable<string> Class => _class ?? Enumerable.Empty<string>();

	/// <inheritdoc cref="Class"/>
	internal HashSet<string> _class;
	internal string _classes;

	/// <summary>
	/// All CSS classes applied to this panel, separated with spaces.
	/// </summary>
	[Property]
	public string Classes
	{
		get
		{
			if ( _classes == null )
				_classes = string.Join( " ", Class );

			return _classes;
		}
		set
		{
			bool had = _class != null && _class.Count > 0;

			_class?.Clear();
			_classes = null;

			if ( had )
				StyleSelectorsChanged( true, true );

			AddClass( value );
		}
	}

	/// <summary>
	/// Adds CSS class(es) separated by spaces to this panel.
	/// </summary>
	public void AddClass( string classname )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		if ( classname.Contains( ' ' ) )
		{
			AddClasses( classname );
			return;
		}

		classname = classname.ToLowerInvariant();

		_class ??= new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( _class.Contains( classname ) ) return;

		_class.Add( classname );
		_classes = null;

		// Adding a class changes the selector so children
		// may have rules that now match - need to update those too.
		StyleSelectorsChanged( true, true );
	}

	/// <summary>
	/// Sets a specific CSS class active or not.
	/// </summary>
	public void SetClass( string classname, bool active )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		if ( active ) AddClass( classname );
		else RemoveClass( classname );
	}

	/// <summary>
	/// Add a class for a set amount of seconds. If called multiple times, we will stomp the earlier call.
	/// </summary>
	public void FlashClass( string classname, float seconds )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		AddClass( classname );
		InvokeOnce( $"FlashClass;{classname}", seconds, () => RemoveClass( classname ) );
	}

	/// <summary>
	/// Add a class if we don't have it, remove a class if we do have it
	/// </summary>
	public void ToggleClass( string classname )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		SetClass( classname, !HasClass( classname ) );
	}

	/// <summary>
	/// Add multiple CSS classes separated by spaces to this panel.
	/// </summary>
	void AddClasses( string classname )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		foreach ( var cname in classname.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries ) )
		{
			AddClass( cname );
		}
	}

	/// <summary>
	/// Removes given CSS class from this panel.
	/// </summary>
	public void RemoveClass( string classname )
	{
		if ( _class == null ) return;
		if ( string.IsNullOrWhiteSpace( classname ) ) return;

		if ( classname.Contains( ' ' ) )
		{
			foreach ( var cname in classname.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries ) )
			{
				RemoveClass( cname );
			}
			return;
		}

		classname = classname.ToLowerInvariant();

		if ( _class.Remove( classname ) )
		{
			// Removing a class changes the selector so children
			// may have rules that now match - need to update those too.
			StyleSelectorsChanged( true, true );
			_classes = null;
		}
	}

	/// <summary>
	/// Whether we have the given CSS class or not.
	/// </summary>
	public bool HasClass( string classname )
	{
		if ( _class == null ) return false;
		if ( string.IsNullOrWhiteSpace( classname ) ) return false;

		if ( _class.Contains( classname ) ) return true;
		return false;
	}

	/// <summary>
	/// Whether if we have <b>all</b> of these CSS classes.
	/// </summary>
	internal bool HasClasses( string[] classes )
	{
		if ( _class == null ) return false;

		for ( int i = 0; i < classes.Length; i++ )
		{
			if ( !_class.Contains( classes[i] ) )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Dirty the styles on this panel
	/// </summary>
	internal void DirtyStylesRecursive()
	{
		StyleSelectorsChanged( true, true );
		Style.InvalidateBroadphase();
	}

	/// <summary>
	/// Dirty the styles of this class and its children recursively.
	/// </summary>
	internal void DirtyStylesWithStyle( Styles withStyles, bool skipTransitions = false )
	{
		if ( Style.ContainsStyle( withStyles ) )
		{
			Style.UnderlyingStyleHasChanged();
			StyleSelectorsChanged( false, false );

			if ( skipTransitions )
				SkipTransitions();
		}

		foreach ( var child in Children )
		{
			child.DirtyStylesWithStyle( withStyles, skipTransitions );
		}
	}

	Dictionary<string, Func<bool>> classBinds;

	/// <summary>
	/// Switch the class on or off depending on the value of the bool.
	/// </summary>
	public void BindClass( string className, Func<bool> func )
	{
		classBinds ??= new();
		classBinds[className] = func;
	}

	private void RunClassBinds()
	{
		if ( classBinds is null ) return;

		foreach ( var c in classBinds )
		{
			SetClass( c.Key, c.Value() );
		}
	}
}
