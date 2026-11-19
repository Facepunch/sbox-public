using Sandbox.Animation;

namespace Sandbox;

public static partial class SandboxToolExtensions
{
	public static int GetRuntimeIndex( this GraphBuilder.Node node )
	{
		return node.Index;
	}

	public static int GetRuntimeIndex( this GraphBuilder.StateMachineBuilder.State state )
	{
		return state.Node.Index;
	}

	public static bool IsClip( this GraphBuilder.Node node )
	{
		return node.IsClip;
	}

	public static byte[] Compile( this GraphBuilder builder, GraphBuilder.Node root, string model, string variation )
	{
		return builder.Compile( root, model, variation );
	}

	public static IEnumerable<string> GetResourceReferences( this GraphBuilder builder ) => builder.ResourceReferences;

	public static void SetSkeleton( this GraphBuilder builder, ResourceId skeleton ) => builder.SkeletonReference = skeleton;
	public static GraphBuilder.Node Clip( this GraphBuilder builder, ResourceId animation, GraphBuilder.ClipSettings settings = null ) => builder.Clip( animation, settings );
	public static GraphBuilder.Node TimeControlledClip( this GraphBuilder builder, ResourceId animation, GraphBuilder.Node time, GraphBuilder.TimeControlledClipSettings settings = null ) => builder.TimeControlledClip( animation, time, settings );
	public static GraphBuilder.Node Pose( this GraphBuilder builder, ResourceId animation, GraphBuilder.PoseSettings settings ) => builder.Pose( animation, settings );
	public static GraphBuilder.Node ReferencedGraph( this GraphBuilder builder, ResourceId graph, GraphBuilder.Node fallback = null, string variation = "" ) => builder.ReferencedGraph( graph, fallback, variation );

	extension( Graph )
	{
		public static Graph Create( string name, byte[] definition )
		{
			return Graph.Create( name, definition );
		}
	}

}
