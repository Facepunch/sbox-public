namespace Sandbox.UI.NodeGraph;

/// <summary>
/// The editor theme colours the node graph draws with.
/// </summary>
internal static class NodeTheme
{
	public static readonly Color WindowBackground = Color.Parse( "#181818" ).Value;
	public static readonly Color ControlBackground = Color.Parse( "#181818" ).Value;
	public static readonly Color TextControl = Color.Parse( "#FFFFFF" ).Value;
	public static readonly Color Red = Color.Parse( "#FB5A5A" ).Value;
	public static readonly Color Blue = Color.Parse( "#2E70EA" ).Value;

	/// <summary>
	/// Rich text tooltip for a node or plug.
	/// </summary>
	public static string FormatToolTip( string name, string description, Type type = null, string error = null )
	{
		var tooltip = Colored( System.Net.WebUtility.HtmlEncode( name ), "#9CDCFE" );

		if ( type is not null )
		{
			tooltip += $": {RichType( type )}";
		}

		var desc = description ?? "No description given.";
		tooltip += desc.StartsWith( "<br/>", StringComparison.OrdinalIgnoreCase ) ? desc : $"<br/>{desc}";

		foreach ( var message in error?.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries ) ?? [] )
		{
			tooltip += $"<br/><span style=\"font-size: 11px; color: {Red.Hex};\">{message}</span>";
		}

		return tooltip;
	}

	static string Colored( string text, string color ) => $"<span style=\"color: {color};\">{text}</span>";

	// Same table and colours as the editor's ControlSheetFormatter
	static readonly Dictionary<Type, string> TypeAliases = new()
	{
		{ typeof( bool ), "bool" }, { typeof( byte ), "byte" }, { typeof( char ), "char" },
		{ typeof( decimal ), "decimal" }, { typeof( double ), "double" }, { typeof( float ), "float" },
		{ typeof( int ), "int" }, { typeof( long ), "long" }, { typeof( object ), "object" },
		{ typeof( sbyte ), "sbyte" }, { typeof( short ), "short" }, { typeof( string ), "string" },
		{ typeof( uint ), "uint" }, { typeof( ulong ), "ulong" }, { typeof( ushort ), "ushort" },
	};

	/// <summary>
	/// A type name coloured like the editor's property tooltips.
	/// </summary>
	public static string RichType( Type type )
	{
		if ( Nullable.GetUnderlyingType( type ) is { } underlying ) return $"{RichType( underlying )}?";
		if ( type.IsArray && type.GetElementType() is { } element ) return $"{RichType( element )}[]";
		if ( TypeAliases.TryGetValue( type, out var alias ) ) return Colored( alias, "#569CD6" );

		var name = type.Name;
		var suffix = "";
		if ( type.IsGenericType && name.IndexOf( '`' ) is var index and > 0 )
		{
			name = name[..index];
			suffix = $"&lt;{string.Join( ",", type.GetGenericArguments().Select( RichType ) )}&gt;";
		}

		return $"{Colored( name, type.IsValueType ? "#86C691" : "#4EC9B0" )}{suffix}";
	}
}
