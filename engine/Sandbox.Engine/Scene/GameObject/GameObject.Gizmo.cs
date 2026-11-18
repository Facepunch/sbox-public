using Sandbox.Engine;
namespace Sandbox;

public partial class GameObject
{
	internal class GameObjectHandle
	{
		public Texture Texture { get; set; }
		public string TextureName { get; set; }
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

	static readonly ReflectionCache<Type, EditorHandleAttribute> _handleAttributes = new(
		t => GlobalContext.Current.TypeLibrary.GetType( t )?.GetAttributes<EditorHandleAttribute>( true ).FirstOrDefault() );

	GameObjectHandle _handle;
	GameObjectHandle _cachedHandle;
	bool handleBuilt;
	int _handleGen = -1;
	Gizmo.SceneSettings _handleSettings;
	List<Component> _gizmoComponents;

	[Obsolete( "Use HasGizmoHandle" )]
	public bool HasGimzoHandle { get => HasGizmoHandle; private set => HasGizmoHandle = value; }
	bool _hasGizmoHandle;
	public bool HasGizmoHandle
	{
		get => _hasGizmoHandle && _handleSettings?.GizmosEnabled == true && _handleGen == Gizmo.GizmoTypeGeneration && IsGizmoVisible;
		private set => _hasGizmoHandle = value;
	}

	void BuildGizmoDetails()
	{
		// Rebuilt only when the component list changes (ClearInternalCache) or a gizmo type is toggled.
		if ( handleBuilt && _handleGen == Gizmo.GizmoTypeGeneration && ReferenceEquals( _handleSettings, Gizmo.Settings ) )
			return;

		handleBuilt = true;
		_handleGen = Gizmo.GizmoTypeGeneration;
		_handleSettings = Gizmo.Settings;
		_gizmoComponents?.Clear();
		_handle = null;

		EditorHandleAttribute handles = null;

		for ( var i = 0; i < Components.Count; ++i )
		{
			var c = Components[i];
			if ( c is null )
				continue;

			if ( !Gizmo.Settings.IsGizmoEnabled( c.GetType() ) ) continue;
			if ( c.OverridesDrawGizmos ) (_gizmoComponents ??= new()).Add( c );
			if ( handles is not null ) continue;

			handles = _handleAttributes[c.GetType()];
		}

		if ( handles is null )
		{
			if ( _cachedHandle is not null ) _cachedHandle.ColorProvider = null;
			return;
		}

		_handle = _cachedHandle ??= new GameObjectHandle();
		_handle.Icon = handles.Icon;
		_handle.Color = handles.Color;
		_handle.ColorProvider = null;
		for ( var i = 0; i < Components.Count; ++i )
			if ( Components[i] is Component.IColorProvider provider ) { _handle.ColorProvider = provider; break; }

		if ( _handle.TextureName != handles.Texture )
		{
			_handle.TextureName = handles.Texture;
			_handle.Texture = string.IsNullOrWhiteSpace( handles.Texture ) ? null : Texture.Load( handles.Texture );
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

		using ( Gizmo.PushScope( "Handle" ) )
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

	internal void DrawGizmos() => DrawGizmos( false );

	void DrawGizmos( bool active )
	{
		if ( !active && !Active ) return;
		if ( !Gizmo.Settings.GizmosEnabled )
		{
			// Scene-mesh selection is reported directly by the viewport; it needs no scene traversal.
			if ( this is Scene && Gizmo.Active.current.HoveredObject is GameObject target
				&& target.Scene == this && target.IsGizmoVisible
				&& (Gizmo.HasClicked || Gizmo.Pressed.Any) )
			{
				using var scope = Gizmo.PushObjectScope( target, target.WorldTransform );
				target.ProcessGizmoSelection( Gizmo.WasClicked );
			}
			return;
		}

		BuildGizmoDetails();
		HasGizmoHandle = false;
		if ( Children.Count == 0 && _handle is null && !HasActiveGizmoComponents() && !Flags.Contains( GameObjectFlags.Bone )
			&& !ReferenceEquals( Gizmo.Active.current.HoveredObject, this ) ) return;

		var parentTx = Gizmo.Transform;

		var tx = LocalTransform;

		// Absolute gameobject transform need to be converted back to local because it's already in worldspace
		if ( Flags.Contains( GameObjectFlags.Absolute ) )
		{
			tx = parentTx.ToLocal( tx );
		}

		using ( Gizmo.PushObjectScope( this, tx ) )
		{
			Gizmo.Active.scope.IsSceneGizmo = true;
			Gizmo.Active.scope.Component = null;
			bool clicked = Gizmo.WasClicked;

			if ( Gizmo.Settings.GizmosEnabled )
			{
				DrawGizmoHandle( ref clicked );

				if ( _gizmoComponents is not null )
					for ( var i = _gizmoComponents.Count - 1; i >= 0 && i < _gizmoComponents.Count; --i )
					{
						var c = _gizmoComponents[i];
						if ( !c.IsValid() || !c.Active || c.Flags.Contains( ComponentFlags.Hidden ) ) continue;
						try
						{
							using var scope = Gizmo.PushScope();
							Gizmo.Active.scope.Component = c;
							c.DrawGizmosInternal();
							clicked |= Gizmo.WasClicked;
						}
						catch ( System.Exception e )
						{
							Log.Warning( e, $"Exception when calling DrawGizmos on {c}: {e.Message}" );
						}
					}
			}

			ProcessGizmoSelection( clicked );

			for ( var i = Children.Count - 1; i >= 0; --i )
			{
				if ( i >= Children.Count ) continue;
				var child = Children[i];
				if ( child is null ) { Children.RemoveAt( i ); continue; }
				if ( !child.Active || child.Flags.Contains( GameObjectFlags.Hidden ) || child.Tags.Has( "hidden" ) ) continue;
				try { child.DrawGizmos( true ); }
				catch ( System.Exception e ) { Log.Warning( e, $"Exception when calling DrawGizmos on {child}" ); }
			}

			DrawBoneGizmo();
		}
	}

	internal bool IsGizmoVisible
	{
		get
		{
			if ( !Active ) return false;
			for ( var go = this; go is not null; go = go.Parent )
				if ( go.Flags.Contains( GameObjectFlags.Hidden ) || go.Tags.Has( "hidden" ) ) return false;
			return true;
		}
	}

	bool HasActiveGizmoComponents()
	{
		if ( _gizmoComponents is null ) return false;
		foreach ( var c in _gizmoComponents )
			if ( c.IsValid() && c.Active && !c.Flags.Contains( ComponentFlags.Hidden ) ) return true;
		return false;
	}

	void ProcessGizmoSelection( bool clicked )
	{
		if ( clicked ) GizmoSelect();
		if ( Gizmo.Pressed.This && Gizmo.CursorDragDelta.Length > 10 ) Gizmo.Pressed.ClearPath();
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

		using ( Gizmo.PushScope( "Bone" ) )
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

			if ( Gizmo.IsHovered || Gizmo.IsSelected )
			{
				var textPos = Gizmo.Transform.Position + Gizmo.CameraTransform.Up * 0.8f;
				Gizmo.Draw.Text( Name, Gizmo.Transform.ToLocal( new Transform( textPos ) ), flags: TextFlag.Center );
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
		var isSelectionBase = IsNetworkRoot || IsOutermostPrefabInstanceRoot || Components.GetAll().Any( x => GlobalContext.Current.TypeLibrary.GetType( x?.GetType() )?.HasAttribute<SelectionBaseAttribute>() == true );

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

		using ( Gizmo.PushObjectScope( this, LocalTransform ) )
		{
			using ( Scene.Editor?.UndoScope( $"Select {Name}" ).Push() )
			{
				Gizmo.Select();
			}
		}
	}

}
