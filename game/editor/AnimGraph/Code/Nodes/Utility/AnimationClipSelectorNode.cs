namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Selects the first Animation Clip whose condition is true and preserves direct clip access for clip-specific nodes.
/// <br/><br/><b>Use when:</b>&#160;Conditionally choosing a clip for root-motion warping or other clip-only processing.
/// <br/><b>Does not continuously update:</b>&#160;the clip is picked on entry and held. To switch while playing, use a State Machine with a transition on that parameter.
/// <br/><b>Important:</b>&#160;Every option must be a direct Animation Clip or the node falls back to general pose-selector behavior.
/// </summary>
[Title( "Clip Selector" ), Group( "Flow" ), Icon( "call_split" )]
public class AnimationClipSelectorNode : SelectorNode
{
}
