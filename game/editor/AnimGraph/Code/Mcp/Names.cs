using Editor.Mcp;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	/// <summary>
	/// The names a graph is allowed to reference, per variation: the bones of its model, the bone
	/// masks and animation clips of its skeleton, and the options of its ID parameters. Node
	/// properties that name one of these have to match exactly - a name that doesn't resolve still
	/// compiles, it just does nothing when the graph plays. Read this before authoring rather than
	/// guessing names and finding out from animgraph_compile warnings.
	/// </summary>
	/// <param name="path">The .ag asset path.</param>
	/// <param name="variation">Optional variation. Every variation is returned when empty.</param>
	[McpTool.ReadOnly( "animgraph_names" )]
	public static NamesResult Names( string path, string variation = "" )
	{
		var session = LoadSession( path );
		var root = session.Graph;

		var variations = string.IsNullOrWhiteSpace( variation )
			? root.Variations.All.ToArray()
			: [FindVariation( root, variation )];

		return new NamesResult
		{
			Path = session.Asset.Path,
			Revision = session.Revision,
			Variations = variations.Select( DescribeNames ).ToArray(),
			IdParameters = root.AllParameters.OfType<Parameter>()
				.Where( parameter => parameter.Options.Count > 0 )
				.Select( parameter => new IdParameterNames
				{
					Name = parameter.Name,
					Options = [.. parameter.Options]
				} )
				.ToArray()
		};
	}

	private static VariationNames DescribeNames( Variation variation )
	{
		var model = variation.TargetModel;
		var skeletonPath = variation.ResolveTargetSkeletonPath();

		return new VariationNames
		{
			Variation = variation.ID,
			Model = model?.Name,
			Skeleton = skeletonPath,
			Bones = model.IsValid() ? model.Bones.AllBones.Select( bone => bone.Name ).ToArray() : [],
			BoneMasks = string.IsNullOrWhiteSpace( skeletonPath ) || Skeleton.Load( skeletonPath ) is not { } skeleton
				? []
				: [.. skeleton.MaskNames],
			Clips = variation.CompatibleClips.Order().ToArray()
		};
	}
}
