using Sandbox.Physics;

namespace Editor;

/// <summary>
/// Simulate rigid bodies in editor
/// </summary>
[EditorTool( "tools.physics-tool" )]
[Title( "Physics Simulation" )]
[Icon( "panorama_fish_eye" )]
[Alias( "physics" )]
[Group( "Scene" )]
public class PhysicsEditorTool : EditorTool
{
	public bool IsSimulating { get; private set; }
	public int RigidBodyCount => RigidBodies.Count;

	private HashSet<Rigidbody> RigidBodies = new();
	private Rigidbody GrabbedBody;
	private float GrabbedDistance;
	private Vector3 LocalOffset;
	private PhysicsBody MouseBody;
	private Sandbox.Physics.FixedJoint MouseJoint;
	private PhysicsWidgetWindow Overlay;

	public override void OnEnabled()
	{
		base.OnEnabled();

		Scene.EnableEditorPhysics( true );

		IsSimulating = false;
		AllowGameObjectSelection = true;

		Selection.OnItemAdded += OnItemAdded;
		Selection.OnItemRemoved += OnItemRemoved;

		foreach ( var item in Selection )
		{
			OnItemAdded( item );
		}

		Overlay = new PhysicsWidgetWindow( this );
		AddOverlay( Overlay, TextFlag.RightBottom, 10 );

		MouseBody = new PhysicsBody( Scene.PhysicsWorld )
		{
			BodyType = PhysicsBodyType.Keyframed
		};
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		Scene.EnableEditorPhysics( false );

		IsSimulating = false;

		Selection.OnItemAdded -= OnItemAdded;
		Selection.OnItemRemoved -= OnItemRemoved;

		Scene.DisableEditorRigidBodies();

		RigidBodies.Clear();

		if ( MouseJoint.IsValid() )
			MouseJoint.Remove();

		if ( MouseBody.IsValid() )
			MouseBody.Remove();

		MouseBody = null;
		MouseJoint = null;

		SceneOverlay.Parent.Cursor = CursorShape.Arrow;
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		var aimTransform = Gizmo.CameraTransform;
		var hovered = false;

		var tr = Trace.Run();
		if ( RigidBodies.Contains( tr.Component ) )
			hovered = true;

		SceneOverlay.Parent.Cursor = GrabbedBody.IsValid() ?
				CursorShape.ClosedHand : hovered ? CursorShape.OpenHand : CursorShape.Arrow;

		if ( GrabbedBody.IsValid() )
		{
			if ( !Gizmo.IsLeftMouseDown )
			{
				if ( MouseJoint.IsValid() )
					MouseJoint.Remove();

				MouseJoint = null;
				GrabbedBody = null;

				return;
			}

			var plane = new Plane( aimTransform.Forward, GrabbedDistance );
			if ( plane.TryTrace( Gizmo.CurrentRay, out var hitPoint, true ) )
			{
				MouseBody.Position = hitPoint;
			}

			return;
		}

		if ( !Gizmo.IsLeftMouseDown )
			return;

		if ( hovered )
		{
			StartSimulation();

			GrabbedBody = tr.Component as Rigidbody;
			LocalOffset = tr.Body.Transform.PointToLocal( tr.HitPosition );
			GrabbedDistance = tr.HitPosition.Dot( aimTransform.Forward );

			MouseBody.Position = tr.HitPosition;
			MouseJoint = PhysicsJoint.CreateFixed( new PhysicsPoint( MouseBody ), new PhysicsPoint( tr.Body ) );
			MouseJoint.Point1 = new PhysicsPoint( MouseBody );
			MouseJoint.Point2 = new PhysicsPoint( tr.Body, LocalOffset );

			var maxForce = 100.0f * tr.Body.Mass * Scene.PhysicsWorld.Gravity.Length;
			MouseJoint.SpringLinear = new PhysicsSpring( 15, 1, maxForce );
			MouseJoint.SpringAngular = new PhysicsSpring( 0, 0, 0 );
		}
	}

	private void UpdateSelection()
	{
		RigidBodies = Selection.OfType<GameObject>()
			.SelectMany( x => x.Components.GetAll<Rigidbody>( FindMode.EnabledInSelfAndDescendants | FindMode.EnabledInSelfAndChildren ) )
			.Where( x => x.IsValid() )
			.ToHashSet();

		Overlay?.UpdateButton();

		StopSimulation();
	}

	private void OnItemAdded( object e )
	{
		if ( Manager.CurrentTool != this )
			return;

		UpdateSelection();
	}

	private void OnItemRemoved( object e )
	{
		if ( Manager.CurrentTool != this )
			return;

		UpdateSelection();
	}

	public void StartSimulation()
	{
		if ( IsSimulating )
			return;

		IsSimulating = true;

		foreach ( var rb in RigidBodies )
		{
			Scene.EnableEditorRigidBody( rb, true );
		}

		Overlay.UpdateButton();
	}

	public void StopSimulation()
	{
		if ( !IsSimulating )
			return;

		IsSimulating = false;

		Scene.DisableEditorRigidBodies();

		Overlay.UpdateButton();
	}

	public void ToggleSimulation()
	{
		if ( Manager.CurrentTool != this )
			return;

		if ( IsSimulating )
		{
			StopSimulation();
		}
		else
		{
			StartSimulation();
		}
	}

	private class PhysicsWidgetWindow : WidgetWindow
	{
		private readonly PhysicsEditorTool Tool;
		private readonly Button Button;

		public PhysicsWidgetWindow( PhysicsEditorTool tool ) : base( tool.SceneOverlay, "Physics Tool" )
		{
			Tool = tool;

			Layout = Layout.Row();
			Layout.Margin = 8;
			FixedWidth = 200.0f;

			Button = new Button( "Simulate" )
			{
				Enabled = false,
				Clicked = () => Tool.ToggleSimulation()
			};

			UpdateButton();

			var buttonRow = Layout.AddRow();
			buttonRow.Spacing = 2;
			buttonRow.Add( Button );
		}

		public void UpdateButton()
		{
			if ( !Button.IsValid() )
				return;

			var count = Tool.RigidBodyCount;

			if ( Tool.IsSimulating )
			{
				Button.Text = "Stop Simulation";
				Button.Enabled = true;
			}
			else
			{
				Button.Text = count > 0 ? $"Simulate {count} Objects" : "Simulate";
				Button.Enabled = count > 0;
			}
		}

		[Shortcut( "tools.physics-toggle", "Space", typeof( SceneViewportWidget ) )]
		public void ToggleSimulation()
		{
			Tool.ToggleSimulation();
		}
	}
}
