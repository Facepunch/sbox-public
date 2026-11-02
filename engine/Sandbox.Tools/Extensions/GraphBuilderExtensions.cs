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

	extension( Graph )
	{
		public static Graph Create( string name, byte[] definition )
		{
			return Graph.Create( name, definition );
		}
	}

}
