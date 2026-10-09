using Sandbox.UI;

namespace EngineTests;

/// <summary>
/// Inspects the final frame data after command-list construction, before GPU playback.
/// </summary>
internal sealed class PanelDrawSnapshot
{
	internal UICssBoxBatched.BoxInstance[] Instances;
	internal UICssBoxBatched.TransformInstance[] Transforms;
	internal UICssBoxBatched.PathPrimitive[] Paths;
	internal UICssBoxBatched.BorderShape[] Shapes;

	internal static PanelDrawSnapshot Build( RootPanel root )
	{
		root.BuildCommandList();
		return Read( root );
	}

	internal static PanelDrawSnapshot Read( RootPanel root )
	{
		var batcher = root.PanelCommandList.FindResource<Painter.Context>().Batcher;
		return new PanelDrawSnapshot
		{
			Instances = batcher.Instances.ToArray(),
			Transforms = batcher.Transforms.ToArray(),
			Paths = batcher.Paths.ToArray(),
			Shapes = batcher.Shapes.ToArray()
		};
	}
}
