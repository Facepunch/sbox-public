namespace Sandbox;

public partial class GameObject
{
	internal class GameObjectHandle
	{
		public Texture Texture { get; set; }
		public string Icon { get; set; }
		public Color Color { get; set; }
	}

	GameObjectHandle _handle;
	bool handleBuilt;

	[Obsolete( "Use HasGizmoHandle" )]
	public bool HasGimzoHandle { get => HasGizmoHandle; private set => HasGizmoHandle = value; }
	public bool HasGizmoHandle { get; private set; }

	void BuildGizmoDetails()
	{
		if ( handleBuilt )
			return;

		_handle = default;

		var handles = Components.GetAll()
			.Where( x => x is not null )
			.Select( x => Game.TypeLibrary.GetType( x.GetType() ) )
			.Where( x => x is not null )
			.SelectMany( x => x.GetAttributes<EditorHandleAttribute>( true ) )
			.FirstOrDefault();

		if ( handles is null )
			return;

		_handle = new GameObjectHandle();
		_handle.Icon = handles.Icon;
		_handle.Color = handles.Color;

		var colorProvider = Components.GetAll<Component.IColorProvider>().FirstOrDefault();
		if ( colorProvider is not null )
		{
			_handle.Color = colorProvider.ComponentColor;

			// this is mainly for lights, we don't want any black bulbs, but we do want to indicate light color
			// so if anything else starts using this we should probably move this logic into the light component implementation
			_handle.Color = ((Vector3)_handle.Color).Normal * 2;
		}

		if ( !string.IsNullOrWhiteSpace( handles.Texture ) )
		{
			_handle.Texture = Texture.Load( handles.Texture );
		}
	}

	void DrawGizmoHandle( ref bool clicked )
	{
		HasGizmoHandle = false;

		if ( !Gizmo.Settings.GizmosEnabled ) return;

		BuildGizmoDetails();

		if ( _handle is null )
			return;

		bool isSelected = Gizmo.IsSelected;
		bool selected = Gizmo.IsSelected;

		using ( Gizmo.Scope( "Handle" ) )
		{
			Gizmo.Transform = Gizmo.Transform.WithScale( 1.0f );

			float size = 32;

			if ( !selected )
			{
				Gizmo.Hitbox.DepthBias = 0.1f;
				Gizmo.Hitbox.Sprite( 0, size * Gizmo.Settings.GizmoScale, false );

				clicked = clicked || Gizmo.WasClicked;
			}

			float opacity = 0.6f;

			if ( Gizmo.IsHovered ) opacity = 1;
			if ( isSelected ) opacity = 1;

			if ( Gizmo.IsHovered && Gizmo.Settings.Selection ) size = 40;

			Gizmo.Draw.IgnoreDepth = true;

			if ( _handle.Texture is not null )
			{
				//
				// Texture mode
				//

				Gizmo.Draw.Color = isSelected ? Color.Yellow : _handle.Color;
				Gizmo.Draw.Sprite( Vector3.Zero, size * Gizmo.Settings.GizmoScale, _handle.Texture, false );
			}
			else if ( _handle.Icon is not null )
			{
				//
				// Icon mode
				//

				var text = new TextRendering.Scope( _handle.Icon, _handle.Color, 64, "Material Icons", 400 );
				text.Shadow = new TextRendering.Shadow { Enabled = true, Color = Color.Black, Offset = 2, Size = 8 };
				var tex = TextRendering.GetOrCreateTexture( text, flag: TextFlag.Center );
				if ( tex is not null )
				{
					Gizmo.Draw.Color = Color.White.WithAlphaMultiplied( opacity );
					Gizmo.Draw.Sprite( Vector3.Zero, size * Gizmo.Settings.GizmoScale, tex, false );
				}
			}
		}

		HasGizmoHandle = true;
	}

	internal void DrawGizmos()
	{
		if ( !Active ) return;
		var parentTx = Gizmo.Transform;

		var tx = LocalTransform;

		// Absolute gameobject transform need to be converted back to local because it's already in worldspace
		if ( Flags.Contains( GameObjectFlags.Absolute ) )
		{
			tx = parentTx.ToLocal( tx );
		}

		using ( Gizmo.ObjectScope( this, tx ) )
		{
			bool clicked = Gizmo.WasClicked;

			if ( Gizmo.Settings.GizmosEnabled )
			{
				DrawGizmoHandle( ref clicked );

				Components.ForEach( "DrawGizmos", false, c =>
				{
					if ( !c.Flags.Contains( ComponentFlags.Hidden ) )
					{
						using var scope = Gizmo.Scope();
						c.DrawGizmosInternal();
						clicked = clicked || Gizmo.WasClicked;
					}
				} );
			}

			if ( clicked )
			{
				GizmoSelect();
			}

			//
			// If we pressed on this, but then moved the mouse a lot, clear the pressed state
			//
			if ( Gizmo.Pressed.This && Gizmo.CursorDragDelta.Length > 10 )
			{
				Gizmo.Pressed.ClearPath();
			}

			ForEachChild( "DrawGizmos", false, c =>
			{
				if ( !c.Flags.Contains( GameObjectFlags.Hidden ) )
				{
					c.DrawGizmos();
				}
			} );

			DrawBoneGizmo();
		}
	}

	void DrawBoneGizmo()
	{
		if ( !Gizmo.Settings.GizmosEnabled )
			return;

		if ( !Flags.Contains( GameObjectFlags.Bone ) )
			return;

		if ( !Parent.IsValid() )
			return;

		if ( !Parent.Flags.Contains( GameObjectFlags.Bone ) )
			return;

		var distance = Root.WorldPosition.Distance( Gizmo.Camera.Position );
		if ( distance > 500.0f )
			return;

		var position = WorldTransform.PointToLocal( Parent.WorldPosition );
		var length = position.Length * 0.5f;
		var width = length * 0.1f;

		if ( width.AlmostEqual( 0.0f ) )
			return;

		using ( Gizmo.Scope( "Bone" ) )
		{
			Gizmo.Hitbox.DepthBias *= 0.2f;
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.2f );
			Gizmo.Draw.LineThickness = 1;

			Gizmo.Draw.Line( 0, position );

			Gizmo.Draw.Color = Gizmo.IsSelected ? Gizmo.Colors.Active : Gizmo.IsHovered ? Color.White : Color.White.WithAlpha( 0.2f );
			Gizmo.Draw.LineThickness = Gizmo.IsSelected ? 2 : Gizmo.IsHovered ? 2 : 1;

			Gizmo.Draw.Sprite( 0, 0.4f, Texture.White );

			Gizmo.Hitbox.Sphere( new Sphere( 0, 0.4f ) );

			if ( Gizmo.WasClicked )
				GizmoSelect();
		}
	}

	/// <summary>
	/// Finds the first GameObject in the ancestor chain that we consider a selection base.
	/// </summary>
	GameObject FindSelectionBase()
	{
		var isSelectionBase = IsNetworkRoot || IsOutermostPrefabInstanceRoot || Components.GetAll().Any( x => Game.TypeLibrary.GetType( x?.GetType() )?.HasAttribute<SelectionBaseAttribute>() == true );

		if ( isSelectionBase ) return this;

		if ( Parent.IsValid() ) return Parent.FindSelectionBase();

		return null;
	}

	void GizmoSelect()
	{
		if ( !Gizmo.Settings.Selection )
			return;

		// Find the best candidate to select
		var selectionBase = FindSelectionBase();

		if ( selectionBase != null && selectionBase != this )
		{
			// If the selectionbase is already selected, we don't want to select it again, we want to switch the selection to the child
			// So when you double click an object that is descendant of the selectionbase you will be able to select the nested object.
			if ( !Gizmo.Active.Selection.Contains( selectionBase ) )
			{
				selectionBase.GizmoSelect();
				return;
			}
		}

		using ( Gizmo.ObjectScope( this, LocalTransform ) )
		{
			using ( Scene.Editor?.UndoScope( $"Select {Name}" ).Push() )
			{
				Gizmo.Select();
			}
		}
	}

}
