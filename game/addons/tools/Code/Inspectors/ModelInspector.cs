using Editor.Assets;
using static Editor.Inspectors.AssetInspector;

namespace Editor.Inspectors;

[CanEdit( "asset:vmdl" )]
public class ModelInspector : Widget, IAssetInspector
{
	private Model _model;
	private AssetPreview _assetPreview;

	private readonly CollapsibleCategory _infoGroup;
	private readonly CollapsibleCategory _meshGroup;
	private readonly CollapsibleCategory _animationGroup;
	private readonly CollapsibleCategory _parameterGroup;
	private readonly CollapsibleCategory _attachmentGroup;
	private readonly CollapsibleCategory _skeletonGroup;
	private readonly AnimationList _animations;
	private readonly AnimationParameterList _parameters;
	private readonly AttachmentList _attachments;
	private readonly BoneList _bones;

	public ModelInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		_infoGroup = AddGroup( "Stats", "info" );
		_meshGroup = AddGroup( "Meshes", "view_in_ar" );
		_animationGroup = AddGroup( "Animations", "directions_run" );
		_parameterGroup = AddGroup( "Parameters", "account_tree" );
		_skeletonGroup = AddGroup( "Skeleton", "accessibility" );
		_attachmentGroup = AddGroup( "Attachments", "hub" );

		_animations = new AnimationList( null );
		_animations.ItemSelected = PlayAnimation;
		_animationGroup.Container.Layout.Add( _animations );

		_parameters = new AnimationParameterList( null );
		_parameterGroup.Container.Layout.Add( _parameters );

		_bones = new BoneList( null );
		_skeletonGroup.Container.Layout.Add( _bones );

		_attachments = new AttachmentList( null );
		_attachmentGroup.Container.Layout.Add( _attachments );
	}

	private CollapsibleCategory AddGroup( string title, string icon )
	{
		var group = new CollapsibleCategory( null, title, icon );
		group.StateCookieName = $"ModelInspector.{title}";
		group.Visible = false;
		Layout.Add( group );
		return group;
	}

	private void PlayAnimation( string name )
	{
		if ( _assetPreview is null )
			return;

		if ( _assetPreview.PrimaryObject?.GetComponent<SkinnedModelRenderer>() is { } smr )
		{
			smr.UseAnimGraph = string.IsNullOrWhiteSpace( name );
			smr.Sequence.Name = name;
		}
	}

	private class AnimationParameterCollection : AnimationParameterList.IAccessor
	{
		public SerializedProperty ParentProperty => null;

		private readonly AssetPreview preview;
		private SkinnedModelRenderer SceneModel => preview?.PrimaryObject?.GetComponent<SkinnedModelRenderer>();

		private readonly HashSet<string> _parameters = new( StringComparer.OrdinalIgnoreCase );

		public AnimationParameterCollection( AssetPreview preview )
		{
			this.preview = preview;
		}

		public bool GetBool( string name ) => SceneModel.IsValid() && SceneModel.GetBool( name );
		public float GetFloat( string name ) => SceneModel.IsValid() ? SceneModel.GetFloat( name ) : default;
		public int GetInt( string name ) => SceneModel.IsValid() ? SceneModel.GetInt( name ) : default;
		public Rotation GetRotation( string name ) => SceneModel.IsValid() ? SceneModel.GetRotation( name ) : default;
		public Vector3 GetVector( string name ) => SceneModel.IsValid() ? SceneModel.GetVector( name ) : default;

		public void Set( string name, bool value ) { SceneModel?.Set( name, value ); _parameters.Add( name ); }
		public void Set( string name, float value ) { SceneModel?.Set( name, value ); _parameters.Add( name ); }
		public void Set( string name, Vector3 value ) { SceneModel?.Set( name, value ); _parameters.Add( name ); }
		public void Set( string name, int value ) { SceneModel?.Set( name, value ); _parameters.Add( name ); }
		public void Set( string name, Rotation value ) { SceneModel?.Set( name, value ); _parameters.Add( name ); }

		public void Clear()
		{
			SceneModel?.Reset();
			_parameters.Clear();
		}

		public void Clear( string name ) => _parameters.Remove( name );
		public bool Contains( string name ) => _parameters.Contains( name );
	}

	public void SetAssetPreview( AssetPreview preview )
	{
		_assetPreview = preview;
		_parameters.SetAccessor( new AnimationParameterCollection( preview ) );
	}

	public void SetAsset( Asset asset )
	{
		_model = asset.LoadResource<Model>();
		if ( _model == null || _model.IsError )
			return;

		var meshInfo = _model.MeshInfo;

		_infoGroup.Container.Layout.Clear( true );
		BuildInfoGroup( _model, meshInfo );
		_infoGroup.Visible = true;

		// Per-mesh breakdown grouped by LOD
		if ( _model.MeshCount > 0 )
		{
			_meshGroup.Container.Layout.Clear( true );
			BuildMeshGroups( _model, meshInfo );
			_meshGroup.Visible = true;
		}

		if ( _model.AnimationCount > 0 )
		{
			_animationGroup.Visible = true;
			_animations.SetModel( _model );
		}

		var animgraph = _model.AnimGraph;
		if ( animgraph != null && animgraph.ParamCount > 0 )
		{
			_parameterGroup.Visible = true;
			_parameters.SetGraph( animgraph );
		}

		if ( _model.Attachments.Count > 0 )
		{
			_attachmentGroup.Visible = true;
			_attachments.SetModel( _model );
		}

		if ( _model.BoneCount > 0 )
		{
			_skeletonGroup.Visible = true;
			_bones.SetModel( _model );
		}
	}

	static int GetLowestLodLevel( int mask )
	{
		if ( mask == 0 ) return 0;
		for ( int i = 0; i < 8; i++ )
			if ( (mask & (1 << i)) != 0 ) return i;
		return 0;
	}

	static string FormatNumber( int n ) => n.ToString( "N0" );

	static string GetTranslucencyTypeName( int type ) => type switch
	{
		1 => "Partial",
		2 => "Full",
		_ => "Opaque"
	};

	static string GetPrimitiveTypeName( int primType ) => primType switch
	{
		0 => "Points",
		1 => "Lines",
		2 => "Lines w/ Adjacency",
		3 => "Line Strip",
		4 => "Line Strip w/ Adjacency",
		5 => "Triangles",
		6 => "Triangles w/ Adjacency",
		7 => "Triangle Strip",
		8 => "Triangle Strip w/ Adjacency",
		9 => "Instanced Quads",
		10 => "Heterogenous",
		_ => $"Unknown ({primType})"
	};

	void BuildInfoGroup( Model model, Model.ModelMeshInfo meshInfo )
	{
		var layout = _infoGroup.Container.Layout;

		layout.Add( new InfoRow( null, "scatter_plot", "Vertices", FormatNumber( meshInfo.TotalVertices ) ) );
		layout.Add( new InfoRow( null, "change_history", "Triangles", FormatNumber( meshInfo.TotalTriangles ) ) );
		layout.Add( new InfoRow( null, "view_in_ar", "Meshes", FormatNumber( model.MeshCount ) ) );
		layout.Add( new InfoRow( null, "brush", "Draw Calls", FormatNumber( meshInfo.TotalDrawCalls ) ) );
		layout.Add( new InfoRow( null, "palette", "Materials", FormatNumber( model.Materials.Length ) ) );

		var size = model.RenderBounds.Size;
		layout.Add( new InfoRow( null, "straighten", "Bounds", $"{size.x:0.#} × {size.y:0.#} × {size.z:0.#}" ) );
	}

	void BuildMeshGroups( Model model, Model.ModelMeshInfo meshInfo )
	{
		bool hasLods = meshInfo.LodCount > 1;
		var layout = _meshGroup.Container.Layout;

		var meshes = meshInfo.Meshes.Select( ( info, index ) => (Index: index, Info: info) ).ToList();

		meshes.Sort( ( a, b ) =>
		{
			int lodA = GetLowestLodLevel( a.Info.LodMask );
			int lodB = GetLowestLodLevel( b.Info.LodMask );
			int cmp = lodA.CompareTo( lodB );
			return cmp != 0 ? cmp : a.Index.CompareTo( b.Index );
		} );

		if ( !hasLods )
		{
			foreach ( var (index, info) in meshes )
			{
				var title = string.IsNullOrEmpty( info.Name ) ? $"Mesh {index}" : info.Name;
				var meshGroup = new MeshSubGroup( null, title );
				meshGroup.StateCookieName = $"{nameof( ModelInspector )}.Mesh{index}";
				BuildMeshInfoRows( info, meshGroup );
				BuildDrawCallGroups( index, info, meshGroup );
				layout.Add( meshGroup );
			}
			return;
		}

		// Group by LOD level with collapsible sub-groups
		var groups = meshes.GroupBy( m => GetLowestLodLevel( m.Info.LodMask ) ).OrderBy( g => g.Key );
		foreach ( var lodGroup in groups )
		{
			int lodTris = lodGroup.Sum( m => m.Info.Triangles );
			int lodVerts = lodGroup.Sum( m => m.Info.Vertices );

			var subGroup = new MeshSubGroup( null, $"LOD {lodGroup.Key}" );
			subGroup.StateCookieName = $"{nameof( ModelInspector )}.LOD{lodGroup.Key}";

			subGroup.AddRow( new InfoRow( null, "scatter_plot", "Vertices", FormatNumber( lodVerts ) ) );
			subGroup.AddRow( new InfoRow( null, "change_history", "Triangles", FormatNumber( lodTris ) ) );

			foreach ( var (index, info) in lodGroup )
			{
				var title = string.IsNullOrEmpty( info.Name ) ? $"Mesh {index}" : info.Name;
				var meshGroup = new MeshSubGroup( null, title );
				meshGroup.StateCookieName = $"{nameof( ModelInspector )}.Mesh{index}";
				BuildMeshInfoRows( info, meshGroup );
				BuildDrawCallGroups( index, info, meshGroup );
				subGroup.AddRow( meshGroup );
			}

			layout.Add( subGroup );
		}
	}

	void BuildMeshInfoRows( Model.ModelMeshInfo.MeshData info, MeshSubGroup group )
	{
		group.AddRow( new InfoRow( null, "scatter_plot", "Vertices", FormatNumber( info.Vertices ) ) );
		group.AddRow( new InfoRow( null, "change_history", "Triangles", FormatNumber( info.Triangles ) ) );

		if ( info.TranslucencyType != 0 )
			group.AddRow( new InfoRow( null, "opacity", "Translucency", GetTranslucencyTypeName( info.TranslucencyType ) ) );

		var size = info.BoundsMax - info.BoundsMin;
		if ( size.Length > 0 )
			group.AddRow( new InfoRow( null, "straighten", "Bounds", $"{size.x:0.#} × {size.y:0.#} × {size.z:0.#}" ) );
	}

	void BuildDrawCallGroups( int meshIndex, Model.ModelMeshInfo.MeshData mesh, MeshSubGroup parent )
	{
		for ( int d = 0; d < mesh.DrawCalls.Length; d++ )
		{
			var dc = mesh.DrawCalls[d];

			var drawGroup = new MeshSubGroup( null, $"Draw Call {d}" );
			drawGroup.StateCookieName = $"{nameof( ModelInspector )}.Mesh{meshIndex}.Draw{d}";

			drawGroup.AddRow( new InfoRow( null, "scatter_plot", "Vertices", FormatNumber( dc.Vertices ) ) );
			drawGroup.AddRow( new InfoRow( null, "tag", "Indices", FormatNumber( dc.Indices ) ) );
			drawGroup.AddRow( new InfoRow( null, "category", "Primitive", GetPrimitiveTypeName( dc.PrimitiveType ) ) );

			if ( dc.UvDensity > 0 )
				drawGroup.AddRow( new InfoRow( null, "grid_on", "UV Density", $"{dc.UvDensity:0.##}" ) );

			if ( dc.InstanceCount > 1 )
				drawGroup.AddRow( new InfoRow( null, "content_copy", "Instances", FormatNumber( dc.InstanceCount ) ) );

			if ( dc.AlphaBlended )
				drawGroup.AddRow( new InfoRow( null, "opacity", "Alpha Blended", "Yes" ) );

			if ( dc.Material is not null )
				drawGroup.AddRow( new InfoRow( null, "palette", "Material", dc.Material.Name ) );

			parent.AddRow( drawGroup );
		}
	}

	private class BoneList : Widget
	{
		private readonly TreeView TreeView;
		private Model Model;

		public BoneList( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			Layout.Margin = 4;
			Layout.Spacing = 4;

			TreeView = new TreeView( this )
			{
				Margin = 4
			};

			var filter = new LineEdit( this )
			{
				PlaceholderText = $"Filter Bones..",
				FixedHeight = 25
			};

			filter.TextEdited += ( t ) =>
			{
				if ( string.IsNullOrWhiteSpace( t ) )
				{
					SetModel( Model );
				}
				else
				{
					TreeView.SetItems( Model.Bones.AllBones
						.Where( x => x.Name.Contains( t, StringComparison.OrdinalIgnoreCase ) )
						.OrderBy( x => x.Name )
						.Select( x => new BoneTreeNode( x, false ) ) );
				}
			};

			Layout.Add( filter );
			Layout.Add( TreeView, 1 );
		}

		public void SetModel( Model model )
		{
			Model = model;
			TreeView.SetItems( Model.Bones.AllBones
				.Where( x => x.Parent is null )
				.OrderBy( x => x.Name )
				.Select( x => new BoneTreeNode( x, true ) ) );

			foreach ( var item in TreeView.Items )
				TreeView.Open( item );
		}

		class BoneTreeNode : TreeNode
		{
			private readonly BoneCollection.Bone Bone;
			private readonly bool ShowChildren;

			public BoneTreeNode( BoneCollection.Bone bone, bool showChildren )
			{
				Bone = bone;
				ShowChildren = showChildren;
			}

			public override bool OnContextMenu()
			{
				var m = new ContextMenu();
				m.AddOption( "Copy", "content_copy", () => EditorUtility.Clipboard.Copy( Bone.Name ) );
				m.OpenAtCursor();
				return true;
			}

			public override void OnPaint( VirtualWidget item )
			{
				PaintSelection( item );

				Paint.Antialiasing = true;

				var fg = Paint.HasSelected || Paint.HasMouseOver ? Color.White : Color.White.Darken( 0.2f );
				var iconRect = item.Rect;
				iconRect.Width = iconRect.Height;
				Paint.Draw( iconRect.Shrink( 4 ), "modeldoc_editor/preview_outliner_bone.png" );

				Paint.SetDefaultFont();
				Paint.SetPen( fg );
				Paint.DrawText( item.Rect.Shrink( 24, 0, 0, 0 ), Bone.Name, TextFlag.LeftCenter );
			}

			protected override void BuildChildren()
			{
				if ( !ShowChildren )
					return;

				SetItems( Bone.Children.Select( x => new BoneTreeNode( x, true ) ) );
			}
		}
	}

	private class AnimationList : ItemList
	{
		public override string ItemName => "Animation";
		public override string ItemIcon => "animgraph_editor/single_frame_icon.png";

		public AnimationList( Widget parent ) : base( parent ) { }

		public override void SetModel( Model model )
		{
			Items = Enumerable.Range( 0, model.AnimationCount )
				.Select( model.GetAnimationName )
				.OrderBy( x => x )
				.ToList();

			ListView.SetItems( Items );
		}
	}

	private class AttachmentList : ItemList
	{
		public override string ItemName => "Attachment";
		public override string ItemIcon => "modeldoc_editor/preview_outliner_attachment.png";

		public AttachmentList( Widget parent ) : base( parent ) { }

		static string GetName( ModelAttachments.Attachment att )
		{
			if ( att.Bone is null ) return att.Name;

			return $"{att.Name} (on bone {att.Bone.Name})";
		}

		public override void SetModel( Model model )
		{
			Items = model.Attachments.All
				.Select( x => GetName( x ) )
				.OrderBy( x => x )
				.ToList();

			ListView.SetItems( Items );
		}
	}

	private abstract class ItemList : Widget
	{
		protected readonly ListView ListView;
		protected List<string> Items;

		public abstract string ItemName { get; }
		public abstract string ItemIcon { get; }

		public Action<string> ItemSelected { get; set; }

		public abstract void SetModel( Model model );

		public ItemList( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			Layout.Margin = 4;
			Layout.Spacing = 4;

			ListView = new ListView( this )
			{
				ItemSize = new Vector2( 0, 25 ),
				Margin = new( 4, 4, 16, 4 ),
				ItemPaint = PaintAnimationItem,
				ItemContextMenu = ShowItemContext,
				ToggleSelect = true,
				ItemSelected = ( o ) => ItemSelected?.Invoke( o as string ),
				ItemDeselected = ( o ) => ItemSelected?.Invoke( null ),
			};

			var filter = new LineEdit( this )
			{
				PlaceholderText = $"Filter {ItemName}s..",
				FixedHeight = 25
			};

			filter.TextEdited += ( t ) =>
			{
				ListView.SetItems( Items == null || Items.Count == 0 ? null : string.IsNullOrWhiteSpace( t ) ? Items :
					Items.Where( x => x.Contains( t, StringComparison.OrdinalIgnoreCase ) ) );
			};

			Layout.Add( filter );
			Layout.Add( ListView, 1 );
		}

		private void ShowItemContext( object obj )
		{
			if ( obj is not string name ) return;

			var m = new ContextMenu();
			m.AddOption( "Copy", "content_copy", () => EditorUtility.Clipboard.Copy( name ) );
			m.OpenAtCursor();
		}

		private void PaintAnimationItem( VirtualWidget v )
		{
			if ( v.Object is not string name )
				return;

			var rect = v.Rect;

			Paint.Antialiasing = true;

			var fg = Color.White.Darken( 0.2f );

			if ( Paint.HasSelected )
			{
				fg = Color.White;
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.5f ) );
				Paint.DrawRect( rect, 2 );
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.4f ) );
			}
			else if ( Paint.HasMouseOver )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.25f ) );
				Paint.DrawRect( rect, 2 );
			}

			var iconRect = rect.Shrink( 8, 4 );
			iconRect.Width = iconRect.Height;
			Paint.Draw( iconRect, ItemIcon );

			var textRect = rect.Shrink( 4 );
			textRect.Left = iconRect.Right + 8;

			Paint.SetDefaultFont();
			Paint.SetPen( fg );
			Paint.DrawText( textRect, name, TextFlag.LeftCenter );
		}
	}

	private class InfoRow : Widget
	{
		readonly string Icon;
		readonly string Label;
		readonly string Value;

		public InfoRow( Widget parent, string icon, string label, string value ) : base( parent )
		{
			Icon = icon;
			Label = label;
			Value = value;
			FixedHeight = 20;
		}

		protected override void OnPaint()
		{
			var rect = LocalRect;
			Paint.Antialiasing = true;

			float iconSize = 14;
			if ( !string.IsNullOrEmpty( Icon ) )
			{
				var iconRect = new Rect( 8, (rect.Height - iconSize) / 2, iconSize, iconSize );
				Paint.SetPen( Theme.Text.WithAlpha( 0.4f ) );
				Paint.DrawIcon( iconRect, Icon, iconSize );
			}

			float textLeft = string.IsNullOrEmpty( Icon ) ? 8 : 30;

			Paint.SetDefaultFont( size: 8, weight: 400 );
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( new Rect( textLeft, 0, 100, rect.Height ), Label, TextFlag.LeftCenter );

			Paint.SetPen( Theme.Text.WithAlpha( 0.8f ) );
			Paint.DrawText( new Rect( 120, 0, rect.Width - 128, rect.Height ), Value, TextFlag.LeftCenter );
		}
	}

	private class MeshSubGroup : Widget
	{
		readonly string Title;
		readonly Widget Body;
		bool IsOpen;

		string _stateCookieName;
		public string StateCookieName
		{
			get => _stateCookieName;
			set
			{
				_stateCookieName = value;
				IsOpen = EditorCookie.Get( _stateCookieName, IsOpen );
				Body.Visible = IsOpen;
			}
		}

		public MeshSubGroup( Widget parent, string title ) : base( parent )
		{
			Title = title;
			IsOpen = false;
			Cursor = CursorShape.Finger;

			Layout = Layout.Column();
			Layout.Margin = new Margin( 0, 0, 12, 0 );

			// Header spacer
			Layout.AddSpacingCell( Theme.RowHeight );

			Body = new Widget( null );
			Body.Layout = Layout.Column();
			Body.Layout.Margin = new Margin( 12, 4, 0, 4 );
			Body.Visible = IsOpen;
			Layout.Add( Body );
		}

		public void AddRow( Widget row )
		{
			Body.Layout.Add( row );
		}

		protected override void OnMousePress( MouseEvent e )
		{
			if ( e.Button == MouseButtons.Left && e.LocalPosition.y < Theme.RowHeight )
			{
				IsOpen = !IsOpen;
				Body.Visible = IsOpen;

				if ( _stateCookieName is not null )
					EditorCookie.Set( _stateCookieName, IsOpen );

				Update();
			}
		}

		protected override void OnPaint()
		{
			var headerRect = new Rect( 0, 0, Width, Theme.RowHeight );

			// +/- circle button
			var circleRect = headerRect.Shrink( 3, 4, 0, 4 );
			circleRect.Height = 22 - 8;
			circleRect.Width = circleRect.Height;

			var backgroundColor = Theme.WindowBackground.Lighten( 0.2f ).WithAlphaMultiplied( IsOpen ? 1f : 0.5f );
			Paint.SetBrushAndPen( backgroundColor );
			Paint.DrawRect( circleRect, 6 );

			Paint.ClearBrush();

			var iconIntensity = IsUnderMouse ? 1.5f : 1f;
			if ( IsOpen )
			{
				Paint.Pen = Theme.TextControl.WithAlpha( 0.2f * iconIntensity );
				Paint.DrawIcon( circleRect, "remove", 12, TextFlag.Center );
			}
			else
			{
				Paint.Pen = Theme.TextControl.WithAlpha( 0.4f * iconIntensity );
				Paint.DrawIcon( circleRect, "add", 12, TextFlag.Center );
			}

			// Title text
			Paint.Pen = Theme.TextControl.WithAlpha( IsOpen ? 1f : 0.8f );
			Paint.SetDefaultFont( 11, weight: 400, sizeInPixels: true );
			Paint.DrawText( headerRect.Shrink( circleRect.Right + 5, 0, 0, 0 ), Title, TextFlag.LeftCenter );

			// Vertical line on left when expanded
			if ( IsOpen )
			{
				var lineRect = LocalRect.Shrink( 8, Theme.RowHeight, 0, 0 );
				lineRect.Width = 3;
				Paint.SetBrushAndPen( Theme.WindowBackground );
				Paint.DrawRect( lineRect, 5 );

				var bottomRect = LocalRect.Shrink( 10, 0, 0, 0 );
				bottomRect.Top = bottomRect.Bottom - 3;
				bottomRect.Width = 8;
				Paint.SetBrushAndPen( Theme.WindowBackground );
				Paint.DrawRect( bottomRect, 5 );
			}
		}
	}
}

public class AnimationParameterList : Widget
{
	private readonly ControlSheet Sheet;
	private AnimationGraph Graph;
	private IAccessor Accessor;

	public interface IAccessor
	{
		/// <summary>
		/// Optional property that contains the parameter list when inspecting,
		/// for example <see cref="SkinnedModelRenderer.Parameters"/>.
		/// </summary>
		SerializedProperty ParentProperty { get; }

		void Set( string name, bool value );
		void Set( string name, float value );
		void Set( string name, Vector3 value );
		void Set( string name, int value );
		void Set( string name, Rotation value );

		Rotation GetRotation( string name );
		Vector3 GetVector( string name );
		bool GetBool( string name );
		float GetFloat( string name );
		int GetInt( string name );

		void Clear();
		void Clear( string name );
		bool Contains( string name );
	}

	public void SetAccessor( IAccessor accessor )
	{
		if ( Graph is null )
			return;

		Accessor = accessor;

		Sheet.Clear( true );
		Sheet.AddObject( new GraphParamsObject( Graph, Accessor ) );
	}

	public void SetGraph( AnimationGraph graph )
	{
		Graph = graph;
	}

	public AnimationParameterList( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		var headerButtons = Layout.AddRow();
		headerButtons.Spacing = 8;

		{
			var btn = headerButtons.Add( new Button( "Clear" ) );
			btn.Clicked = () =>
			{
				Accessor?.Clear();
				Update();
			};
		}

		Sheet = new ControlSheet();
		Layout.Add( Sheet, 1 );
	}
}

#region Serialized Object

/// <summary>
/// <see cref="SerializedObject"/> with properties for each parameter of an <see cref="AnimationGraph"/>.
/// </summary>
file class GraphParamsObject : SerializedObject
{
	public AnimationGraph Graph { get; }
	public AnimationParameterList.IAccessor Accessor { get; }

	public GraphParamsObject( AnimationGraph graph, AnimationParameterList.IAccessor accessor )
	{
		Graph = graph;
		Accessor = accessor;
		ParentProperty = accessor.ParentProperty;

		UpdatePropertyList();
	}

	private void UpdatePropertyList()
	{
		PropertyList = new List<SerializedProperty>();

		var parameterIndices = Enumerable.Range( 0, Graph.ParamCount )
			.OrderBy( Graph.GetParameterName );

		foreach ( var index in parameterIndices )
		{
			if ( ParamProperty.Create( this, index ) is { } property )
			{
				PropertyList.Add( property );
			}
		}
	}
}

/// <summary>
/// Base class for animation parameter properties contained in a <see cref="GraphParamsObject"/>.
/// </summary>
file abstract class ParamProperty( GraphParamsObject parent, string name, Type type ) : SerializedProperty
{
	public static ParamProperty Create( GraphParamsObject parent, int index )
	{
		var name = parent.Graph.GetParameterName( index );
		var type = parent.Graph.GetParameterType( index );

		if ( type == typeof( float ) )
		{
			return new FloatParamProperty( parent, name );
		}

		if ( type == typeof( int ) )
		{
			return new IntParamProperty( parent, name );
		}

		if ( type == typeof( Vector3 ) )
		{
			return new Vector3ParamProperty( parent, name );
		}

		if ( type == typeof( Rotation ) )
		{
			return new RotationParamProperty( parent, name );
		}

		if ( type == typeof( bool ) )
		{
			return new BoolParamProperty( parent, name );
		}

		if ( type == typeof( byte ) )
		{
			return new EnumParamProperty( parent, name );
		}

		return null;
	}

	private GraphParamsObject _parent = parent;

	public override SerializedObject Parent => _parent;
	public override Type PropertyType { get; } = type.IsValueType ? typeof( Nullable<> ).MakeGenericType( type ) : type;
	public override string DisplayName => Name;
	public override string Name { get; } = name;
	public override string Description => Name;

	protected AnimationParameterList.IAccessor Accessor => _parent.Accessor;

	public bool Enabled
	{
		get => Accessor.Contains( Name );
	}
}

/// <summary>
/// Typed <see cref="ParamProperty"/> that implements getting / setting.
/// </summary>
file abstract class ParamProperty<TValue> : ParamProperty
{
	private IReadOnlyList<Attribute> _attributes;

	protected AnimParam<TValue> Parameter { get; }

	public abstract TValue Value { get; set; }

	protected ParamProperty( GraphParamsObject parent, string name )
		: base( parent, name, typeof( TValue ) )
	{
		Parameter = parent.Graph.GetParameter<TValue>( name );
	}

	protected void SetAttributes( params Attribute[] attributes )
	{
		_attributes = attributes;
	}

	public override IEnumerable<Attribute> GetAttributes() => _attributes ?? Enumerable.Empty<Attribute>();

	public override T GetValue<T>( T defaultValue = default )
	{
		return ValueToType( Enabled ? Value : null, defaultValue );
	}

	public override void SetValue<T>( T value )
	{
		if ( value is null )
		{
			SetValue( GetDefault() );
			Accessor.Clear( Name );

			return;
		}

		Value = ValueToType( value, Value );
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		var typeDesc = EditorTypeLibrary.GetType<TValue>();
		obj = EditorTypeLibrary.GetSerializedObject( () => Value, typeDesc, this );
		return true;
	}
}

file class FloatParamProperty : ParamProperty<float>
{
	public Vector2 Range { get; private init; }

	public FloatParamProperty( GraphParamsObject parent, string name )
		: base( parent, name )
	{
		Range = new Vector2( Parameter.MinValue, Parameter.MaxValue );

		SetAttributes( new RangeAttribute( Range.x, Range.y ), new DefaultValueAttribute( Parameter.DefaultValue ) );
	}

	public override float Value
	{
		get => Accessor.GetFloat( Name );
		set => Accessor.Set( Name, value );
	}
}

file class IntParamProperty : ParamProperty<int>
{
	public Vector2 Range { get; private init; }

	public override int Value
	{
		get => Accessor.GetInt( Name );
		set => Accessor.Set( Name, value );
	}

	public IntParamProperty( GraphParamsObject parent, string name )
		: base( parent, name )
	{
		Range = new Vector2( Parameter.MinValue, Parameter.MaxValue );

		SetAttributes( new RangeAttribute( Range.x, Range.y ), new DefaultValueAttribute( Parameter.DefaultValue ) );
	}
}

file class Vector3ParamProperty : ParamProperty<Vector3>
{
	public override Vector3 Value
	{
		get => Accessor.GetVector( Name );
		set => Accessor.Set( Name, value );
	}

	public Vector3ParamProperty( GraphParamsObject parent, string name )
		: base( parent, name )
	{
		SetAttributes( new DefaultValueAttribute( Parameter.DefaultValue ) );
	}
}

file class RotationParamProperty : ParamProperty<Rotation>
{
	public override Rotation Value
	{
		get => Accessor.GetRotation( Name );
		set => Accessor.Set( Name, value );
	}

	public RotationParamProperty( GraphParamsObject parent, string name )
		: base( parent, name )
	{
		SetAttributes( new DefaultValueAttribute( Parameter.DefaultValue ) );
	}
}

file class BoolParamProperty : ParamProperty<bool>
{
	public override bool Value
	{
		get => Accessor.GetBool( Name );
		set => Accessor.Set( Name, value );
	}

	public BoolParamProperty( GraphParamsObject parent, string name )
		: base( parent, name )
	{
		SetAttributes( new DefaultValueAttribute( Parameter.DefaultValue ) );
	}
}

file sealed class EnumParamAttribute : Attribute;

file class EnumParamProperty : ParamProperty<byte>
{
	public string[] Options { get; private init; }

	public override byte Value
	{
		get => (byte)Accessor.GetInt( Name );
		set => Accessor.Set( Name, value );
	}

	public EnumParamProperty( GraphParamsObject parent, string name )
		: base( parent, name )
	{
		Options = Parameter.OptionNames;

		SetAttributes( new EnumParamAttribute(), new DefaultValueAttribute( Parameter.DefaultValue ) );
	}
}

#endregion

[CustomEditor( typeof( byte ), WithAllAttributes = [typeof( EnumParamAttribute )] )]
file class EnumParamControlWidget : ControlWidget
{
	public override bool IsControlActive => base.IsControlActive || _menu.IsValid();
	public override bool IsControlButton => true;
	public override bool IsControlHovered => base.IsControlHovered || _menu.IsValid();
	public override bool SupportsMultiEdit => false;

	public string[] Options { get; set; }

	public EnumParamControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Spacing = 2;

		Options = (SerializedProperty as EnumParamProperty)?.Options;
	}

	protected override void PaintControl()
	{
		if ( Options is null )
			return;

		var value = SerializedProperty.GetValue<byte>();

		var color = IsControlHovered ? Theme.Blue : Theme.TextControl;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		var rect = LocalRect;

		rect = rect.Shrink( 8, 0 );

		Paint.SetPen( color );
		Paint.DrawText( rect, Options[value] ?? "Unset", TextFlag.LeftCenter );

		Paint.SetPen( color );
		Paint.DrawIcon( rect, "Arrow_Drop_Down", 17, TextFlag.RightCenter );
	}

	PopupWidget _menu;

	public override void StartEditing()
	{
		if ( IsControlDisabled ) return;

		if ( !_menu.IsValid )
		{
			OpenMenu();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		if ( e.LeftMouseButton && !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{

	}

	void OpenMenu()
	{
		PropertyStartEdit();

		_menu = new PopupWidget( null )
		{
			Layout = Layout.Column(),
			MinimumWidth = ScreenRect.Width,
			MaximumWidth = ScreenRect.Width
		};

		var scroller = _menu.Layout.Add( new ScrollArea( this ), 1 );
		scroller.Canvas = new Widget( scroller )
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand
		};

		for ( var i = 0; i < Options.Length; ++i )
		{
			var b = scroller.Canvas.Layout.Add( new MenuOption( Options[i], (byte)i, SerializedProperty ) );
			b.MouseLeftPress = () =>
			{
				SerializedProperty.SetValue( b.Value );

				PropertyFinishEdit();

				_menu.Update();
				_menu.Close();
			};
		}

		_menu.Position = ScreenRect.BottomLeft;
		_menu.Visible = true;
		_menu.AdjustSize();
		_menu.ConstrainToScreen();
		_menu.OnPaintOverride = PaintMenuBackground;
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
		return true;
	}
}

file class MenuOption : Widget
{
	private SerializedProperty _property;
	private byte _value;

	public byte Value => _value;

	public MenuOption( string name, byte value, SerializedProperty p ) : base( null )
	{
		_property = p;
		_value = value;

		Layout = Layout.Row();
		Layout.Margin = 8;

		Layout.AddSpacingCell( 8 );
		var c = Layout.AddColumn();
		var title = c.Add( new Label( name ) );
		title.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );
	}

	bool HasValue()
	{
		var value = _property.GetValue<byte>( 0 );
		return value == _value;
	}

	protected override void OnPaint()
	{
		if ( Paint.HasMouseOver || HasValue() )
		{
			Paint.SetBrushAndPen( Theme.Blue.WithAlpha( HasValue() ? 0.3f : 0.1f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), 2 );
		}
	}
}
