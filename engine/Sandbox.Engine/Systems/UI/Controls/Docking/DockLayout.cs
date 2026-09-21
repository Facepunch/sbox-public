using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Sandbox.UI;

/// <summary>
/// Where a panel joins a docking layout.
/// </summary>
public enum DockPosition
{
	/// <summary>Join the target's tabs.</summary>
	Center,
	/// <summary>Split to the left of the target.</summary>
	Left,
	/// <summary>Split to the right of the target.</summary>
	Right,
	/// <summary>Split above the target.</summary>
	Top,
	/// <summary>Split below the target.</summary>
	Bottom
}

/// <summary>
/// A group or split owned by a docking layout.
/// </summary>
internal abstract class DockNode
{
	internal DockNode() { }
}

/// <summary>
/// An ordered, nonempty set of panel tabs.
/// </summary>
internal sealed class DockGroup : DockNode
{
	internal readonly List<string> Items = new();
	internal string Selected;

	/// <summary>Panel IDs in display order.</summary>
	public IReadOnlyList<string> Tabs { get; }

	/// <summary>The selected panel ID.</summary>
	public string ActiveId => Selected;

	internal DockGroup()
	{
		Tabs = Items.AsReadOnly();
	}
}

/// <summary>
/// Two docking regions separated by a splitter.
/// </summary>
internal sealed class DockSplit : DockNode
{
	internal DockNode FirstNode;
	internal DockNode SecondNode;
	internal float SplitFraction;

	/// <summary>The left or top region.</summary>
	public DockNode First => FirstNode;

	/// <summary>The right or bottom region.</summary>
	public DockNode Second => SecondNode;

	/// <summary>Whether regions are stacked top to bottom.</summary>
	public bool Vertical { get; }

	/// <summary>The first region's share, from 0.05 to 0.95.</summary>
	public float Fraction => SplitFraction;

	internal DockSplit( DockNode first, DockNode second, bool vertical, float fraction )
	{
		FirstNode = first;
		SecondNode = second;
		Vertical = vertical;
		SplitFraction = fraction;
	}
}

/// <summary>
/// A managed docking tree with validated edits and versioned persistence.
/// </summary>
internal sealed class DockLayout
{
	const int MaxDepth = 64;
	DockNode _root;

	/// <summary>The layout tree, or null when empty.</summary>
	public DockNode Root => _root;

	/// <summary>Raised once after a changed edit or successful restore.</summary>
	public event Action Changed;

	/// <summary>
	/// Inserts or moves a panel and selects it. Null targets use the first group for tabs, or the whole tree for edges.
	/// Fraction is the incoming region's share (0.05 to 0.95). Tab indices apply after removal; -1 appends,
	/// except within the same group where it only selects. Trees are limited to 64 levels.
	/// Invalid arguments or excessive depth throw without changing the layout.
	/// </summary>
	public void Dock( string id, string relativeTo = null, DockPosition position = DockPosition.Center, float fraction = 0.5f, int tabIndex = -1 )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( id );
		for ( var i = 0; i < id.Length; i++ )
		{
			if ( !char.IsSurrogate( id[i] ) ) continue;
			if ( !char.IsSurrogatePair( id, i ) )
				throw new ArgumentException( "Panel IDs must contain valid Unicode.", nameof( id ) );
			i++;
		}
		if ( !Enum.IsDefined( position ) )
			throw new ArgumentOutOfRangeException( nameof( position ) );
		ValidateFraction( fraction );

		var source = FindGroup( id );
		var target = relativeTo is null ? null : FindGroup( relativeTo );
		if ( relativeTo is not null && target is null )
			throw new ArgumentException( "The target panel is not docked.", nameof( relativeTo ) );

		if ( position == DockPosition.Center && target is null )
		{
			var first = _root;
			while ( first is DockSplit split ) first = split.First;
			target = first as DockGroup;
		}

		var count = position == DockPosition.Center ? (target?.Tabs.Count ?? 0) - (source is not null && source == target ? 1 : 0) : 0;
		if ( tabIndex < -1 || tabIndex > count )
			throw new ArgumentOutOfRangeException( nameof( tabIndex ) );

		if ( position == DockPosition.Center && source is not null && source == target )
		{
			var oldIndex = source.Items.IndexOf( id );
			if ( tabIndex == -1 || tabIndex == oldIndex )
			{
				Activate( id );
				return;
			}

			source.Items.RemoveAt( oldIndex );
			source.Items.Insert( tabIndex, id );
			source.Selected = id;
			Changed?.Invoke();
			return;
		}

		if ( position != DockPosition.Center )
		{
			if ( source is not null && source.Tabs.Count == 1 && (source == target || (relativeTo is null && source == _root)) )
				return;

			// Measure the resulting tree before detaching the source or collapsing its split.
			var removed = source?.Tabs.Count == 1 ? source : null;
			var depth = GetDepth( _root, removed, target );
			if ( target is null ) depth++;
			if ( depth > MaxDepth )
				throw new InvalidOperationException( "The docking layout is too deep." );
		}

		if ( source is not null ) Remove( source, id );

		if ( position == DockPosition.Center )
		{
			if ( target is null ) _root = target = new DockGroup();
			target.Items.Insert( tabIndex == -1 ? target.Items.Count : tabIndex, id );
			target.Selected = id;
		}
		else
		{
			var incoming = new DockGroup { Selected = id };
			incoming.Items.Add( id );
			DockNode region = target ?? _root;
			if ( region is null )
			{
				_root = incoming;
			}
			else
			{
				var before = position is DockPosition.Left or DockPosition.Top;
				var split = new DockSplit( before ? incoming : region, before ? region : incoming,
					position is DockPosition.Top or DockPosition.Bottom, before ? fraction : 1.0f - fraction );
				_root = Replace( _root, region, split );
			}
		}

		Changed?.Invoke();
	}

	/// <summary>
	/// Removes a docked panel, selecting the next tab or the previous last tab. Returns false if absent.
	/// </summary>
	public bool Close( string id )
	{
		var group = FindGroup( id );
		if ( group is null ) return false;
		Remove( group, id );
		Changed?.Invoke();
		return true;
	}

	/// <summary>Selects a docked panel. Returns false if absent or already selected.</summary>
	public bool Activate( string id )
	{
		var group = FindGroup( id );
		if ( group is null || group.Selected == id ) return false;
		group.Selected = id;
		Changed?.Invoke();
		return true;
	}

	/// <summary>Finds the group containing a panel, or null if absent.</summary>
	public DockGroup FindGroup( string id )
	{
		return FindGroup( _root, id );
	}

	static DockGroup FindGroup( DockNode node, string id )
	{
		if ( node is DockGroup group ) return group.Items.Contains( id ) ? group : null;
		if ( node is DockSplit split ) return FindGroup( split.First, id ) ?? FindGroup( split.Second, id );
		return null;
	}

	/// <summary>Sets an attached split's first-child share. Invalid splits or fractions throw without changes.</summary>
	public void SetFraction( DockSplit split, float fraction )
	{
		ValidateFraction( fraction );
		if ( split is null || !Contains( _root, split ) )
			throw new ArgumentException( "The split does not belong to this layout.", nameof( split ) );
		if ( split.Fraction == fraction ) return;
		split.SplitFraction = fraction;
		Changed?.Invoke();
	}

	static bool Contains( DockNode node, DockSplit target )
	{
		return node == target || node is DockSplit split && (Contains( split.First, target ) || Contains( split.Second, target ));
	}

	static void ValidateFraction( float fraction )
	{
		if ( !float.IsFinite( fraction ) || fraction < 0.05f || fraction > 0.95f )
			throw new ArgumentOutOfRangeException( nameof( fraction ), "Fraction must be between 0.05 and 0.95." );
	}

	void Remove( DockGroup group, string id )
	{
		var index = group.Items.IndexOf( id );
		group.Items.RemoveAt( index );
		if ( group.Items.Count == 0 )
		{
			group.Selected = null;
			_root = Replace( _root, group, null );
		}
		else if ( group.Selected == id )
		{
			group.Selected = group.Items[Math.Min( index, group.Items.Count - 1 )];
		}
	}

	static DockNode Replace( DockNode node, DockNode target, DockNode replacement )
	{
		if ( node == target ) return replacement;
		if ( node is not DockSplit split ) return node;
		split.FirstNode = Replace( split.First, target, replacement );
		split.SecondNode = Replace( split.Second, target, replacement );
		if ( split.First is null ) return split.Second;
		if ( split.Second is null ) return split.First;
		return split;
	}

	static int GetDepth( DockNode node, DockNode removed, DockNode wrapped )
	{
		if ( node is null || node == removed ) return 0;
		var depth = 1;
		if ( node is DockSplit split )
		{
			var first = GetDepth( split.First, removed, wrapped );
			var second = GetDepth( split.Second, removed, wrapped );
			depth = first == 0 ? second : second == 0 ? first : 1 + Math.Max( first, second );
		}
		return node == wrapped ? depth + 1 : depth;
	}

	/// <summary>Serializes the entire layout, including tab order and selection, as version 1 JSON.</summary>
	public string Save()
	{
		using var stream = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( stream, new JsonWriterOptions { MaxDepth = MaxDepth + 4 } ) )
		{
			writer.WriteStartObject();
			writer.WriteNumber( "version", 1 );
			writer.WritePropertyName( "root" );
			WriteNode( writer, _root );
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString( stream.ToArray() );
	}

	static void WriteNode( Utf8JsonWriter writer, DockNode node )
	{
		if ( node is null )
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStartObject();
		if ( node is DockGroup group )
		{
			writer.WriteString( "type", "group" );
			writer.WriteStartArray( "tabs" );
			foreach ( var id in group.Tabs ) writer.WriteStringValue( id );
			writer.WriteEndArray();
			writer.WriteString( "activeId", group.ActiveId );
		}
		else if ( node is DockSplit split )
		{
			writer.WriteString( "type", "split" );
			writer.WriteBoolean( "vertical", split.Vertical );
			writer.WriteNumber( "fraction", split.Fraction );
			writer.WritePropertyName( "first" );
			WriteNode( writer, split.First );
			writer.WritePropertyName( "second" );
			WriteNode( writer, split.Second );
		}
		writer.WriteEndObject();
	}

	/// <summary>
	/// Replaces the layout only if all JSON, structure, version and IDs are valid. Returns false without changes on invalid data.
	/// Known IDs may be absent from the saved layout; omitted panels remain closed. Maximum tree depth is 64.
	/// </summary>
	public bool Restore( string json, IEnumerable<string> knownIds )
	{
		if ( json is null || knownIds is null ) return false;
		var known = new HashSet<string>( knownIds, StringComparer.Ordinal );
		DockNode root;
		try
		{
			using var document = JsonDocument.Parse( json, new JsonDocumentOptions { MaxDepth = MaxDepth + 4 } );
			var data = document.RootElement;
			RequireProperties( data, "version", "root" );
			var version = data.GetProperty( "version" );
			if ( version.ValueKind != JsonValueKind.Number || !version.TryGetInt32( out var number ) || number != 1 )
				return false;
			var node = data.GetProperty( "root" );
			root = node.ValueKind == JsonValueKind.Null ? null : ReadNode( node, known, new HashSet<string>( StringComparer.Ordinal ), 1 );
		}
		catch ( Exception exception ) when ( exception is JsonException or InvalidOperationException or ArgumentException )
		{
			// Malformed Unicode can fail during string decoding rather than JSON parsing.
			return false;
		}

		_root = root;
		Changed?.Invoke();
		return true;
	}

	static DockNode ReadNode( JsonElement data, HashSet<string> known, HashSet<string> used, int depth )
	{
		if ( depth > MaxDepth || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty( "type", out var type ) )
			throw new JsonException();
		if ( type.ValueKind != JsonValueKind.String ) throw new JsonException();

		if ( type.GetString() == "group" )
		{
			RequireProperties( data, "type", "tabs", "activeId" );
			var tabs = data.GetProperty( "tabs" );
			var active = data.GetProperty( "activeId" );
			if ( tabs.ValueKind != JsonValueKind.Array || active.ValueKind != JsonValueKind.String ) throw new JsonException();
			var group = new DockGroup { Selected = active.GetString() };
			foreach ( var tab in tabs.EnumerateArray() )
			{
				if ( tab.ValueKind != JsonValueKind.String ) throw new JsonException();
				var id = tab.GetString();
				if ( string.IsNullOrWhiteSpace( id ) || !known.Contains( id ) || !used.Add( id ) ) throw new JsonException();
				group.Items.Add( id );
			}
			if ( !group.Items.Contains( group.Selected ) ) throw new JsonException();
			return group;
		}

		if ( type.GetString() != "split" ) throw new JsonException();
		RequireProperties( data, "type", "vertical", "fraction", "first", "second" );
		var vertical = data.GetProperty( "vertical" );
		var fraction = data.GetProperty( "fraction" );
		if ( vertical.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
			|| fraction.ValueKind != JsonValueKind.Number || !fraction.TryGetSingle( out var value )
			|| !float.IsFinite( value ) || value < 0.05f || value > 0.95f ) throw new JsonException();
		return new DockSplit( ReadNode( data.GetProperty( "first" ), known, used, depth + 1 ),
			ReadNode( data.GetProperty( "second" ), known, used, depth + 1 ), vertical.GetBoolean(), value );
	}

	static void RequireProperties( JsonElement data, params string[] names )
	{
		if ( data.ValueKind != JsonValueKind.Object ) throw new JsonException();
		var remaining = new HashSet<string>( names, StringComparer.Ordinal );
		foreach ( var property in data.EnumerateObject() )
		{
			if ( !remaining.Remove( property.Name ) ) throw new JsonException();
		}
		if ( remaining.Count != 0 ) throw new JsonException();
	}
}
