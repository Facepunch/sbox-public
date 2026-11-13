// Names Mono.Cecil.AssemblyDefinition - a type in an assembly (Mono.Cecil) that IS loaded in the test
// process (the engine's hotload resolver uses it) but is NOT in the compiler reference whitelist.
// Package code must not be able to bind it.
class Example
{
	static Mono.Cecil.AssemblyDefinition Get() => null;
}
