using System;

namespace Editor;

public static class EditorShortcuts
{
	public static List<Entry> Entries = new();
	static Dictionary<Type, List<object>> Targets = new();

	public static bool AllowShortcuts
	{
		get => _timeSinceInputsBlocked >= 0.05f;
		set => _timeSinceInputsBlocked = value ? 1f : 0f;
	}
	static RealTimeSince _timeSinceInputsBlocked = 0f;

	internal static RealTimeSince _timeSinceGlobalShortcut = 0f;

	[Event( "editor.created" )]
	static void EditorCreated( EditorMainWindow _ )
	{
		RegisterShortcuts();
	}

	[EditorEvent.Hotload]
	[Event( "keybinds.update" )]
	internal static void RegisterShortcuts()
	{
		Entries.Clear();

		var methods = EditorTypeLibrary.GetMethodsWithAttribute<ShortcutAttribute>( false );

		foreach ( var method in methods )
		{
			var group = "";
			if ( method.Method.HasAttribute<GroupAttribute>() )
			{
				group = method.Method.GetCustomAttribute<GroupAttribute>().Value;
			}

			// Make sure static methods on non-widget es are treated as window shortcuts
			var attributeTarget = method.Attribute.TargetOverride ?? method.Method.TypeDescription.TargetType;
			if ( !attributeTarget.IsSubclassOf( typeof( Widget ) ) && method.Attribute.Type == ShortcutType.Widget )
			{
				method.Attribute.Type = ShortcutType.Window;
			}

			var entry = new Entry( method.Method, method.Attribute, group );
			Entries.Add( entry );
		}
	}

	internal static void Register( object obj )
	{
		var typeKey = obj.GetType();
		if ( !Targets.ContainsKey( typeKey ) )
			Targets[typeKey] = new List<object>();

		Targets[typeKey].Add( obj );
	}

	internal static void Unregister( object obj )
	{
		var typeKey = obj.GetType();
		if ( !Targets.ContainsKey( typeKey ) )
			return;

		Targets[typeKey].Remove( obj );
	}

	internal static bool Invoke( string keys, bool force = false )
	{
		// Don't invoke shortcuts if the focus widget if we're typing in a LineEdit and holding CTRL or ALT (Not SHIFT since it's used for capital letters)
		if ( (Application.FocusWidget is LineEdit || Application.FocusWidget is TextEdit) && (!keys.Split( "+" ).Any( x => x == "CTRL" || x == "ALT" ) || keys == "CTRL+A" || keys == "CTRL+C" || keys == "CTRL+V" || keys == "CTRL+X") ) return false;

		// widgets first, then work up to app-level
		bool hasInvoked = false;
		var groups = Entries.OrderBy( x => x.Attribute.Type ).GroupBy( x => x.TargetKey );
		foreach ( var group in groups )
		{
			bool justInvoked = false;
			foreach ( var entry in group )
			{
				if ( GetKeys( entry.Identifier ) != keys ) continue;
				entry.IsDown = true;

				if ( AllowShortcuts && !hasInvoked && entry.Invoke( force ) )
				{
					justInvoked = true;
				}
			}
			if ( justInvoked ) hasInvoked = true;
		}

		return hasInvoked;
	}

	internal static void Press( string key )
	{
		foreach ( var entry in Entries )
		{
			if ( GetKeys( entry.Identifier ).Split( "+" ).LastOrDefault() == key )
			{
				entry.IsDown = true;
			}
		}
	}

	internal static void Release( string key )
	{
		foreach ( var entry in Entries )
		{
			if ( GetKeys( entry.Identifier ).Split( "+" ).LastOrDefault() == key )
			{
				entry.IsDown = false;
			}
		}
	}

	internal static void ReleaseAll()
	{
		foreach ( var entry in Entries )
		{
			entry.IsDown = false;
		}
	}

	/// <summary>
	/// Returns the keybind for a given identifier
	/// </summary>
	/// <param name="identifier">The identifier of the shortcut</param>
	public static string GetKeys( string identifier )
	{
		var entry = Entries.FirstOrDefault( x => x.Identifier == identifier );
		if ( entry != null )
			return entry.Keys;

		return "";
	}

	/// <summary>
	/// Returns the pretty key hint for a given identifier
	/// </summary>
	/// <param name="identifier">The identifier of the shortcut</param>
	public static string GetDisplayKeys( string identifier )
	{
		var entry = Entries.FirstOrDefault( x => x.Identifier == identifier );
		if ( entry != null )
			return entry.DisplayKeys;

		return "";
	}

	/// <summary>
	/// Returns the default keybind for a given identifier
	/// </summary>
	/// <param name="identifier">The identifier of the shortcut</param>
	public static string GetDefaultKeys( string identifier )
	{
		var entry = Entries.FirstOrDefault( x => x.Identifier == identifier );
		if ( entry != null )
			return entry.Attribute.Keys;

		return "";

	}

	/// <summary>
	/// Returns whether a given shortcut is currently being held down
	/// </summary>
	/// <param name="identifier">The identifier of the shortcut</param>
	public static bool IsDown( string identifier )
	{
		var entry = Entries.FirstOrDefault( x => x.Identifier == identifier );
		if ( entry != null )
			return entry.IsDown;

		return false;
	}

	public class Entry
	{
		public string Identifier { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		internal Type TypeKey { get; set; }
		internal Type TargetKey { get; set; }

		public bool IsDown { get; internal set; }

		public string Keys
		{
			get => _keys;
			set
			{
				_keys = value.Trim().ToUpperInvariant();
				UpdateDisplayKeys();
				if ( Attribute.Keys.Trim().ToUpperInvariant() == _keys )
					EditorPreferences.ShortcutOverrides.Remove( Identifier );
				else
				{
					var overrides = EditorPreferences.ShortcutOverrides;
					overrides[Identifier] = value;
					EditorPreferences.ShortcutOverrides = overrides;
				}
			}
		}
		string _keys;

		public string DisplayKeys { get; private set; }

		MethodDescription MethodDesc;
		public ShortcutAttribute Attribute { get; init; }

		public Entry( MethodDescription desc, ShortcutAttribute attribute, string group = "" )
		{
			Identifier = attribute.Identifier;
			MethodDesc = desc;
			Attribute = attribute;
			Group = group;
			TargetKey = attribute.TargetOverride ?? desc.TypeDescription.TargetType;
			TypeKey = desc.TypeDescription.TargetType;
			GetNameFromIdent( Identifier );

			ResetKeys();
		}

		public bool Invoke( bool force = false )
		{
			ShortcutType type = force ? ShortcutType.Application : Attribute.Type;

			var targets = new List<object>();
			if ( MethodDesc.IsStatic && TargetKey == TypeKey && targets.Count == 0 )
			{
				targets.Add( EditorWindow );
				if ( !force ) type = ShortcutType.Window;
			}
			else
			{
				foreach ( var target in Targets )
				{
					if ( target.Key.IsAssignableTo( TargetKey ) )
					{
						targets.AddRange( target.Value );
					}
				}
			}

			bool invoked = false;
			foreach ( var target in targets )
			{
				if ( target is not Widget w )
					continue;

				if ( type == ShortcutType.Window )
				{
					if ( !w.IsActiveWindow )
						continue;
				}
				else if ( type == ShortcutType.Widget )
				{
					var accessible = w.Visible && w.Enabled && (w.IsFocused || (Application.FocusWidget?.IsDescendantOf( w ) ?? false));
					if ( type == ShortcutType.Widget && !accessible )
						continue;
				}

				invoked = true;

				// Only invoke the method if we don't have a custom target override
				if ( MethodDesc.IsStatic || TargetKey == TypeKey )
				{
					MethodDesc?.Invoke( MethodDesc.IsStatic ? null : target );
					break;
				}
			}

			// If we would have invoked the method, but we have a custom target override, invoke the method for the custom target(s)
			if ( !MethodDesc.IsStatic && invoked && TargetKey != TypeKey )
			{
				invoked = false;
				foreach ( var target in Targets )
				{
					if ( target.Key.IsAssignableTo( TypeKey ) )
					{
						var focusedTarget = target.Value.OrderByDescending( x => (x as Widget)?.IsFocused ?? false ).FirstOrDefault();
						if ( focusedTarget == null ) continue;
						invoked = true;
						MethodDesc?.Invoke( focusedTarget );
						break;
					}
				}
			}

			return invoked;
		}

		void ResetKeys()
		{
			if ( EditorPreferences.ShortcutOverrides.ContainsKey( Identifier ) )
				Keys = EditorPreferences.ShortcutOverrides[Identifier];
			else
				Keys = Attribute.Keys;
		}

		void UpdateDisplayKeys()
		{
			DisplayKeys = string.Join( '+', _keys.Split( '+' )
				.Select( x => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase( x.ToLower() ) ) );
		}

		void GetNameFromIdent( string ident )
		{
			var split = ident.Split( '.' ).ToList();
			if ( split.Count == 1 ) split.Insert( 0, "other" ); // "other" is a fallback group for shortcuts that don't have a group
			if ( string.IsNullOrEmpty( Group ) )
			{
				Group = split.FirstOrDefault();
				Group = Group.Replace( "-", " " ).Replace( "_", " " );
				Group = string.Join( " ", Group.Split( ' ' ).Select( x => char.ToUpper( x[0] ) + x.Substring( 1 ) ) );
			}
			var combined = split.Skip( 1 ).Aggregate( ( a, b ) => a + "." + b );
			Name = combined.Replace( "-", " " ).Replace( "_", " " );
			Name = string.Join( " ", Name.Split( ' ' ).Select( x => char.ToUpper( x[0] ) + x.Substring( 1 ) ) );
		}
	}
}
