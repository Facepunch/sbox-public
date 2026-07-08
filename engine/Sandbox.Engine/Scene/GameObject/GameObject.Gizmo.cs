namespace Sandbox;

public partial class GameObject
{
	internal class GameObjectHandle
	{
		public Texture Texture { get; set; }
		public string Icon { get; set; }

		/// <summary>Base color from the <see cref="EditorHandleAttribute"/>, used when there's no color provider.</summary>
		public Color Color { get; set; }

		/// <summary>Color provider (usually a light), read live each frame so its color stays current.</summary>
		public Component.IColorProvider ColorProvider { get; set; }

		/// <summary>The color to draw the handle with this frame.</summary>
		public Color EffectiveColor
		{
			get
			{
				if ( ColorProvider is null )
					return Color;

				// this is mainly for lights, we don't want any black bulbs, but we do want to indicate light color
				// so if anything else starts using this we should probably move this logic into the light component implementation
				return ((Vector3)ColorProvider.ComponentColor).Normal * 2;
			}
		}
	}

	GameObjectHandle _handle;
	bool handleBuilt;
	int _handleGen = -1;

	[Obsolete( "Use HasGizmoHandle" )]
	public bool HasGimzoHandle { get => HasGizmoHandle; private set => HasGizmoHandle = value; }
	public bool HasGizmoHandle { get; private set; }

	void BuildGizmoDetails()
	{
		// Rebuilt only when the component list changes (ClearInternalCache) or a gizmo type is toggled.
		if ( handleBuilt && _handleGen == Gizmo.GizmoTypeGeneration )
			return;

		handleBuilt = true;
		_handleGen = Gizmo.GizmoTypeGeneration;
		_handle = null;

		EditorHandleAttribute handles = null;

		foreach ( var c in Components.GetAll() )
		{
			if ( c is null )
				continue;

			var typeDesc = Game.TypeLibrary.GetType( c.GetType() );
			if ( typeDesc is null )
				continue;

			if ( !Gizmo.Settings.IsGizmoEnabled( typeDesc.TargetType ) )
				continue;

			foreach ( var attr in typeDesc.GetAttributes<EditorHandleAttribute>( true ) )
			{
				handles = attr;
				break;
			}

			if ( handles is not null )
				break;
		}

		if ( handles is null )
			return;

		_handle = new GameObjectHandle();
		_handle.Icon = handles.Icon;
		_handle.Color = handles.Color;
		_handle.ColorProvider = Components.GetAll<Component.IColorProvider>().FirstOrDefault();

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

		var renderDistance = Gizmo.Settings.GizmoRenderDistance;
		if ( renderDistance > 0 )
		{
			var dist = Gizmo.Camera.Position.Distance( Gizmo.Transform.Position );
			if ( dist > renderDistance )
				return;
		}

		bool isSelected = Gizmo.IsSelected;
		bool selected = Gizmo.IsSelected;
		bool worldSpace = Gizmo.Settings.WorldSpaceGizmos;

		using ( Gizmo.Scope( "Handle" ) )
		{
			Gizmo.Transform = Gizmo.Transform.WithScale( 1.0f );

			float size = worldSpace ? 8 : 32;

			if ( !selected )
			{
				Gizmo.Hitbox.DepthBias = 0.1f;
				Gizmo.Hitbox.Sprite( 0, size * Gizmo.Settings.GizmoScale, worldSpace );

				clicked = clicked || Gizmo.WasClicked;
			}

			float opacity = 0.6f;

			if ( Gizmo.IsHovered ) opacity = 1;
			if ( isSelected ) opacity = 1;

			if ( Gizmo.IsHovered && Gizmo.Settings.Selection ) size = worldSpace ? 10 : 40;

			Gizmo.Draw.IgnoreDepth = !Gizmo.Settings.GizmoDepthTest;

			if ( _handle.Texture is not null )
			{
				//
				// Texture mode
				//

				Gizmo.Draw.Color = isSelected ? Color.Yellow : _handle.EffectiveColor;
				Gizmo.Draw.Sprite( Vector3.Zero, size * Gizmo.Settings.GizmoScale, _handle.Texture, worldSpace );
			}
			else if ( _handle.Icon is not null )
			{
				//
				// Icon mode
				//

				var text = new TextRendering.Scope( _handle.Icon, _handle.EffectiveColor, 64, "Material Icons", 400 );
				text.Shadow = new TextRendering.Shadow { Enabled = true, Color = Color.Black, Offset = 2, Size = 8 };
				var tex = TextRendering.GetOrCreateTexture( text, flag: TextFlag.Center );
				if ( tex is not null )
				{
					Gizmo.Draw.Color = Color.White.WithAlphaMultiplied( opacity );
					Gizmo.Draw.Sprite( Vector3.Zero, size * Gizmo.Settings.GizmoScale, tex, worldSpace );
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
					if ( c.OverridesDrawGizmos && !c.Flags.Contains( ComponentFlags.Hidden ) )
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
				if ( !c.Flags.Contains( GameObjectFlags.Hidden ) && !c.Tags.Has( "hidden" ) )
				{
					c.DrawGizmos();
				}
			} );

			DrawBoneGizmo();
		}
	}

	// Used to deterimine if a bone gizmo should be drawn as a bip or a bone
	static string[] _nonBipWhitelist = [
		"pelvis", "hips", "spine", "ribcage", "head", "neck",
		"shoulder", "collar", "clavicle", "arm", "elbow",
		"hand", "wrist", "palm", "finger", "digit", "meta",
		"index", "middle", "pinky", "ring", "thumb", "leg",
		"thigh", "knee", "calf", "ankle", "heel", "foot",
		"ball", "toe", "shin"
	];
	static string[] _nonBipBlacklist = [
		"twist", "mscl", "lookat", "ik", "targ", "trg",
		"tip", "end", "root", "reflex", "rfx", "dyn",
		"cloth", "attach", "attch", "phys", "upnode"
	];

	/// <summary>
	/// Try to guess based on names if a bone should be drawn as a bone (connected) or a bip (disconnected)
	/// </summary>
	static bool DrawBoneAsBip( string child, string parent = null )
	{
		var bip = true;
		// if the name has one of these substrings its probably a bone
		foreach ( var str in _nonBipWhitelist )
		{
			if ( !child.Contains( str, StringComparison.InvariantCultureIgnoreCase ) )
				continue;
			bip = false;
			break;
		}
		if ( !bip )
		{
			// unless it has one of these, then its probably some procedural shit
			foreach ( var str in _nonBipBlacklist )
			{
				if ( !child.Contains( str, StringComparison.InvariantCultureIgnoreCase ) )
					continue;
				bip = true;
				break;
			}
		}
		if ( parent is null )
			return bip;
		bip = bip || DrawBoneAsBip( parent ); //also check parent is valid
		if ( bip && parent.Length == child.Length )
		{
			// even if it isn't a standard skeleton bone we still want to pick up chains
			// so stuff like rope_01 -> rope_02 still draws as a bone
			bip = false;
			for ( var i = 0; i < parent.Length; i++ )
			{
				var a = parent[i];
				var b = child[i];
				if ( a == b )
					continue;
				if ( char.IsNumber( a ) && char.IsNumber( b ) )
					continue;
				bip = true;
				break;
			}
		}
		return bip;
	}

	void DrawBoneGizmo()
	{
		if ( !Gizmo.Settings.GizmosEnabled )
			return;

		if ( !Flags.Contains( GameObjectFlags.Bone ) )
			return;

		if ( !Parent.IsValid() )
			return;

		var distance = Root.WorldPosition.Distance( Gizmo.Camera.Position );
		if ( distance > 500.0f )
			return;

		var bounds = BBox.FromPositionAndSize( WorldPosition );
		foreach ( var child in Children )
			bounds = bounds.AddPoint( child.WorldPosition );
		if ( !Gizmo.Camera.GetFrustum( new( 0, 1 ), 1 ).IsInside( bounds.Grow( 8 ), true ) )
			return;

		using ( Gizmo.Scope( "Bone" ) )
		{
			Gizmo.Hitbox.DepthBias *= 0.1f;
			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Gizmo.IsSelected ? Gizmo.Colors.Active : Gizmo.IsHovered ? Color.White : Color.White.Darken( 0.5f );

			var dist = Gizmo.CameraTransform.Position.Distance( WorldPosition );
			var size = dist.Remap( 0, 256, 1.5f, 0.4f );
			if ( Gizmo.Camera.Ortho )
				size *= Gizmo.Camera.OrthoHeight * 0.006f;
			else
			{
				size *= 1024.0f / Gizmo.Camera.Size.Length;
				size *= dist * Gizmo.Camera.FieldOfView.DegreeToRadian() * 0.006f;
			}
			var rot = Gizmo.LocalCameraTransform.Rotation;
			var bip = true;
			// bones
			var bsize = size * 0.6f;
			Gizmo.Hitbox.Sphere( new Sphere( 0, bsize ) );
			foreach ( var child in Children )
			{
				if ( DrawBoneAsBip( child.Name, Name ) )
					continue;
				bip = false;

				var delta = Gizmo.CameraTransform.PointToLocal( child.WorldPosition ).WithX( 0 );
				delta -= Gizmo.CameraTransform.PointToLocal( WorldPosition ).WithX( 0 );
				var aim = rot * Rotation.FromRoll( MathF.Atan2( delta.z, delta.y ).RadianToDegree() - 90 );
				var tip = Gizmo.Transform.PointToLocal( child.WorldPosition );
				Gizmo.Draw.SolidTriangle( aim.Left * bsize, tip, aim.Right * bsize );
				Gizmo.Draw.SolidTriangle( aim.Left * bsize, aim.Down * bsize, aim.Right * bsize );

				Gizmo.Transform = Gizmo.Transform.WithRotation( Rotation.LookAt( Vector3.Direction( WorldPosition, child.WorldPosition ), rot.Up ) );
				var girth = bsize * 0.6f;
				Gizmo.Hitbox.BBox( new( new Vector3( 0, -girth, -girth ), new Vector3( tip.Length, girth, girth ) ) );
				Gizmo.Transform = Gizmo.Transform.WithRotation( WorldRotation );
			}
			if ( bip )
			{
				// bip
				Gizmo.Hitbox.Sphere( new Sphere( 0, size ) );
				Gizmo.Draw.SolidTriangle( rot.Left * size, rot.Up * size, rot.Right * size );
				Gizmo.Draw.SolidTriangle( rot.Left * size, rot.Down * size, rot.Right * size );
			}
			if ( Gizmo.IsHovered && !Gizmo.IsSelected )
			{
				var offset = Gizmo.LocalCameraTransform.Position.Length.Remap( 0, 256, 12, 6 );
				Gizmo.Draw.ScreenText( Name, WorldPosition, Vector2.Right * offset, size: 14, flags: TextFlag.LeftCenter );
			}

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
