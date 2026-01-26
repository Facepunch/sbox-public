using System.Collections.Generic;
using System.Text.Json.Nodes;
using System;
using System.Text.Json;

public static class JsonTestUtils
{
	private static readonly JsonSerializerOptions PrettyPrintOptions = new()
	{
		WriteIndented = true
	};

	// Helper method to strip null values from a JsonNode to make output more readable
	private static void StripNullValues( JsonNode node )
	{
		if ( node is JsonObject obj )
		{
			var nullProps = obj.Where( x => x.Value == null || x.Value is JsonValue value && value.GetValueKind() == JsonValueKind.Null )
							  .Select( x => x.Key )
							  .ToList();

			foreach ( var prop in nullProps )
			{
				obj.Remove( prop );
			}

			foreach ( var prop in obj )
			{
				StripNullValues( prop.Value );
			}
		}
		else if ( node is JsonArray arr )
		{
			for ( int i = 0; i < arr.Count; i++ )
			{
				StripNullValues( arr[i] );
			}
		}
	}

	// Main helper method for round-trip testing
	internal static void RunRoundTripTest( JsonObject source, JsonObject target, string testName, HashSet<Json.TrackedObjectDefinition> definitions )
	{
		var cleanSource = (JsonObject)source.DeepClone();
		StripNullValues( cleanSource );
		var cleanTarget = (JsonObject)target.DeepClone();
		StripNullValues( cleanTarget );

		Console.WriteLine();
		Console.WriteLine( "=== Warning ===" );
		Console.WriteLine( " Null values are omitted from the test output" );
		Console.WriteLine( " This is done so very large hierarchies remain readable." );
		Console.WriteLine( " If you ever need to debug this you might want to disable that." );
		Console.WriteLine();

		Console.WriteLine( $"=== {testName} ===" );

		Console.WriteLine( "Source JSON:" );
		Console.WriteLine( cleanSource.ToJsonString( PrettyPrintOptions ) );
		Console.WriteLine( "\nTarget JSON:" );
		Console.WriteLine( cleanTarget.ToJsonString( PrettyPrintOptions ) );

		// Generate patch
		var patch = Json.CalculateDifferences( source, target, definitions );

		Console.WriteLine( "\nPatch Operations:" );
		Console.WriteLine( $"\nAdded objects: {patch.AddedObjects.Count}" );
		Console.WriteLine( $"Removed objects: {patch.RemovedObjects.Count}" );
		Console.WriteLine( $"Moved objects: {patch.MovedObjects.Count}" );
		Console.WriteLine( $"Property overrides: {patch.PropertyOverrides.Count}" );

		Console.WriteLine( "\nPatch JSON:" );
		Console.WriteLine( Json.SerializeAsObject( patch ).ToJsonString( PrettyPrintOptions ) );

		// Apply patch to source
		var result = Json.ApplyPatch( source, patch, definitions );


		var cleanResult = (JsonObject)result.DeepClone();
		StripNullValues( cleanResult );

		Console.WriteLine( "\nResult after applying patch:" );
		Console.WriteLine( cleanResult.ToJsonString( PrettyPrintOptions ) );

		// Verify that applying the patch to source equals target
		bool areEqual = JsonNode.DeepEquals( cleanResult, cleanTarget );
		Assert.IsTrue( areEqual, "Applied patch should transform source into target" );
	}
}
