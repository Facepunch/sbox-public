namespace Sandbox.UI.NodeGraph;

/// <summary>
/// The node graph's colours and shapes, for drawing custom nodes and editors that match.
/// </summary>
public static class NodeTheme
{
	public static readonly Color Red = Color.Parse( "#FB5A5A" ).Value;
	public static readonly Color Canvas = Color.Parse( "#141414" ).Value;
	public static readonly Color Accent = Color.Parse( "#4C8DFF" ).Value;
	public static readonly Color Text = Color.Parse( "#ECECEC" ).Value;
	public static readonly Color TextMuted = Color.Parse( "#8E8E8E" ).Value;
	public static readonly Color FieldBackground = Color.Parse( "#1A1A1A" ).Value;
	public static readonly Color Outline = Color.Gray.Darken( 0.4f );
	public static readonly Color OutlineHover = Color.White.Darken( 0.25f );

	/// <summary>
	/// Node corner radius, in node units.
	/// </summary>
	public const float Radius = 4f;

	/// <summary>
	/// A node's body from its tint: dark, keeping a hint of the tint.
	/// </summary>
	public static Color Body( Color tint ) => tint.Darken( 0.72f ).WithAlpha( 1f );

	/// <summary>
	/// The rounded field an inline editor draws in its local <paramref name="rect"/>.
	/// </summary>
	public static Rect FieldRect( Rect rect ) => rect.Shrink( 2f, 3f );

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
