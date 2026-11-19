
using Sandbox.UI;
namespace Sandbox;

/// <summary>
/// A clickable icon over a component in the world, shown while the inspect menu is open. Used for things with
/// nothing to click on themselves, see <see cref="IInspectHandle"/>. Left click selects, right click opens the
/// object's context menu.
/// </summary>
public class ComponentHandle : Panel
{
	/// <summary>
	/// Handles further than this from the camera aren't shown.
	/// </summary>
	public const float MaxDistance = 2048f;

	readonly Inspector _inspector;
	readonly Component _component;
	readonly Label _icon;
	Vector3 worldpos;

	public ComponentHandle( Panel parent, Component c, Inspector i ) : base( parent )
	{
		_component = c;
		_inspector = i;
		worldpos = _component.WorldPosition;

		_icon = AddChild<Label>( "icon" );

		var typeInfo = TypeLibrary.GetType( c.GetType() );
		_icon.Text = (c as IInspectHandle)?.InspectHandleIcon ?? typeInfo?.Icon ?? "info";
	}

	public override void Tick()
	{
		base.Tick();

		if ( ShouldDelete() )
		{
			Delete();
			return;
		}

		worldpos = _component.WorldPosition;

		var screenPos = Scene.Camera.PointToScreenPixels( worldpos, out bool behind );
		Style.Left = screenPos.x * ScaleFromScreen;
		Style.Top = screenPos.y * ScaleFromScreen;

		SetClass( "behind", behind );
		SetClass( "active", _inspector?.Selected.Contains( _component.GameObject ) ?? false );
	}

	bool ShouldDelete()
	{
		if ( !_component.IsValid() ) return true;
		if ( _component is IInspectHandle handle && !handle.ShowInspectHandle ) return true;
		if ( Scene?.Camera is not { IsValid: true } camera ) return true;

		// a little past the range they're made in, so one on the edge doesn't flicker
		return worldpos.Distance( camera.WorldPosition ) > MaxDistance * 1.1f;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		e.StopPropagation();

		if ( !_component.IsValid() ) return;

		// the component's own object, not its network root - a hidden speaker parented to a prop should select the
		// speaker (so the inspector shows its settings), not the prop
		var target = _component.GameObject;

		if ( e.MouseButton == MouseButtons.Right )
		{
			_inspector?.OpenContextMenu( target );
			return;
		}

		_inspector?.SelectObject( target );
	}
}
