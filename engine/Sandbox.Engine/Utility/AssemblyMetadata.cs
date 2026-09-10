using Mono.Cecil;
using System.IO;

namespace Sandbox;

public static class AssemblyMetadata
{
	public struct Attribute
	{
		public string AttributeType { get; }
		public string AttributeFullName { get; }
		public object[] Arguments { get; }

		public Attribute( CustomAttribute x )
		{
			AttributeType = x.AttributeType.Name;
			AttributeFullName = x.AttributeType.FullName;
			Arguments = x.ConstructorArguments.Select( a => a.Value ).ToArray();
		}
	}

	public static Attribute[] GetCustomAttributes( byte[] assemblyData )
	{
		using var ms = new MemoryStream( assemblyData );
		using var assembly = AssemblyDefinition.ReadAssembly( ms );

		return assembly.CustomAttributes.Select( x => new Attribute( x ) ).ToArray();
	}
}
