using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace JsonTests.Diff;

[TestClass]
public class ReorderingTest
{
	private static HashSet<Json.TrackedObjectDefinition> Definitions() =>
	[
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition( "Root", ["root"], allowedAsRoot: true ),
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition( "Item", ["id"], "id", "Root" )
	];

	private static JsonObject Tree( params int[][] arrays )
	{
		var root = new JsonObject { ["root"] = true };
		for ( var i = 0; i < arrays.Length; i++ )
			root[$"items{i}"] = new JsonArray( arrays[i].Select( id => (JsonNode)new JsonObject { ["id"] = id } ).ToArray() );
		return root;
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void BlockMovePreservesUnchangedSuccessors( bool reverseOperations )
	{
		var source = Tree( [1, 2, 3, 4] );
		var target = Tree( [3, 4, 1, 2] );
		var definitions = Definitions();
		var patch = Json.CalculateDifferences( source, target, definitions );
		Assert.AreEqual( 2, patch.MovedObjects.Count );
		if ( reverseOperations )
			patch.MovedObjects.Reverse();

		Assert.IsTrue( JsonNode.DeepEquals( target, Json.ApplyPatch( source, patch, definitions ) ) );
		Assert.IsTrue( JsonNode.DeepEquals( target, Json.ApplyPatch( source, patch, definitions ) ) );
		Assert.IsTrue( JsonNode.DeepEquals( Tree( [1, 2, 3, 4] ), source ) );
	}

	[TestMethod]
	public void BlockMovePreservesConcurrentSuccessors()
	{
		var definitions = Definitions();
		var patch = Json.CalculateDifferences( Tree( [1, 2, 3, 4] ), Tree( [3, 4, 1, 2] ), definitions );
		var result = Json.ApplyPatch( Tree( [1, 99, 2, 3, 4] ), patch, definitions );
		Assert.IsTrue( JsonNode.DeepEquals( Tree( [3, 4, 1, 99, 2] ), result ) );

		result = Json.ApplyPatch( Tree( [1, 3, 4] ), patch, definitions );
		Assert.IsTrue( JsonNode.DeepEquals( Tree( [3, 4, 1] ), result ) );
	}

	[TestMethod]
	public void MoveToAnotherContainerDoesNotCarrySuccessors()
	{
		var patch = new Json.Patch();
		patch.MovedObjects.Add( new Json.MovedObject
		{
			Id = new() { Type = "Item", IdValue = "1" },
			NewParent = new() { Type = "Root" },
			NewContainerProperty = "items1",
			IsNewContainerArray = true,
			NewPreviousElement = new Json.ObjectIdentifier { Type = "Item", IdValue = "3" }
		} );
		var result = Json.ApplyPatch( Tree( [1, 2], [3] ), patch, Definitions() );
		Assert.IsTrue( JsonNode.DeepEquals( Tree( [2], [3, 1] ), result ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ReversedDependenciesAcrossMultipleArrays( bool additions )
	{
		var first = Enumerable.Range( 0, 1000 ).ToArray();
		var second = Enumerable.Range( 1000, 1000 ).ToArray();
		var source = additions ? Tree( [], [] ) : Tree( first, second );
		var target = Tree( first.Reverse().ToArray(), second.Reverse().ToArray() );
		var definitions = Definitions();
		var patch = Json.CalculateDifferences( source, target, definitions );
		patch.AddedObjects.Reverse();
		patch.MovedObjects.Reverse();

		Assert.IsTrue( JsonNode.DeepEquals( target, Json.ApplyPatch( source, patch, definitions ) ) );
		Assert.IsTrue( JsonNode.DeepEquals( target, Json.ApplyPatch( source, patch, definitions ) ) );
	}

	[TestMethod]
	public void CyclicPredecessorsUseBoundedFallback()
	{
		var patch = new Json.Patch();
		foreach ( var (id, previous) in new[] { ("1", "2"), ("2", "1") } )
		{
			patch.MovedObjects.Add( new Json.MovedObject
			{
				Id = new() { Type = "Item", IdValue = id },
				NewParent = new() { Type = "Root" },
				NewContainerProperty = "items0",
				IsNewContainerArray = true,
				NewPreviousElement = new Json.ObjectIdentifier { Type = "Item", IdValue = previous }
			} );
		}

		var result = Json.ApplyPatch( Tree( [1, 2, 3] ), patch, Definitions() );
		Assert.IsTrue( JsonNode.DeepEquals( Tree( [3, 1, 2] ), result ) );
	}

	[TestMethod]
	public void PartialPatchWithMissingPredecessors()
	{
		var definitions = Definitions();
		var patch = Json.CalculateDifferences( Tree( [1, 2, 3] ), Tree( [1, 4, 2, 5, 3] ), definitions );
		// Both added objects lose their anchors. Preserve the bounded fallback order.
		var result = Json.ApplyPatch( Tree( [3] ), patch, definitions );
		Assert.IsTrue( JsonNode.DeepEquals( Tree( [5, 3, 4] ), result ) );
	}
}
