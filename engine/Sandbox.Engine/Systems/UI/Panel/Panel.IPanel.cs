using Sandbox.Internal;

namespace Sandbox.UI;

public partial class Panel
{
	IPanel IPanel.Parent => Parent;
	IEnumerable<IPanel> IPanel.Children => Children;
	int IPanel.ChildrenCount => ChildrenCount;
	string IPanel.ElementName => ElementName;

	bool IPanel.IsMainMenu => Game.IsMenu;
	bool IPanel.IsGame => !Game.IsMenu;
	bool IPanel.IsVisible => IsVisible;
	bool IPanel.IsVisibleSelf => IsVisibleSelf;
	string IPanel.Classes => Classes;
	Rect IPanel.Rect => Box.Rect;
	Rect IPanel.OuterRect => Box.RectOuter;
	Rect IPanel.InnerRect => Box.RectInner;
	Matrix? IPanel.GlobalMatrix => GlobalMatrix;
	bool IPanel.WantsPointerEvents => (ComputedStyle?.PointerEvents ?? PointerEvents.None) == PointerEvents.All;

	IPanel IPanel.GetPanelAt( Vector2 point, bool visibleOnly, bool needPointerEvents ) => GetPanelAt( point, visibleOnly, needPointerEvents );
	bool IPanel.IsAncestor( IPanel panel ) => IsAncestor( panel as Panel );

	Panel GetPanelAt( Vector2 point, bool visibleOnly, bool needPointerEvents = false )
	{
		if ( this is RootPanel root && root.FindFixedPanelAt( point, visibleOnly, needPointerEvents ) is { } overlayHit ) return overlayHit;
		if ( visibleOnly && !IsVisible ) return null;

		point = LocalMatrix?.Transform( point ) ?? point;

		if ( !IsInside( point ) ) return null;

		Panel bestSelection = this;

		foreach ( var child in Children.OrderByDescending( x => x.GetRenderOrderIndex() ).ThenByDescending( x => x.SiblingIndex ) )
		{
			if ( child.IsFixed ) continue;
			var p = child.GetPanelAt( point, visibleOnly, needPointerEvents );

			if ( !p.IsValid() ) continue;

			bestSelection = p;
			break;
		}

		if ( bestSelection == this && needPointerEvents && !(this as IPanel).WantsPointerEvents )
			return null;

		return bestSelection;
	}

	int Depth => 1 + (Parent?.Depth ?? 0);

	void IPanel.Delete( bool immediate ) => Delete( immediate );

	/// <summary>
	/// Pin the panel to a spot on its UI, offset from a position - a tooltip beside the cursor.
	/// The alignment says which side of the position the panel sits on. This wouldn't be needed
	/// if we could expose the styles. Which we should do.
	/// </summary>
	internal void SetAbsolutePosition( TextFlag alignment, Vector2 position, float offset )
	{
		var size = UISystem.Size;

		Style.Left = null;
		Style.Right = null;
		Style.Top = null;
		Style.Bottom = null;

		if ( (alignment & TextFlag.Left) != 0 )
		{
			Style.Right = ((size.x - position.x) + offset) * ScaleFromScreen;
		}

		if ( (alignment & TextFlag.Right) != 0 )
		{
			Style.Left = (offset + position.x) * ScaleFromScreen;
		}

		if ( (alignment & TextFlag.Top) != 0 )
			Style.Bottom = ((size.y - position.y) + offset) * ScaleFromScreen;

		if ( (alignment & TextFlag.Bottom) != 0 )
			Style.Top = (offset + position.y) * ScaleFromScreen;
	}
}
