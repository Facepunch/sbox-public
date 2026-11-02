namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Selects the Animation Clip whose option ID matches the input and preserves direct clip access for clip-specific nodes.
/// <br/><br/><b>Use when:</b>&#160;Choosing a named clip for root-motion warping or other clip-only processing.
/// <br/><b>Does not continuously update:</b>&#160;the clip is picked on entry and held, so changing the ID later will not switch it. To switch while playing, use a State Machine with a transition on that parameter.
/// <br/><b>Important:</b>&#160;Non-clip options or fallback poses force general pose-selector behavior.
/// </summary>
[Title( "ID Based Clip Selector" ), Group( "Flow" ), Icon( "call_split" )]
public class IDBasedAnimationClipSelectorNode : IDBasedSelectorNode
{
}
