namespace Facepunch.InteropGen;

/// <summary>
/// Placeholder for a type name that wasn't a known built-in at parse time. <see cref="TypeResolver"/>
/// later resolves it to the real class/struct/pointer/delegate arg, or throws if it's genuinely unknown.
/// </summary>
public class ArgUnknown : Arg
{
	public string Type { get; set; }
}
