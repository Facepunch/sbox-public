using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// The clip a node plays, previewed on the graph's model. Marking the clip up with events is the
/// clip window's job - a .anim is its own file, so it's edited where it can be undone and saved.
/// </summary>
[NodePage( typeof( AnimationClipNode ) )]
internal sealed class AnimationClipPage : ClipEditor, INodePage
{
	private readonly Window _window;
	private AnimationClipNode _node;

	protected override string NoModelHint => "Set a target model to preview this animation.";
	protected override string NoAnimationHint => "Select an animation on this node to preview it.";
	protected override Data PreviewSettings => _window.Graph;

	public AnimationClipPage( Widget parent, AnimationClipNode node, Window window )
		: base( parent, "Animation Clip Preview" )
	{
		_window = window;
		_node = node;
	}

	public bool Rebind( Node node )
	{
		if ( node is not AnimationClipNode clip || clip.Identifier != _node.Identifier )
			return false;

		_node = clip;
		return true;
	}

	protected override void OnPreFrame()
		=> SetSource( _window.Graph?.TargetModel, _node.AnimationName, _node.Loop, _node.SpeedMultiplier );
}
