namespace Sandbox.UI;

/// <summary>
/// This is a tree renderer for panels. If we ever use razor on other ui we'll want to make a copy of
/// this class and do the specific things to that.
/// </summary>
public partial class PanelRenderTreeBuilder : Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder
{
	Stack<Scope> stack = new();
	Scope CurrentScope;

	void PushScope( int sequence, object key )
	{
		var loop = CurrentBlock.Increment( sequence );

		stack.Push( CurrentScope );
		CurrentScope.Sequence = sequence;
		CurrentScope.Loop = loop;
		CurrentScope.ChildIndex = 0;

		// Split so a null key doesn't box the loop index
		CurrentScope.Hash = key is null
			? HashCode.Combine( CurrentScope.Loop, CurrentScope.Sequence )
			: HashCode.Combine( key, CurrentScope.Sequence );
	}

	void PopScope()
	{
		contentBuilder.Clear();
		CurrentScope = stack.Pop();
	}

	struct Scope
	{
		public int Sequence { get; set; }
		public int Loop { get; set; }
		public Panel Element => Block?.ElementPanel;
		public Block Block { get; set; }
		public int ChildIndex { get; set; }
		public int Hash { get; set; }
	}
}
