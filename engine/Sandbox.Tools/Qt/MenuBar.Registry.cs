using System;

namespace Editor;

public partial class MenuBar : Widget
{
	static Dictionary<string, MenuBar> Targets = new();
	static Action Unregister;

	/// <summary>
	/// Register a named menubar target. This allows [Menu] to target a specific menubar.
	/// </summary>
	public static void RegisterNamed( string name, MenuBar b )
	{
		Targets[name] = b;
		RegisterAll();
	}

	[Event( "refresh" )]
	static void RegisterAll()
	{
		Unregister?.Invoke();

		foreach ( var target in Targets )
		{
			if ( !target.Value.IsValid ) continue;

			using var su = SuspendUpdates.For( target.Value );

			foreach ( var m in EditorTypeLibrary.GetMembersWithAttribute<MenuAttribute>().Where( x => x.Attribute.Target == target.Key ).OrderBy( x => x.Attribute.Priority ) )
			{
				Register( m.Attribute, m.Member, target.Value );
			}
		}
	}

	static void Register( MenuAttribute attr, MemberDescription member, MenuBar menuBar )
	{
		if ( member is MethodDescription method )
		{
			var shortcut = method.GetCustomAttribute<ShortcutAttribute>();
			var o = menuBar.AddOption( attr.Path, attr.Icon, () => method.Invoke( null, null ), shortcut?.Identifier ?? null );

			Unregister += () => o.Destroy();
		}

		if ( member is PropertyDescription property )
		{
			var option = new Option( menuBar, "..." );
			option.Checkable = true;

			option.FetchCheckedState = () => (bool)property.GetValue( null );
			option.Checked = option.FetchCheckedState();

			if ( !string.IsNullOrEmpty( attr.Icon ) )
			{
				option.Icon = attr.Icon;
			}

			option.Triggered += () =>
			{
				property.SetValue( null, option.Checked );
			};

			if ( property.HasAttribute<ShortcutAttribute>() )
				option.ShortcutName = property.GetCustomAttribute<ShortcutAttribute>().Identifier;

			menuBar.AddOption( attr.Path, option );
			Unregister += () => option.Destroy();
		}

	}
}
