using System;

namespace Editor;

/// <summary>
/// Marks a widget as a creatable dock on a DockWindow.
/// Common targets include: Editor, Hammer
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
public class DockAttribute : Attribute, ITypeAttribute
{
	static Dictionary<string, DockWindow> Targets = new();
	static List<DockAttribute> All = new();

	Type ITypeAttribute.TargetType { get; set; }

	void ITypeAttribute.TypeRegister()
	{
		All.Add( this );
		Register();
	}

	void ITypeAttribute.TypeUnregister()
	{
		All.Remove( this );

		if ( !Targets.TryGetValue( Target, out var window ) )
			return;

		if ( !window.IsValid() )
			return;

		window.DockManager.UnregisterDockType( Title );
	}

	public string Target { get; set; }
	public string Title { get; set; }
	public string Icon { get; set; }
	public DockArea Area { get; set; }

	public DockAttribute( string target, string name, string icon = null, DockArea area = DockArea.Bottom )
	{
		Target = target;
		Title = name;
		Icon = icon;
		Area = area;
	}

	public static void RegisterWindow( string name, DockWindow b )
	{
		Targets[name] = b;

		foreach ( var m in All.Where( x => x.Target == name ) )
		{
			m.Register();
		}
	}

	void Register()
	{
		if ( !Targets.TryGetValue( Target, out var window ) )
			return;

		window.DockManager.RegisterDock( new DockManager.DockInfo
		{
			Title = Title,
			Icon = Icon,
			Area = Area,
			CreateAction = () => EditorTypeLibrary.Create<Widget>( (this as ITypeAttribute).TargetType, [window] )
		} );
	}
}
