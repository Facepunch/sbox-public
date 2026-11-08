using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Sandbox.Mounting.Source1;

/// <summary>
/// An immutable KeyValues entry. A null Value denotes a block; Children preserve order and
/// duplicate names, including repeated proxy types. Values keeps the existing last-scalar-wins view.
/// </summary>
internal sealed class VmtNode
{
	private static readonly IReadOnlyList<VmtNode> EmptyChildren = Array.AsReadOnly( Array.Empty<VmtNode>() );
	private static readonly IReadOnlyDictionary<string, string> EmptyValues =
		new ReadOnlyDictionary<string, string>( new Dictionary<string, string>() );

	public string Name { get; }
	public string Value { get; }
	public bool IsBlock => Value == null;
	public IReadOnlyList<VmtNode> Children { get; }
	public IReadOnlyDictionary<string, string> Values { get; }

	internal VmtNode( string name, string value, IEnumerable<VmtNode> children )
	{
		Name = name;
		Value = value;
		if ( value != null )
		{
			Children = EmptyChildren;
			Values = EmptyValues;
			return;
		}
		Children = new List<VmtNode>( children ).AsReadOnly();
		var values = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var child in Children )
		{
			if ( !child.IsBlock ) values[child.Name] = child.Value;
		}
		Values = new ReadOnlyDictionary<string, string>( values );
	}

	public VmtNode GetBlock( string name )
	{
		foreach ( var child in Children )
		{
			if ( child.IsBlock && child.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) )
				return child;
		}
		return null;
	}

}

internal enum VmtDiagnosticKind
{
	UnknownCondition,
	InvalidCondition,
	UnsupportedDirective
}

internal sealed record VmtDiagnostic( string Path, int Line, VmtDiagnosticKind Kind, string Message );
