namespace Editor.AnimGraph;

/// <summary>
/// Checks the clips, bones and bone masks a graph names against what the variation animates -
/// bones against its model, clips against its skeleton. None of this stops a graph compiling -
/// the names simply don't resolve when it plays, which otherwise only shows up as a console
/// warning at runtime.
/// </summary>
internal static class Validator
{
	/// <summary>
	/// Warn about every name a variation can't resolve. The graph is walked as the variation
	/// resolves it, so each variation is checked against what it actually animates.
	/// </summary>
	public static IReadOnlyList<Compiler.Warning> Validate( Data graph, string variationID )
	{
		var variation = graph?.Variations?.GetVariation( variationID );
		if ( variation?.TargetModel is not { } model || !model.IsValid() )
			return [];

		var bones = new HashSet<string>( model.Bones.AllBones.Select( bone => bone.Name ), StringComparer.OrdinalIgnoreCase );
		var modelName = System.IO.Path.GetFileName( model.Name );

		// What a property's string means is whatever picked it - an editor or a resource type - so
		// a node naming a bone or a clip gets checked without this knowing the node exists.
		var expectations = new Dictionary<string, (HashSet<string> Names, string Describe)>
		{
			["animgraph.bone"] = (bones, $"a bone on {modelName}")
		};

		// Clips and bone masks belong to the skeleton rather than the model - a clip is compiled
		// against one, a mask is authored on one. Without a skeleton there's nothing to check
		// against, and that missing skeleton is reported on its own. A skeleton that didn't
		// resolve looks exactly like one with no clips or masks, so leave those names alone
		// rather than calling every one of them wrong.
		var skeletonPath = variation.ResolveTargetSkeletonPath();
		if ( !string.IsNullOrWhiteSpace( skeletonPath ) )
		{
			var skeletonName = System.IO.Path.GetFileName( skeletonPath );

			var clips = new HashSet<string>( variation.CompatibleClips, StringComparer.OrdinalIgnoreCase );
			if ( clips.Count > 0 )
			{
				expectations["resource:anim"] = (clips, $"an animation clip for {skeletonName}");
			}

			if ( Skeleton.Load( skeletonPath ) is { MaskNames.Count: > 0 } skeleton )
			{
				expectations["animgraph.bonemask"] = (new( skeleton.MaskNames, StringComparer.OrdinalIgnoreCase ),
					$"a bone mask on {skeletonName}");
			}
		}

		var warnings = new List<Compiler.Warning>();

		void Check( object target, Node node )
		{
			if ( target is null )
				return;

			foreach ( var property in EditorTypeLibrary.GetType( target.GetType() )?.Properties ?? [] )
			{
				var picker = property.GetCustomAttribute<EditorAttribute>()?.Value
					?? property.GetCustomAttribute<ResourceTypeAttribute>()?.AssetTypeExtension;

				if ( picker is null || !expectations.TryGetValue( picker, out var expected ) )
					continue;

				if ( property.GetValue( target ) is not string name || string.IsNullOrWhiteSpace( name ) || expected.Names.Contains( name ) )
					continue;

				var message = $"'{name}' is not {expected.Describe}";

				if ( warnings.All( warning => warning.Message != message ) )
					warnings.Add( new Compiler.Warning( message, node ) );
			}
		}

		foreach ( var node in graph.AllNodes )
		{
			Check( node, node );

			if ( node is VariationDataNode variationData )
				Check( variationData.GetResolvedVariationData( graph.Variations, variationID ), node );
		}

		return warnings;
	}
}
