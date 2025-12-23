using Sandbox.UI;
using static Sandbox.Gizmo;

namespace Editor;

[CustomEditor( typeof( GameTransform ) )]
partial class TransformComponentWidget : ComponentEditorWidget
{
	private Widget content;

	bool UseLocal
	{
		get => ProjectCookie.Get( "Transform.UseLocal", true );
		set => ProjectCookie.Set( "Transform.UseLocal", value );
	}

	public TransformComponentWidget( SerializedObject obj ) : base( obj )
	{
		Layout = Layout.Column();
		Layout.Margin = new Margin( 0, 5, 0, 4 );

		Rebuild();
	}

	void Rebuild()
	{
		Layout.Clear( true );

		content = new Widget();

		var cs = new ControlSheet();
		cs.IncludePropertyNames = true;
		cs.Margin = new Margin( 0, 0, 0, 0 );

		if ( IsBone() )
		{
			Layout.Add( new TransformControlledBanner( gameObject ) );
		}

		if ( UseLocal )
		{
			cs.AddRow( SerializedObject.GetProperty( "LocalPosition" ) );
			cs.AddRow( SerializedObject.GetProperty( "LocalRotation" ) );
			cs.AddRow( SerializedObject.GetProperty( "LocalScale" ) );
		}
		else
		{
			cs.AddRow( SerializedObject.GetProperty( "Position" ) );
			cs.AddRow( SerializedObject.GetProperty( "Rotation" ) );
			cs.AddRow( SerializedObject.GetProperty( "Scale" ) );
		}

		content.Layout = Layout.Column();
		content.Layout.Add( cs );
		Layout.Add( content );
	}

	private SerializedObject gameObject => SerializedObject.ParentProperty.Parent;

	private bool IsBone()
	{
		if ( !SerializedObject.IsMultipleTargets )
		{
			GameObjectFlags flags = gameObject.GetProperty( nameof( GameObject.Flags ) ).GetValue<GameObjectFlags>();
			bool isBone = flags.HasFlag( GameObjectFlags.Bone );
			return isBone;
		}

		// todo?
		return false;
	}
	private bool IsProceduralBone()
	{
		if ( !SerializedObject.IsMultipleTargets )
		{
			GameObjectFlags flags = gameObject.GetProperty( nameof( GameObject.Flags ) ).GetValue<GameObjectFlags>();
			bool isBone = flags.HasFlag( GameObjectFlags.ProceduralBone );
			return isBone;
		}

		// todo?
		return false;
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		content.Enabled = !IsBone() || IsProceduralBone();
	}

	public override void OnHeaderContextMenu( Menu menu )
	{
		base.OnHeaderContextMenu( menu );

		menu.Clear();

		menu.AddOption( UseLocal ? "Display Worldspace" : "Display Localspace", "public", () =>
		{
			UseLocal = !UseLocal;
			Rebuild();
		} );

		menu.AddSeparator();

		menu.AddOption( "Reset", "restart_alt", () =>
		{
			SerializedObject.GetProperty( UseLocal ? "Local" : "World" ).SetValue( Transform.Zero );
		} );
		menu.AddSeparator();

		menu.AddOption( "Copy Local Transform", "content_copy", () =>
		{
			var tx = SerializedObject.GetProperty( "Local" ).GetValue<Transform>();
			EditorUtility.Clipboard.Copy( Json.Serialize( tx ) );
		} );

		menu.AddOption( "Copy World Transform", "content_copy", () =>
		{
			var tx = SerializedObject.GetProperty( "World" ).GetValue<Transform>();
			EditorUtility.Clipboard.Copy( Json.Serialize( tx ) );
		} );

		try
		{
			var clipText = EditorUtility.Clipboard.Paste();
			var tx = Json.Deserialize<Transform>( clipText );
			if ( tx != default )
			{
				menu.AddOption( "Paste as Local Transform", "content_paste", () =>
				{
					SerializedObject.GetProperty( "Local" ).SetValue( tx );
				} );

				menu.AddOption( "Paste as World Transform", "content_paste", () =>
				{
					SerializedObject.GetProperty( "World" ).SetValue( tx );
				} );
			}
		}
		catch ( System.Exception )
		{
			// ignore
		}
	}
}

file class TransformControlledBanner : Widget
{
	private SerializedObject targetObject;

	public TransformControlledBanner( SerializedObject targetObject )
	{
		this.targetObject = targetObject;
		Cursor = CursorShape.Finger;
	}

	protected override Vector2 SizeHint() => 24;

	protected override void OnPaint()
	{
		var flagsProp = targetObject.GetProperty( nameof( GameObject.Flags ) );
		GameObjectFlags flags = flagsProp.GetValue<GameObjectFlags>();

		Color color = Theme.Pink;
		string text = "Controlled by a bone - click here to make procedural";

		if ( flags.HasFlag( GameObjectFlags.ProceduralBone ) )
		{
			color = Theme.Green;
			text = "Controlled procedurally - click here to make animation controlled";
		}

		Paint.Antialiasing = true;
		Paint.SetBrushAndPen( color.WithAlpha( 0.1f ), color.WithAlpha( 0.4f ) );
		Paint.DrawRect( LocalRect.Shrink( 8, 1 ), 4 );

		Paint.SetPen( color );
		Paint.DrawIcon( LocalRect.Shrink( 16, 0, 0, 0 ), "polyline", 13, TextFlag.LeftCenter );
		Paint.DrawText( LocalRect.Shrink( 48, 0, 0, 0 ), text, TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		var flagsProp = targetObject.GetProperty( nameof( GameObject.Flags ) );
		GameObjectFlags flags = flagsProp.GetValue<GameObjectFlags>();

		if ( flags.HasFlag( GameObjectFlags.ProceduralBone ) )
		{
			flags &= ~GameObjectFlags.ProceduralBone;
		}
		else
		{
			flags |= GameObjectFlags.ProceduralBone;
		}

		flagsProp.SetValue( flags );
	}
}
