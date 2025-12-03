using System;

namespace Editor
{
	public enum ScrollbarMode
	{
		/// <summary>
		/// Only show the scrollbar when necessary.
		/// </summary>
		Auto,

		/// <summary>
		/// Never show the scrollbar.
		/// </summary>
		Off,

		/// <summary>
		/// Always show the scrollbar.
		/// </summary>
		On
	}

	public partial class GraphicsView : Widget
	{
		Native.QGraphicsView _graphicsview;

		public Rect SceneRect
		{
			get => _graphicsview.sceneRect().Rect;
			set => _graphicsview.setSceneRect( value );
		}

		public bool Capture( string path )
		{
			return Scene._scene.Capture( path );
		}

		/// <summary>
		/// All items inside this rect will be selected
		/// </summary>
		public Rect SelectionRect
		{
			set => Scene._scene.setSelectionArea( _graphicsview, value );
		}

		/// <summary>
		/// Where in the scene is the view currently centered.
		/// </summary>
		public Vector2 Center
		{
			get => (Vector2)_graphicsview.mapToScene( Size * 0.5f );
			set => CenterOn( value );
		}

		Vector2 _scale = 1;
		public Vector2 Scale
		{
			get => _scale;
			set
			{
				_scale = value;
				BuildTransform();
			}
		}

		float _rotate;
		public float Rotation
		{
			get => _rotate;
			set
			{
				_rotate = value;
				BuildTransform();
			}
		}

		public IEnumerable<GraphicsItem> SelectedItems => Scene.SelectedItems;

		public float MinZoom { get; set; } = 0.1f;
		public float MaxZoom { get; set; } = 5.0f;

		public void Zoom( float adjust, Vector2 viewpos )
		{
			_scale *= adjust;
			_scale = _scale.Clamp( MinZoom, MaxZoom );
			var mousePosBefore = ToScene( viewpos );
			_graphicsview.resetTransform();
			_graphicsview.scale( _scale.x, _scale.y );
			var mousePosAfter = ToScene( viewpos );
			var delta = mousePosAfter - mousePosBefore;
			_graphicsview.translate( delta.x, delta.y );
		}

		public void Translate( Vector2 delta )
		{
			if ( delta.Length <= 0.001f )
				return;

			var old = TransformAnchor;
			TransformAnchor = ViewportAnchorType.NoAnchor;
			_graphicsview.translate( delta.x, delta.y );
			TransformAnchor = old;
		}

		void BuildTransform()
		{
			_graphicsview.resetTransform();
			_graphicsview.scale( _scale.x, _scale.y );
			_graphicsview.rotate( _rotate );
		}

		public GraphicsView( Widget parent = null ) : base( false )
		{
			Sandbox.InteropSystem.Alloc( this );
			NativeInit( WidgetUtil.CreateGraphicsView( parent?._widget ?? default, this ) );

			// common defaults
			SetSizeMode( SizeMode.Default, SizeMode.Default );
			HorizontalScrollbar = ScrollbarMode.Off;
			VerticalScrollbar = ScrollbarMode.Off;
			Antialiasing = true;
			TextAntialiasing = true;
			BilinearFiltering = true;

			Scene = new GraphicsScene( this );
			Scene.OnSelectionChanged += () => OnSelectionChanged?.Invoke();
			_graphicsview.setScene( Scene._scene );

			TransformAnchor = ViewportAnchorType.NoAnchor;
		}

		public Vector2 ToScene( Vector2 pos ) => (Vector2)_graphicsview.mapToScene( pos );
		public Rect ToScene( Rect pos ) { pos.Position = ToScene( pos.Position ); return pos; }

		public Vector2 FromScene( Vector2 pos ) => (Vector2)_graphicsview.mapFromScene( pos );
		public Rect FromScene( Rect pos ) { pos.Position = FromScene( pos.Position ); return pos; }

		internal GraphicsScene Scene
		{
			get;
			init;
		}

		public void DeleteAllItems()
		{
			foreach ( var item in Items.ToArray() )
			{
				item.Destroy();
			}
		}

		public IEnumerable<GraphicsItem> Items => Scene.Items;

		internal override void NativeInit( IntPtr ptr )
		{
			_graphicsview = ptr;

			base.NativeInit( ptr );
		}
		internal override void NativeShutdown()
		{
			base.NativeShutdown();

			_graphicsview = default;

			// Scene is free'd auomatically I think (because we're its parent)
		}


		public void CenterOn( Vector2 center ) => _graphicsview.centerOn( center );
		public void FitInView( Rect rect ) => _graphicsview.fitInView( rect );

		public ScrollbarMode HorizontalScrollbar
		{
			get => _graphicsview.horizontalScrollBarPolicy();
			set => _graphicsview.setHorizontalScrollBarPolicy( value );
		}

		public ScrollbarMode VerticalScrollbar
		{
			get => _graphicsview.verticalScrollBarPolicy();
			set => _graphicsview.setVerticalScrollBarPolicy( value );
		}

		public void SetBackgroundImage( string image )
		{
			SetBackgroundImage( Pixmap.FromFile( image ) );
		}

		public void SetBackgroundImage( Pixmap image )
		{
			_graphicsview.setBackground( image.ptr );
		}


		public enum ViewportAnchorType
		{
			NoAnchor,
			AnchorViewCenter,
			AnchorUnderMouse
		}

		public ViewportAnchorType TransformAnchor
		{
			get => _graphicsview.transformationAnchor();
			set => _graphicsview.setTransformationAnchor( value );
		}

		public GraphicsItem GetItemAt( Vector2 scenePosition )
		{
			var item = _graphicsview.itemAt( FromScene( scenePosition ) );

			return GraphicsItem.Get( item );

		}

		public void Add( GraphicsItem t ) => Scene.Add( t );
		public GraphicsWidget Add( Widget t ) => Scene.Add( t );

		public Action OnSelectionChanged { get; set; }


		public enum DragTypes
		{
			None,
			Scroll,
			SelectionRect
		}

		DragTypes _dragType = DragTypes.None;

		/// <summary>
		/// What happens when the user drags the mouse. You generally want to toggle this in
		/// OnMouseDown to switch what happens with different mouse buttons.
		/// </summary>
		public DragTypes DragType
		{
			get => _dragType;
			set
			{
				_dragType = value;
				_graphicsview.setDragMode( _dragType );
			}
		}
	}
}
