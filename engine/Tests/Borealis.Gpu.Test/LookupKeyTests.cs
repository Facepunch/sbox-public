using System.Reflection;

namespace BorealisGpuTests;

/// <summary>
/// The renderer's lookups allocate nothing however far the JIT has got with them. A tuple of eight or more keeps the rest in a
/// tuple of its own, which its hash and equality box until the JIT has optimised them: a dictionary keyed by one allocated on
/// every lookup in a run's first seconds, and the steady-frame allocation tests (<see cref="AllocationTests"/>) failed or passed
/// by how soon the JIT got there. Keys that long are record structs.
/// </summary>
[TestClass]
public class LookupKeyTests
{
	static readonly Type[] Keyed = [typeof( Dictionary<,> ), typeof( HashSet<> ), typeof( System.Collections.Concurrent.ConcurrentDictionary<,> )];

	[TestMethod]
	public void NoLookupIsKeyedByALongTuple()
	{
		Assembly[] assemblies = [typeof( Borealis.RenderSystem ).Assembly, typeof( Borealis.Backend.BackendDevice ).Assembly, typeof( Borealis.Gpu.Device ).Assembly, typeof( Borealis.Source2.ContentFiles ).Assembly];
		var found = new List<string>();
		foreach ( var assembly in assemblies )
		{
			foreach ( var type in assembly.GetTypes() )
			{
				foreach ( var field in type.GetFields( BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
				{
					var fieldType = field.FieldType;
					if ( !fieldType.IsGenericType || !Keyed.Contains( fieldType.GetGenericTypeDefinition() ) ) continue;
					var key = fieldType.GetGenericArguments()[0];
					if ( key.IsGenericType && key.GetGenericTypeDefinition() == typeof( ValueTuple<,,,,,,,> ) ) found.Add( $"{type.FullName}.{field.Name}" );
				}
			}
		}

		Assert.AreEqual( 0, found.Count, "keyed by a tuple of eight or more: " + string.Join( ", ", found ) );
	}
}
