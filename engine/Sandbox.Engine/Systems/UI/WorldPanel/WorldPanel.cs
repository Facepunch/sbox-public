namespace Sandbox.UI
{
	/// <summary>
	/// An interactive 2D panel rendered in the 3D world.
	/// </summary>
	public class WorldPanel : RootPanel
	{
		public static float ScreenToWorldScale => ScenePanelObject.ScreenToWorldScale;

		/// <summary>
		/// Scene object that renders the panel.
		/// </summary>
		internal ScenePanelObject SceneObject { get; set; }

		/// <summary>
		/// Transform of the world panel in 3D space.
		/// </summary>
		public Transform Transform
		{
			get => SceneObject.Transform;
			set => SceneObject.Transform = value;
		}

		/// <summary>
		/// Tags that are applied to the underlying SceneObject
		/// </summary>
		public ITagSet Tags => SceneObject.Tags;

		/// <summary>
		/// Position of the world panel in 3D space.
		/// </summary>
		public Vector3 Position
		{
			get => Transform.Position;
			set => Transform = Transform.WithPosition( value );
		}

		/// <summary>
		/// Rotation of the world panel in 3D space.
		/// </summary>
		public Rotation Rotation
		{
			get => Transform.Rotation;
			set => Transform = Transform.WithRotation( value );
		}

		/// <summary>
		/// Scale of the world panel in 3D space.
		/// </summary>
		public float WorldScale
		{
			get => Transform.UniformScale;
			set => Transform = Transform.WithScale( value );
		}

		/// <summary>
		/// Maximum distance at which a player can interact with this world panel.
		/// </summary>
		public float MaxInteractionDistance { get; set; }

		public WorldPanel( SceneWorld world ) : this( world, Sandbox.Engine.GlobalContext.Current.UISystem )
		{
		}

		/// <summary>
		/// A world panel hosted by an independent UI surface, such as an editor window.
		/// </summary>
		internal WorldPanel( SceneWorld world, UISystem system ) : base( system )
		{
			ArgumentNullException.ThrowIfNull( world, "world" );

			SceneObject = new ScenePanelObject( world, this );
			SceneObject.Flags.IsOpaque = false;
			SceneObject.Flags.IsTranslucent = true;

			// Don't render this panel using the panel system
			RenderedManually = true;

			// Default size is 1000x1000, centered on scene object transform
			PanelBounds = new Rect( -500, -500, 1000, 1000 );

			// World panels are scaled down to world units,
			// so boost the panel scale to sensible default
			Scale = 2.0f;

			MaxInteractionDistance = 1000.0f;

			// This is a world panel - we need to set this so that layers
			// get the attribute restored properly
			IsWorldPanel = true;
		}

		/// <summary>
		/// Update the bounds for this panel. We purposely do nothing here because
		/// on world panels you can change the bounds by setting <see cref="RootPanel.PanelBounds"/>.
		/// </summary>
		protected override void UpdateBounds( Rect rect )
		{
			if ( !SceneObject.IsValid() )
				return;

			var right = Rotation.Right;
			var down = Rotation.Down;

			var panelBounds = PanelBounds * WorldScale * ScenePanelObject.ScreenToWorldScale;

			//
			// Work out the bounds by adding each corner to a bbox
			//
			var bounds = BBox.FromPositionAndSize( right * panelBounds.Left + down * panelBounds.Top );
			bounds = bounds.AddPoint( right * panelBounds.Left + down * panelBounds.Bottom );
			bounds = bounds.AddPoint( right * panelBounds.Right + down * panelBounds.Top );
			bounds = bounds.AddPoint( right * panelBounds.Right + down * panelBounds.Bottom );

			SceneObject.Bounds = bounds + Position;
		}

		/// <summary>
		/// We override this to prevent the scale automatically being set based on screen
		/// size changing.. because that's obviously not needed here.
		/// </summary>
		protected override void UpdateScale( Rect screenSize )
		{

		}

		public override void Delete( bool immediate = false )
		{
			base.Delete( immediate );
		}

		public override void OnDeleted()
		{
			base.OnDeleted();

			SceneObject?.Delete();
			SceneObject = null;
		}

		public override bool RayToLocalPosition( Ray ray, out Vector2 position, out float distance )
		{
			position = default;
			distance = 0;

			var plane = new Plane( Position, Rotation.Forward );
			var pos = plane.Trace( ray, false, MaxInteractionDistance );

			if ( !pos.HasValue )
				return false;

			distance = Vector3.DistanceBetween( pos.Value, ray.Position );
			if ( distance < 1 )
				return false;

			// to local coords
			var localPos3 = Transform.PointToLocal( pos.Value );
			var localPos = new Vector2( localPos3.y, -localPos3.z );

			// convert to screen coords
			localPos *= 1f / ScenePanelObject.ScreenToWorldScale;

			if ( !IsInside( localPos ) )
				return false;

			position = localPos;

			return true;
		}
	}
}
