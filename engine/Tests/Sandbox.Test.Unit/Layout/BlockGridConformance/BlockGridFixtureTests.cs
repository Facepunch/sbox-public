using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Sandbox.Layout;

namespace LayoutTests.BlockGridConformance;

/// <summary>
/// Runs Taffy's Chrome-generated XML layout fixtures (Data/Layout/layout-*.xml) against Sandbox.Layout. Each
/// fixture describes a styled tree plus the rects Chrome produced for it.
/// Source: https://github.com/DioxusLabs/taffy/tree/ac2b86929d35b7e0f1d24919595b89b4ce89baa4/tests/xml (MIT).
/// </summary>
[TestClass]
public class BlockGridFixtureTests
{
	private const float Tolerance = 0.1f;

	private static readonly Dictionary<string, XElement> _cache = new();

	// Each known failure is pinned to its exact mismatch report. A passing fixture or any changed mismatch fails,
	// so an existing failure cannot gain more wrong results without review.
	private static readonly Lazy<Dictionary<string, string>> _knownFailures = new( () =>
	{
		var path = Path.Combine( DataDirectory, "layout-known-failures.txt" );
		var signatures = new Dictionary<string, string>( StringComparer.Ordinal );
		if ( !File.Exists( path ) ) return signatures;

		foreach ( var (line, index) in File.ReadAllLines( path ).Select( ( value, index ) => (value, index) ) )
		{
			var trimmed = line.Trim();
			if ( trimmed.Length == 0 || trimmed.StartsWith( '#' ) ) continue;

			var parts = trimmed.Split( '|', StringSplitOptions.TrimEntries );
			if ( parts.Length != 2 || parts[0].Length == 0 || parts[1].Length != 64 || !parts[1].All( Uri.IsHexDigit ) )
			{
				throw new InvalidDataException( $"Invalid known-failure entry at {path}:{index + 1}; expected 'fixture | SHA-256'." );
			}

			if ( !signatures.TryAdd( parts[0], parts[1].ToUpperInvariant() ) )
			{
				throw new InvalidDataException( $"Duplicate known-failure fixture '{parts[0]}' at {path}:{index + 1}." );
			}
		}

		var fixtureNames = Directory.EnumerateFiles( DataDirectory, "layout-*.xml" )
			.SelectMany( file => XElement.Load( file ).Elements( "test" ) )
			.Select( test => (string)test.Attribute( "name" ) )
			.ToHashSet( StringComparer.Ordinal );
		var missing = signatures.Keys.Where( name => !fixtureNames.Contains( name ) ).ToList();
		if ( missing.Count > 0 )
		{
			throw new InvalidDataException( "Known-failure fixtures not found: " + string.Join( ", ", missing ) );
		}

		return signatures;
	} );

	private static string DataDirectory
	{
		get
		{
			var dir = Path.Combine( AppContext.BaseDirectory, "Data", "Layout" );
			if ( Directory.Exists( dir ) ) return dir;

			// Running from source (e.g. the scratch harness): walk up looking for the test project's Data folder.
			var probe = AppContext.BaseDirectory;
			for ( int i = 0; i < 6 && probe is not null; i++ )
			{
				var candidate = Path.Combine( probe, "Data", "Layout" );
				if ( Directory.Exists( candidate ) ) return candidate;
				probe = Path.GetDirectoryName( probe );
			}

			return dir;
		}
	}

	private static IEnumerable<object[]> Cases( string group )
	{
		var path = Path.Combine( DataDirectory, $"layout-{group}.xml" );
		if ( !File.Exists( path ) ) yield break;

		XElement root;
		lock ( _cache )
		{
			if ( !_cache.TryGetValue( group, out root ) )
			{
				root = XElement.Load( path );
				_cache[group] = root;
			}
		}

		foreach ( var test in root.Elements( "test" ) )
		{
			yield return new object[] { group, (string)test.Attribute( "name" ) };
		}
	}

	public static IEnumerable<object[]> BlockCases() => Cases( "block" );
	public static IEnumerable<object[]> GridCases() => Cases( "grid" );
	public static IEnumerable<object[]> BlockGridCases() => Cases( "blockgrid" );
	public static IEnumerable<object[]> GridFlexCases() => Cases( "gridflex" );

	public static string CaseName( System.Reflection.MethodInfo method, object[] data ) => $"{data[1]}";

	[TestMethod]
	[DynamicData( nameof( BlockCases ), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof( CaseName ) )]
	public void Block( string group, string name ) => Run( group, name );

	[TestMethod]
	[DynamicData( nameof( GridCases ), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof( CaseName ) )]
	public void Grid( string group, string name ) => Run( group, name );

	[TestMethod]
	[DynamicData( nameof( BlockGridCases ), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof( CaseName ) )]
	public void BlockGrid( string group, string name ) => Run( group, name );

	[TestMethod]
	[DynamicData( nameof( GridFlexCases ), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof( CaseName ) )]
	public void GridFlex( string group, string name ) => Run( group, name );

	// -----------------------------------------------------------------------------------------------

	private static void Run( string group, string name )
	{
		XElement root;
		lock ( _cache ) root = _cache[group];

		var test = root.Elements( "test" ).First( t => (string)t.Attribute( "name" ) == name );
		var useRounding = (string)test.Attribute( "use-rounding" ) is not "false";
		var viewport = test.Element( "viewport" );
		var input = test.Element( "input" ).Elements().First();
		var expected = test.Element( "expectations" ).Elements().First();

		var unsupported = new List<string>();

		var ownerWidth = ParseViewport( (string)viewport?.Attribute( "width" ), unsupported );
		var ownerHeight = ParseViewport( (string)viewport?.Attribute( "height" ), unsupported );

		var rootNode = Build( input, unsupported );
		var isKnownFailure = _knownFailures.Value.TryGetValue( name, out var expectedSignature );
		if ( unsupported.Count > 0 )
		{
			if ( isKnownFailure ) Assert.Fail( $"Known failure {name} is no longer compared: " + string.Join( ", ", unsupported.Distinct() ) );
			Assert.Inconclusive( "Fixture uses unsupported features: " + string.Join( ", ", unsupported.Distinct() ) );
		}

		// A definite viewport is *available space* for the root (it shrink-wraps inside it, like Chrome's
		// test harness), not a size to stretch to. Express that as a max size.
		if ( !float.IsNaN( ownerWidth ) && rootNode.Style.Width.IsAuto && rootNode.Style.MaxWidth.IsUndefined ) rootNode.Style.MaxWidth = ownerWidth;
		if ( !float.IsNaN( ownerHeight ) && rootNode.Style.Height.IsAuto && rootNode.Style.MaxHeight.IsUndefined ) rootNode.Style.MaxHeight = ownerHeight;

		rootNode.CalculateLayout( ownerWidth, ownerHeight, Direction.LTR );

		var failures = new StringBuilder();
		Compare( rootNode, expected, "root", failures, useRounding );

		if ( failures.Length > 0 )
		{
			var normalizedFailures = failures.ToString().ReplaceLineEndings( "\n" );
			var actualSignature = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( normalizedFailures + "\n" ) ) );
			var report = $"{name}\n{failures}\nINPUT:\n{input}\nACTUAL:\n{Dump( rootNode, "" )}";
			if ( isKnownFailure && actualSignature == expectedSignature )
			{
				Assert.Inconclusive( $"Known failure {actualSignature} (Data/Layout/layout-known-failures.txt)\n" + report );
			}

			if ( isKnownFailure )
			{
				Assert.Fail( $"{name} mismatch changed; review it and update the baseline only if the change is intentional.\nExpected signature: {expectedSignature}\nActual signature:   {actualSignature}\n{failures}" );
			}

			Assert.Fail( report );
		}

		if ( isKnownFailure )
		{
			Assert.Fail( $"{name} now passes - remove it from Data/Layout/layout-known-failures.txt" );
		}
	}

	private static float ParseViewport( string value, List<string> unsupported )
	{
		if ( value is null || value == "max-content" ) return float.NaN;
		if ( value == "min-content" )
		{
			unsupported.Add( "min-content viewport" );
			return float.NaN;
		}

		return ParseFloat( value.Replace( "px", "" ) );
	}

	private static float ParseFloat( string s ) => float.Parse( s, NumberStyles.Float, CultureInfo.InvariantCulture );

	private static void Compare( LayoutNode node, XElement expected, string path, StringBuilder failures, bool round )
	{
		var rect = FixtureGeometry.GetRect( node, round );
		Check( path, "x", ParseFloat( (string)expected.Attribute( "x" ) ), rect.Left, failures );
		Check( path, "y", ParseFloat( (string)expected.Attribute( "y" ) ), rect.Top, failures );
		Check( path, "width", ParseFloat( (string)expected.Attribute( "width" ) ), rect.Width, failures );
		Check( path, "height", ParseFloat( (string)expected.Attribute( "height" ) ), rect.Height, failures );

		var expectedChildren = expected.Elements().ToList();
		if ( expectedChildren.Count != node.ChildCount )
		{
			failures.AppendLine( FormattableString.Invariant( $"  {path}.child-count: expected {expectedChildren.Count}, got {node.ChildCount}" ) );
		}

		for ( int i = 0; i < Math.Min( expectedChildren.Count, node.ChildCount ); i++ )
		{
			Compare( node.GetChild( i ), expectedChildren[i], $"{path}/{i}", failures, round );
		}
	}

	private static void Check( string path, string what, float expected, float actual, StringBuilder failures )
	{
		if ( float.IsNaN( actual ) || MathF.Abs( expected - actual ) > Tolerance )
		{
			failures.AppendLine( FormattableString.Invariant( $"  {path}.{what}: expected {expected:R}, got {actual:R}" ) );
		}
	}

	private static string Dump( LayoutNode node, string indent )
	{
		var sb = new StringBuilder();
		sb.AppendLine( $"{indent}[x: {node.LayoutLeft} y: {node.LayoutTop} w: {node.LayoutWidth} h: {node.LayoutHeight}]" );
		foreach ( var child in node.Children ) sb.Append( Dump( child, indent + "  " ) );
		return sb.ToString();
	}

	// -----------------------------------------------------------------------------------------------
	// Tree construction
	// -----------------------------------------------------------------------------------------------

	private static LayoutNode Build( XElement element, List<string> unsupported )
	{
		var node = new LayoutNode();
		ApplyStyle( node.Style, element, unsupported );

		var children = element.Elements().ToList();
		if ( children.Count > 0 )
		{
			foreach ( var child in children ) node.AddChild( Build( child, unsupported ) );
		}
		else
		{
			var text = element.Value?.Trim();
			if ( !string.IsNullOrEmpty( text ) )
			{
				var writingMode = (string)element.Attribute( "writing-mode" );
				if ( writingMode is not null && writingMode.Contains( "vertical" ) ) unsupported.Add( "vertical writing-mode" );
				node.Context = text;
				node.MeasureFunc = AhemMeasure;
			}
		}

		return node;
	}

	/// <summary>
	/// Taffy's test "font": every glyph is 10x10 and lines may only break at zero-width spaces.
	/// </summary>
	private static LayoutSize AhemMeasure( LayoutNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode )
	{
		const char zws = '​';
		const float glyph = 10.0f;

		var lines = ((string)node.Context).Split( zws );
		int minLineLength = lines.Max( l => l.Length );
		int maxLineLength = lines.Sum( l => l.Length );

		float inlineSize;
		switch ( widthMode )
		{
			case MeasureMode.Exactly: inlineSize = width; break;
			case MeasureMode.AtMost: inlineSize = MathF.Min( width, maxLineLength * glyph ); break;
			case MeasureMode.MinContent: inlineSize = minLineLength * glyph; break;
			default: inlineSize = maxLineLength * glyph; break;
		}
		if ( widthMode != MeasureMode.Exactly ) inlineSize = MathF.Max( inlineSize, minLineLength * glyph );

		float blockSize;
		if ( heightMode == MeasureMode.Exactly )
		{
			blockSize = height;
		}
		else
		{
			int inlineLineLength = (int)MathF.Floor( inlineSize / glyph );
			int lineCount = 1;
			int currentLineLength = 0;
			foreach ( var line in lines )
			{
				if ( currentLineLength + line.Length > inlineLineLength )
				{
					if ( currentLineLength > 0 ) lineCount++;
					currentLineLength = line.Length;
				}
				else
				{
					currentLineLength += line.Length;
				}
			}
			blockSize = lineCount * glyph;
		}

		return new LayoutSize( inlineSize, blockSize );
	}

	private static void ApplyStyle( LayoutStyle style, XElement e, List<string> unsupported )
	{
		foreach ( var attr in e.Attributes() )
		{
			var name = attr.Name.LocalName;
			var value = attr.Value.Trim();

			switch ( name )
			{
				case "display":
					style.Display = value switch
					{
						"grid" => Display.Grid,
						"block" => Display.Block,
						"flow-root" => Display.Block,
						"flex" => Display.Flex,
						"none" => Display.None,
						"contents" => Display.Contents,
						_ => Unsupported( unsupported, $"display: {value}", Display.Block ),
					};
					if ( value == "flow-root" ) unsupported.Add( "display: flow-root" );
					break;

				case "direction":
					style.Direction = value == "rtl" ? Direction.RTL : Direction.LTR;
					break;

				case "box-sizing":
					style.BoxSizing = value == "content-box" ? BoxSizing.ContentBox : BoxSizing.BorderBox;
					break;

				case "position":
					style.PositionType = value switch { "absolute" => PositionType.Absolute, "relative" => PositionType.Relative, _ => PositionType.Static };
					break;

				case "overflow-x":
				case "overflow-y":
					{
						var overflow = value switch { "scroll" => Overflow.Scroll, "hidden" => Overflow.Hidden, "clip" => Overflow.Visible, _ => Overflow.Visible };
						// Only one overflow value; scroll wins over hidden wins over visible.
						if ( overflow > style.Overflow ) style.Overflow = overflow;
						break;
					}

				case "scrollbar-width":
					if ( ParseFloat( value ) != 0 ) unsupported.Add( "scrollbar gutters" );
					break;

				case "width": style.Width = Length( value, unsupported ); break;
				case "height": style.Height = Length( value, unsupported ); break;
				case "min-width": style.MinWidth = Length( value, unsupported ); break;
				case "min-height": style.MinHeight = Length( value, unsupported ); break;
				case "max-width": style.MaxWidth = Length( value, unsupported ); break;
				case "max-height": style.MaxHeight = Length( value, unsupported ); break;

				case "top": style.SetPosition( Edge.Top, Length( value, unsupported ) ); break;
				case "left": style.SetPosition( Edge.Left, Length( value, unsupported ) ); break;
				case "right": style.SetPosition( Edge.Right, Length( value, unsupported ) ); break;
				case "bottom": style.SetPosition( Edge.Bottom, Length( value, unsupported ) ); break;

				case "margin-top": style.SetMargin( Edge.Top, Length( value, unsupported ) ); break;
				case "margin-left": style.SetMargin( Edge.Left, Length( value, unsupported ) ); break;
				case "margin-right": style.SetMargin( Edge.Right, Length( value, unsupported ) ); break;
				case "margin-bottom": style.SetMargin( Edge.Bottom, Length( value, unsupported ) ); break;

				case "padding-top": style.SetPadding( Edge.Top, Length( value, unsupported ) ); break;
				case "padding-left": style.SetPadding( Edge.Left, Length( value, unsupported ) ); break;
				case "padding-right": style.SetPadding( Edge.Right, Length( value, unsupported ) ); break;
				case "padding-bottom": style.SetPadding( Edge.Bottom, Length( value, unsupported ) ); break;

				case "border-top": style.SetBorder( Edge.Top, Length( value, unsupported ) ); break;
				case "border-left": style.SetBorder( Edge.Left, Length( value, unsupported ) ); break;
				case "border-right": style.SetBorder( Edge.Right, Length( value, unsupported ) ); break;
				case "border-bottom": style.SetBorder( Edge.Bottom, Length( value, unsupported ) ); break;

				case "column-gap": style.SetGap( Gutter.Column, Length( value, unsupported ) ); break;
				case "row-gap": style.SetGap( Gutter.Row, Length( value, unsupported ) ); break;

				case "aspect-ratio": style.AspectRatio = ParseFloat( value ); break;

				case "flex-direction":
					style.FlexDirection = value switch { "row" => FlexDirection.Row, "row-reverse" => FlexDirection.RowReverse, "column" => FlexDirection.Column, _ => FlexDirection.ColumnReverse };
					break;
				case "flex-wrap":
					style.FlexWrap = value switch { "wrap" => Wrap.Wrap, "wrap-reverse" => Wrap.WrapReverse, _ => Wrap.NoWrap };
					break;
				case "flex-grow": style.FlexGrow = ParseFloat( value ); break;
				case "flex-shrink": style.FlexShrink = ParseFloat( value ); break;
				case "flex-basis": style.FlexBasis = Length( value, unsupported ); break;

				case "align-items": style.AlignItems = Alignment( value, unsupported ); break;
				case "align-self": style.AlignSelf = Alignment( value, unsupported ); break;
				case "align-content": style.AlignContent = Alignment( value, unsupported ); break;
				case "justify-items": style.JustifyItems = Alignment( value, unsupported ); break;
				case "justify-self": style.JustifySelf = Alignment( value, unsupported ); break;
				case "justify-content": style.JustifyContent = Justification( value, unsupported ); break;

				case "grid-template-columns":
					if ( GridParser.TryParseTrackList( value, out var cols ) ) style.GridTemplateColumns = cols;
					else unsupported.Add( $"grid-template-columns: {value}" );
					break;
				case "grid-template-rows":
					if ( GridParser.TryParseTrackList( value, out var rows ) ) style.GridTemplateRows = rows;
					else unsupported.Add( $"grid-template-rows: {value}" );
					break;
				case "grid-auto-columns":
					if ( GridParser.TryParseTrackSizes( value, out var autoCols ) ) style.GridAutoColumns = autoCols;
					else unsupported.Add( $"grid-auto-columns: {value}" );
					break;
				case "grid-auto-rows":
					if ( GridParser.TryParseTrackSizes( value, out var autoRows ) ) style.GridAutoRows = autoRows;
					else unsupported.Add( $"grid-auto-rows: {value}" );
					break;
				case "grid-auto-flow":
					if ( GridParser.TryParseAutoFlow( value, out var flow ) ) style.GridAutoFlow = flow;
					else unsupported.Add( $"grid-auto-flow: {value}" );
					break;
				case "grid-column-start": style.GridColumnStart = Placement( value, unsupported ); break;
				case "grid-column-end": style.GridColumnEnd = Placement( value, unsupported ); break;
				case "grid-row-start": style.GridRowStart = Placement( value, unsupported ); break;
				case "grid-row-end": style.GridRowEnd = Placement( value, unsupported ); break;

				case "text-align":
				case "float":
				case "clear":
				case "contain":
					unsupported.Add( name );
					break;

				case "writing-mode":
					break;

				default:
					unsupported.Add( $"attribute {name}" );
					break;
			}
		}
	}

	private static T Unsupported<T>( List<string> unsupported, string what, T fallback )
	{
		unsupported.Add( what );
		return fallback;
	}

	private static StyleLength Length( string value, List<string> unsupported )
	{
		if ( value == "auto" ) return StyleLength.Auto;
		if ( value.EndsWith( "%" ) ) return StyleLength.Percent( ParseFloat( value[..^1] ) );
		if ( value.EndsWith( "px" ) ) return StyleLength.Points( ParseFloat( value[..^2] ) );
		if ( float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f ) ) return StyleLength.Points( f );

		unsupported.Add( $"length {value}" );
		return StyleLength.Auto;
	}

	private static Align Alignment( string value, List<string> unsupported )
	{
		value = value.Replace( "safe ", "" ).Replace( "unsafe ", "" );
		switch ( value )
		{
			case "start": case "flex-start": case "self-start": case "left": return Align.FlexStart;
			case "end": case "flex-end": case "self-end": case "right": return Align.FlexEnd;
			case "center": return Align.Center;
			case "stretch": case "normal": return Align.Stretch;
			case "baseline": return Align.Baseline;
			case "space-between": return Align.SpaceBetween;
			case "space-around": return Align.SpaceAround;
			case "space-evenly": return Align.SpaceEvenly;
			case "auto": return Align.Auto;
			default:
				unsupported.Add( $"alignment {value}" );
				return Align.Auto;
		}
	}

	private static Justify Justification( string value, List<string> unsupported )
	{
		value = value.Replace( "safe ", "" ).Replace( "unsafe ", "" );
		switch ( value )
		{
			case "start": case "flex-start": case "left": case "normal": return Justify.FlexStart;
			case "end": case "flex-end": case "right": return Justify.FlexEnd;
			case "center": return Justify.Center;
			case "stretch": return Justify.Stretch;
			case "space-between": return Justify.SpaceBetween;
			case "space-around": return Justify.SpaceAround;
			case "space-evenly": return Justify.SpaceEvenly;
			default:
				unsupported.Add( $"justification {value}" );
				return Justify.FlexStart;
		}
	}

	private static GridPlacement Placement( string value, List<string> unsupported )
	{
		if ( GridParser.TryParsePlacement( value, out var placement ) ) return placement;
		unsupported.Add( $"placement {value}" );
		return GridPlacement.Auto;
	}
}
