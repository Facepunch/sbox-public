using Sandbox.Audio;
using Sandbox.Engine;
using Sandbox.MovieMaker;
using System;
using System.Reflection;

namespace Editor;

[CustomEditor( typeof( Variant ) )]
public class VariantControlWidget : ControlWidget
{
	readonly SerializedProperty _value;

	public VariantControlWidget( SerializedProperty property ) : base( property )
	{
		if ( !SerializedProperty.TryGetAsObject( out var o ) )
			return;

		_value = o.GetProperty( nameof( Variant.Value ) );

		Layout = Layout.Row();

		RebuildUI();
	}

	void RebuildUI()
	{
		Layout.Clear( true );

		var variant = SerializedProperty.GetValue<Variant>();
		var t = variant.Type;

		if ( t != null )
		{
			var custom = _value.GetCustomizable();
			custom.SetPropertyType( t );
			var editor = ControlWidget.Create( custom );
			if ( editor != null )
			{
				Layout.Add( editor );
			}
			else
			{
				Layout.Add( new Label( $"No editor for type {t.Name}" ) { ContentMargins = new Sandbox.UI.Margin( 8, 4 ) } );
			}
		}
		else
		{
			Layout.Add( new Label( "No type selected" ) { ContentMargins = new Sandbox.UI.Margin( 8, 4 ) } );
		}

		Layout.Add( new IconButton( "arrow_drop_down" ) { Background = Color.Transparent, OnClick = OpenMenu } );
	}

	void OpenMenu()
	{
		var popup = new TypeDropdownWidget( this );
		popup.OnChanged = ( t ) =>
		{
			var variant = SerializedProperty.GetValue<Variant>();
			if ( variant.Type == t )
				return;

			variant = new Variant( null, t );

			if ( t.IsValueType )
			{
				variant.Value = Activator.CreateInstance( t );
			}
			else
			{
				variant.Value = null;
			}

			SerializedProperty.SetValue( variant );

			RebuildUI();
		};
		popup.OpenBelowCursor( 5 );
	}
}

internal class TypeDropdownWidget : AdvancedDropdownPopup
{
	public Action<Type> OnChanged;

	public TypeDropdownWidget( Widget parent ) : base( parent )
	{
		Dropdown.RootTitle = "Types";
		Dropdown.SearchPlaceholderText = "Find Types";
		Dropdown.OnBuildItems = BuildTypes;
		Dropdown.OnSelect = ( value ) =>
		{
			if ( value is Type t )
			{
				OnChanged?.Invoke( t );
			}
		};

		Dropdown.Rebuild();
	}

	private static HashSet<Type> SkipTypes { get; } = new()
	{
		typeof(Variant), // lol
		typeof(ColorHsv),
		typeof(IMovieResource),
		typeof(RangedFloat),
		typeof(GameTransform),
		typeof(SkinnedModelRenderer.ParameterAccessor),
		typeof(MaterialAccessor),
		typeof(Enum),
		typeof(ICodeEditor),
		typeof(MissingComponent),
		typeof(Clothing.IconSetup),
		typeof(DspPresetHandle),
		typeof(Sprite.Frame),
		typeof(System.Delegate),
	};

	protected void BuildTypes( AdvancedDropdownItem root )
	{
		var t = EditorTypeLibrary.GetTypesWithAttribute<CustomEditorAttribute>()
									.Select( x => x.Attribute.TargetType )
									.Where( x => x != null )
									.Where( x => !x.IsGenericType )
									.Where( x => !SkipTypes.Contains( x ) )
									.Where( x => !x.IsEnum )
									.Distinct()
									.ToArray();

		foreach ( var group in t.GroupBy( GetGroup ).OrderBy( x => x.Key ) )
		{
			var cat = group.Key != null ? new AdvancedDropdownItem( group.Key ) : null;
			if ( cat != null )
			{
				root.Add( cat );
			}

			foreach ( var type in group )
			{
				var v = new AdvancedDropdownItem( type.Name ) { Value = type };
				(cat ?? root).Add( v );
			}
		}

		var enumCategory = new AdvancedDropdownItem( "Enums" );
		root.Add( enumCategory );

		foreach ( var e in TypeLibrary.GetTypes().Where( x => x.IsEnum ).OrderBy( x => x.FullName ) )
		{
			var v = new AdvancedDropdownItem( e.FullName ) { Value = e.TargetType };
			enumCategory.Add( v );
		}

	}

	public string GetGroup( Type t )
	{
		if ( t == typeof( Component ) ) return null;
		if ( t == typeof( GameObject ) ) return null;

		var asm = t.Assembly;

		if ( asm.FullName.Contains( "Sandbox.System", StringComparison.OrdinalIgnoreCase ) )
			return "Sandbox";

		if ( asm.FullName.Contains( "System", StringComparison.OrdinalIgnoreCase ) )
			return "System";

		if ( asm.FullName.Contains( "Sandbox", StringComparison.OrdinalIgnoreCase ) )
			return "Sandbox";

		return FormatAssemblyName( asm );
	}

	private static string FormatAssemblyName( Assembly asm )
	{
		var name = asm.GetName().Name!;

		if ( name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) )
		{
			name = name.Substring( "package.".Length );
		}

		if ( name.StartsWith( "local.", StringComparison.OrdinalIgnoreCase ) )
		{
			name = name.Substring( "local.".Length );
		}

		return name.ToTitleCase();
	}
}
