using System;

namespace Facepunch.InteropGen;

/// <summary>
/// Maps a .def type name (e.g. "int", "CUtlString") to the <see cref="Arg"/> subclass that marshals it.
/// Discovered by reflection when the type registry is built.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = true )]
public class TypeNameAttribute : System.Attribute
{
	public string TypeName { get; private set; }

	public TypeNameAttribute( string match )
	{
		TypeName = match.ToLower();
	}
}
