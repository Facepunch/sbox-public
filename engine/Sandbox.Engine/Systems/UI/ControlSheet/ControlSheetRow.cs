using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

/// <summary>
/// Used by ControlSheet to display a single row of a property. This is created from a SerializedProperty
/// and contains a label and a control for editing the property. Controls are created using BaseControl.CreateFor.
/// </summary>
[StyleSheet.Inline( "controlsheetrow", Styles )]
public class ControlSheetRow : Panel
{
	const string Styles = """

		ControlSheetRow
		{
			flex-direction: row;
			flex-shrink: 0;
			border-radius: 4px;

			&.hidden
			{
				display: none;
			}
		}

		ControlSheetRow > .left
		{
			width: 150px;
			flex-shrink: 0;
			flex-grow: 0;
			padding: 6px;
			align-items: center;

			&.hidden
			{
				display: none;
			}
		}

		ControlSheetRow > .right
		{
			flex-grow: 1;
			flex-shrink: 1;
			flex-basis: 0px;
			min-width: 0px;
		}

		ControlSheetRow.wide
		{
			flex-direction: column;
		}

		ControlSheetRow.wide > .left
		{
			width: auto;
		}

		ControlSheetRow.wide > .right
		{
			flex-basis: auto;
		}

		// A long name wraps onto more lines and the row grows, rather than being cut down to its
		// first word - the tooltip still has it whole
		ControlSheetRow > .left > .title
		{
			flex-shrink: 1;
			min-width: 0px;
			font-size: 13px;
			line-height: 1.2;
			white-space: normal;
			overflow: hidden;
		}

		ControlSheetRow.wide > .left > .title
		{
			white-space: nowrap;
		}

		ControlSheetRow > .right > textentry,
		ControlSheetRow > .right numberentry
		{
			flex-grow: 1;
		}
		""";

	[Parameter]
	public SerializedProperty Property { get; set; }

	Panel _left;
	Label _title;
	Panel _right;

	InspectorVisibilityAttribute[] _visibilityAttributes;

	public ControlSheetRow()
	{
		_left = AddChild<Panel>( "left" );
		_title = _left.AddChild<Label>( "title" );
		_left.OnTooltip = BuildTooltip;

		_right = AddChild<Panel>( "right" );
	}

	/// <summary>
	/// Name, type, code name, description and default value - the editor's row tooltip.
	/// </summary>
	void BuildTooltip( Panel tooltip )
	{
		if ( Property is null ) return;

		var text = $"<strong>{Color( Encode( Property.DisplayName ), "#9CDCFE" )} - {RichType( Property.PropertyType )}</strong>";

		if ( Property.Name != Property.DisplayName )
			text += $"<br/>Property Name: {Color( Encode( Property.Name ), "#cce8ba" )}";

		if ( !string.IsNullOrWhiteSpace( Property.Description ) )
			text += $"<br/><br/>{Property.Description}";

		// Only worth showing when the type says something useful about itself
		var defaultValue = Property.GetDefault();
		if ( defaultValue is not null && defaultValue.GetType().GetMethod( "ToString", Type.EmptyTypes )?.DeclaringType == defaultValue.GetType() )
			text += $"<br/><br/>Default Value: {Encode( defaultValue.ToString() )}";

		var label = tooltip.AddChild<Label>();
		label.IsRich = true;
		label.Text = text;
	}

	static readonly Dictionary<Type, string> TypeAliases = new()
	{
		{ typeof( bool ), "bool" }, { typeof( byte ), "byte" }, { typeof( char ), "char" },
		{ typeof( decimal ), "decimal" }, { typeof( double ), "double" }, { typeof( float ), "float" },
		{ typeof( int ), "int" }, { typeof( long ), "long" }, { typeof( object ), "object" },
		{ typeof( sbyte ), "sbyte" }, { typeof( short ), "short" }, { typeof( string ), "string" },
		{ typeof( uint ), "uint" }, { typeof( ulong ), "ulong" }, { typeof( ushort ), "ushort" },
	};

	// Same colours as the editor's ControlSheetFormatter
	static string RichType( Type type )
	{
		if ( Nullable.GetUnderlyingType( type ) is { } underlying ) return $"{RichType( underlying )}?";
		if ( type.IsArray && type.GetElementType() is { } element ) return $"{RichType( element )}[]";
		if ( TypeAliases.TryGetValue( type, out var alias ) ) return Color( alias, "#569CD6" );

		var name = type.Name;
		var suffix = "";
		if ( type.IsGenericType && name.IndexOf( '`' ) is var index and > 0 )
		{
			name = name[..index];
			suffix = $"&lt;{string.Join( ",", type.GetGenericArguments().Select( RichType ) )}&gt;";
		}

		return $"{Color( name, type.IsValueType ? "#86C691" : "#4EC9B0" )}{suffix}";
	}

	static string Color( string text, string color ) => $"<span style=\"color: {color};\">{text}</span>";
	static string Encode( string text ) => System.Net.WebUtility.HtmlEncode( text );

	internal void Initialize( SerializedProperty prop )
	{
		Property = prop;

		_visibilityAttributes = Property.GetAttributes<InspectorVisibilityAttribute>()?.ToArray();
	}

	protected override void OnParametersSet()
	{
		if ( Property is null )
			return;

		_title.Text = Property?.DisplayName;

		_right.DeleteChildren();

		var c = BaseControl.CreateFor( Property );
		if ( c is null ) return;
		_right.AddChild( c );

		// Same rules as the editor's row
		var wide = c.IsWideMode;
		var hasLabel = c.IncludeLabel;
		if ( Property.TryGetAttribute<WideModeAttribute>( out var wideMode ) )
		{
			wide = true;
			hasLabel = c.IncludeLabel && wideMode.HasLabel;
		}

		SetClass( "wide", wide );
		_left.SetClass( "hidden", !hasLabel );
	}

	public override void Tick()
	{
		base.Tick();

		if ( _visibilityAttributes?.Length == 0 ) return;
		if ( Property.Parent is null ) return;

		bool hidden = _visibilityAttributes.All( x => x.TestCondition( Property.Parent ) );
		SetClass( "hidden", hidden );
	}
}
