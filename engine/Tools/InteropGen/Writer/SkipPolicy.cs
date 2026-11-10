using System.Linq;

namespace Facepunch.InteropGen;

/// <summary>
/// Decides which classes / structs / includes to skip while emitting, based on what's already
/// provided by an inherited def (.inherit) or wholesale-skipped (.skipall / .skipdefine).
/// </summary>
internal class SkipPolicy
{
	private readonly Definition definitions;

	public SkipPolicy( Definition definitions )
	{
		this.definitions = definitions;
	}

	public bool ShouldSkip( Class def )
	{
		// Don't import a native class if we already have it in this assembly
		if ( def.Native && IsIncluded( def ) )
		{
			return true;
		}

		// Skipped wholesale by a [skipall] include
		return definitions.SkipAll.Any( x => x.Classes.Any( y => y.ManagedNameWithNamespace == def.ManagedNameWithNamespace && y.NativeNameWithNamespace == def.NativeNameWithNamespace ) );
	}

	public bool ShouldStubFunction( Class c )
	{
		// Stub functions in classes marked with WindowsOnly on non-Windows platforms
		return c.HasAttribute( "WindowsOnly" ) && !System.OperatingSystem.IsWindows();
	}

	public bool ShouldSkip( Struct s )
	{
		// Already provided by an include
		if ( IsIncluded( s ) )
		{
			return true;
		}

		// Skipped wholesale by a [skipall] include
		return definitions.SkipAll.Any( x => x.Structs.Any( y => y.ManagedNameWithNamespace == s.ManagedNameWithNamespace && y.NativeNameWithNamespace == s.NativeNameWithNamespace ) );
	}

	public bool ShouldSkipInclude( string include )
	{
		return definitions.SkipAll.Any( x => x.Includes.Contains( include ) );
	}

	private bool IsIncluded( Class c )
	{
		return definitions.IncludedDefinitions.Any( x => x.Classes.Any( y => y.ManagedNameWithNamespace == c.ManagedNameWithNamespace && y.NativeNameWithNamespace == c.NativeNameWithNamespace ) );
	}

	private bool IsIncluded( Struct s )
	{
		return definitions.IncludedDefinitions.Any( x => x.Structs.Any( y => y.ManagedNameWithNamespace == s.ManagedNameWithNamespace && y.NativeNameWithNamespace == s.NativeNameWithNamespace ) );
	}
}
