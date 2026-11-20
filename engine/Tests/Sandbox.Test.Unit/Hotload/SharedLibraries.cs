using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Diagnostics;
using NodeLibrary = Facepunch.ActionGraphs.NodeLibrary;

namespace HotloadTests;

/// <summary>
/// Type and node libraries for Sandbox.Engine, shared between tests because building them takes most of a second.
/// </summary>
internal static class SharedLibraries
{
	private static readonly Lazy<(TypeLibrary Types, NodeLibrary Nodes)> _libraries = new( () =>
	{
		var typeLibrary = new TypeLibrary();
		typeLibrary.AddAssembly( typeof( LogNodes ).Assembly, false );

		var nodeLibrary = new NodeLibrary( new TypeLoader( () => typeLibrary ), new GraphLoader() );
		var result = nodeLibrary.AddAssembly( typeof( LogNodes ).Assembly );

		foreach ( var (method, e) in result.Errors )
		{
			Debug.WriteLine( $"{method}: {e}" );
		}

		Assert.IsFalse( result.AlreadyAdded );
		Assert.AreEqual( 0, result.Errors.Count );

		return (typeLibrary, nodeLibrary);
	} );

	public static TypeLibrary Types => _libraries.Value.Types;
	public static NodeLibrary Nodes => _libraries.Value.Nodes;
}
