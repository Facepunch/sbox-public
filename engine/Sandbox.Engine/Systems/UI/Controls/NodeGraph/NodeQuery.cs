using System.Globalization;
using System.Text.RegularExpressions;

namespace Sandbox.UI.NodeGraph;

public enum FilterModifier
{
	None,
	Not
}

/// <summary>
/// One word of a node menu filter. A leading <c>-</c> excludes paths containing it; quotes keep
/// separators inside one word.
/// </summary>
public record struct FilterPart( FilterModifier Modifier, string Value )
{
	private static Regex Pattern { get; } =
		new( @"(?<modifier>-?)(?<value>(?:[^\s.,;\\\/""_-]|\\[\\""]|""(?:(?:[^\\""]|\\[\\""])+)"")+)" );

	private static FilterModifier ParseModifier( string value )
	{
		return value switch
		{
			"-" => FilterModifier.Not,
			_ => FilterModifier.None
		};
	}

	private static Regex EscapedChars { get; } = new( @"\\[\\""]|""" );

	private static string FormatValue( string value )
	{
		return EscapedChars.Replace( value, x => x.Value switch
		{
			"\\\\" => "\\",
			"\\\"" => "\"",
			_ => ""
		} ).Trim();
	}

	public static IReadOnlyList<FilterPart> Parse( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
		{
			return Array.Empty<FilterPart>();
		}

		return Pattern.Matches( value )
			.Select( x => new FilterPart(
				ParseModifier( x.Groups["modifier"].Value ),
				FormatValue( x.Groups["value"].Value ) ) )
			.Where( x => !string.IsNullOrEmpty( x.Value ) )
			.ToArray();
	}
}

/// <summary>
/// What the node menu is looking for: types that connect to <see cref="Plug"/>, when set, and
/// whose path matches every part of <see cref="Filter"/>.
/// </summary>
public record struct NodeQuery( IGraph Graph, IPlug Plug, IReadOnlyList<FilterPart> Filter )
{
	public bool IsEmpty => Plug is null && Filter.Count == 0;

	public NodeQuery( IGraph graph, IPlug plug, string filter = null )
		: this( graph, plug, FilterPart.Parse( filter ) )
	{
	}

	public bool Matches( IReadOnlyList<NodePathElement> path )
	{
		return GetScore( path ) is not null;
	}

	/// <summary>
	/// Check if <paramref name="str"/> contains <paramref name="value"/>, ignoring case and white space.
	/// </summary>
	private static bool FuzzyContains( string str, string value )
	{
		return CultureInfo.InvariantCulture.CompareInfo.IndexOf( str, value, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols ) >= 0;
	}

	/// <summary>
	/// Null when <paramref name="path"/> doesn't match the filter, else higher for more matched
	/// characters and lower for longer names.
	/// </summary>
	public int? GetScore( IReadOnlyList<NodePathElement> path )
	{
		// Do we match all filter parts?

		foreach ( var part in Filter )
		{
			var match = false;

			foreach ( var elem in path )
			{
				if ( elem.IsHeading ) continue;
				if ( FuzzyContains( elem.Name, part.Value ) )
				{
					match = true;
					break;
				}
			}

			var shouldMatch = part.Modifier != FilterModifier.Not;

			if ( match != shouldMatch )
			{
				return null;
			}
		}

		// Calculate score, higher for more matched characters, lower for longer names

		var score = 0;

		foreach ( var elem in path )
		{
			if ( elem.IsHeading ) continue;

			foreach ( var part in Filter )
			{
				if ( part.Modifier == FilterModifier.Not ) continue;
				if ( !FuzzyContains( elem.Name, part.Value ) ) continue;

				score += part.Value.Length * part.Value.Length * 1_000 / (elem.Name.Length * elem.Name.Length);
			}
		}

		return score;
	}
}

public static class NodeQueryExtensions
{
	/// <summary>
	/// The types matching <paramref name="query"/>.
	/// </summary>
	public static IEnumerable<INodeType> Filter( this IEnumerable<INodeType> nodes, NodeQuery query )
	{
		return nodes.Where( x => x.Matches( query ) ).ToArray();
	}
}
