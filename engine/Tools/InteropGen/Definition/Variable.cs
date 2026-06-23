using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen;

/// <summary>
/// A member field exposed across the interop boundary, generated as a get/set pair.
/// </summary>
public class Variable
{
	public bool Static { get; private set; }
	public string Name { get; set; }
	public Arg Return { get; set; }
	public string MangledName { get; set; }

	internal static Variable Parse( string line )
	{
		Match m = Regex.Match( line, @"^[\s+]?(static)?[\s+]?(.+?)\s+([a-zA-Z0-9_]+?);", RegexOptions.IgnoreCase );
		if ( !m.Success )
		{
			return null;
		}

		return new Variable
		{
			Static = m.Groups[1].Success,
			Name = m.Groups[3].Value.Trim(),
			Return = Arg.Parse( m.Groups[2].Value + " returnvalue" )
		};
	}

	internal string GetManagedName()
	{
		return Name == "GetType" ? "GetType_Native" : Name == "params" ? $"@{Name}" : Name;
	}

	/// <summary>
	/// A variable doesn't use attributes, but it still consumes the pending ones so they don't
	/// leak onto the next member.
	/// </summary>
	internal void TakeAttributes( List<string> attributes )
	{
		attributes.Clear();
	}
}
