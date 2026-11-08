namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Chooses one Animation Clip when initialized from the absolute floored input value and weighted option cycle, preserving direct clip access.
/// <br/><br/><b>Use when:</b>&#160;Deterministically varying a clip for root-motion warping or other clip-only processing.
/// <br/><b>Does not continuously update:</b>&#160;the clip is picked on entry and held, so changing the parameter later will not switch it. To switch while playing, use a State Machine with a transition on that parameter.
/// <br/><b>Important:</b>&#160;Non-clip options force general pose-selector behavior.
/// </summary>
[Title( "Parameterized Clip Selector" ), Group( "Flow" ), Icon( "shuffle" )]
public class ParameterizedAnimationClipSelectorNode : ParameterizedSelectorNode
{
}
