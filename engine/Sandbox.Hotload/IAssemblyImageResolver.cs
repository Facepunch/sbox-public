using System.Reflection;
using Mono.Cecil;

namespace Sandbox;

/// <summary>
/// Opens the exact image that supplied a loaded assembly so hotload can read its method bodies.
/// </summary>
public interface IAssemblyImageResolver : IAssemblyResolver
{
	/// <summary>
	/// Opens a definition owned by the caller, or returns null if the assembly's image is unavailable.
	/// </summary>
	AssemblyDefinition OpenAssemblyImage( Assembly assembly );
}
