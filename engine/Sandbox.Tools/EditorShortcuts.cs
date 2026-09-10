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

	/// <summary>
	/// Set this to true in a shortcut method to indicate the shortcut should not be consumed,
	/// allowing other shortcuts with the same key binding to be tried.
	/// </summary>
	public static bool PassShortcut { get; set; }

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

	public static void ReleaseAll()
	{
		foreach ( var entry in Entries )
		{
			entry.IsDown = false;
		}
	}

	/// <summary>
	/// Try to invoke any shortcuts bound to mouse wheel up/down.
	/// Returns true if a shortcut consumed the wheel event.
	/// </summary>
	internal static bool InvokeWheel( int delta, KeyboardModifiers modifiers )
	{
		if ( !AllowShortcuts ) return false;
		if ( delta == 0 ) return false;

		var wheelKey = delta > 0 ? "MWHEELUP" : "MWHEELDOWN";

		var modifiedKey = wheelKey;
		if ( modifiers.HasFlag( KeyboardModifiers.Shift ) ) modifiedKey = "SHIFT+" + modifiedKey;
		if ( modifiers.HasFlag( KeyboardModifiers.Alt ) ) modifiedKey = "ALT+" + modifiedKey;
		if ( modifiers.HasFlag( KeyboardModifiers.Ctrl ) ) modifiedKey = "CTRL+" + modifiedKey;

		if ( modifiers != KeyboardModifiers.None && Invoke( modifiedKey ) )
			return true;

		if ( Invoke( wheelKey ) )
			return true;

		return false;
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
			var type = force ? ShortcutType.Application : Attribute.Type;

			// Widgets of the target type gate whether this shortcut is currently accessible
			IEnumerable<Widget> gates;
			if ( MethodDesc.IsStatic && TargetKey == TypeKey )
			{
				gates = [EditorWindow];
				if ( !force ) type = ShortcutType.Window;
			}
			else
			{
				// Prefer the focused gate so forced invokes (e.g. menu options) hit the right scene view
				gates = InstancesOf( TargetKey ).OrderByDescending( ContainsFocus );
			}

			foreach ( var gate in gates )
			{
				if ( !IsAccessible( gate, type ) )
					continue;

				var instance = MethodDesc.IsStatic ? null : ResolveInstance( gate );
				if ( !MethodDesc.IsStatic && instance is null )
					continue;

				MethodDesc.Invoke( instance );

				if ( PassShortcut )
				{
					PassShortcut = false;
					return false;
				}

				return true;
			}

			return false;
		}

		static bool ContainsFocus( Widget w ) => w.IsValid() && (w.IsFocused || (Application.FocusWidget?.IsDescendantOf( w ) ?? false));

		static bool IsAccessible( Widget w, ShortcutType type ) => w.IsValid() && type switch
		{
			ShortcutType.Window => w.IsActiveWindow,
			ShortcutType.Widget => w.Visible && w.Enabled && ContainsFocus( w ),
			_ => true,
		};

		/// <summary>
		/// All registered widgets of the given type.
		/// </summary>
		static IEnumerable<Widget> InstancesOf( Type type )
		{
			return Targets.Where( t => t.Key.IsAssignableTo( type ) )
				.SelectMany( t => t.Value )
				.OfType<Widget>();
		}

		/// <summary>
		/// The widget the shortcut method is invoked on. Usually the gating widget itself, but for
		/// shortcuts with a target override it's the nearest instance of the declaring type - found
		/// by widening the search scope one ancestor at a time - so shortcuts stay within the same
		/// scene view as the gate, whether the declaring widget is a descendant or a sibling of it.
		/// </summary>
		Widget ResolveInstance( Widget gate )
		{
			if ( TargetKey == TypeKey )
				return gate;

			var candidates = InstancesOf( TypeKey ).ToList();

			for ( var scope = gate; scope.IsValid(); scope = scope.Parent )
			{
				var match = candidates.FirstOrDefault( w => w == scope || w.IsDescendantOf( scope ) );
				if ( match is not null )
					return match;
			}

			return null;
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
