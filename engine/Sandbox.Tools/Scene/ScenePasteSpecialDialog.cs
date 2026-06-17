using System;
using System.Text.Json.Nodes;

namespace Editor;

public sealed class ScenePasteSpecialDialog : Dialog
{
	public class PasteSpecialOptions
	{
		[Property, Title( "Number of Copies" ), Range( 1, 1000, slider: false ), Step( 1 )]
		public int Copies { get; set; } = 1;

		[Property, Title( "Start at Center of Original" )]
		public bool CenterOriginal { get; set; } = true;

		[Property, Title( "Group Copies" )]
		public bool GroupCopies { get; set; }

		[Property, Title( "Relative to Last Copy" ), Description( "Apply offset/rotation in local space of the previous copy." )]
		public bool RelativeToLast { get; set; }

		[Property, Title( "Offset (Accumulative)" )]
		public Vector3 Offset { get; set; }

		[Property, Title( "Rotation (Accumulative)" )]
		public Angles Rotation { get; set; }
	}

	readonly PasteSpecialOptions _options = new();
	readonly Action<PasteSpecialOptions> _onSubmit;

	public ScenePasteSpecialDialog( Action<PasteSpecialOptions> onSubmit )
	{
		_onSubmit = onSubmit;

		Window.SetModal( true, true );
		Window.SetWindowIcon( "content_paste_go" );
		Window.Title = "Paste Special";
		Window.FixedSize = new Vector2( 420, 330 );

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		var so = _options.GetSerialized();

		AddPropertyRow( so, nameof( PasteSpecialOptions.Copies ) );
		AddPropertyRow( so, nameof( PasteSpecialOptions.CenterOriginal ) );
		AddPropertyRow( so, nameof( PasteSpecialOptions.GroupCopies ) );
		AddPropertyRow( so, nameof( PasteSpecialOptions.RelativeToLast ) );

		Layout.AddSpacingCell( 8 );

		AddPropertyRow( so, nameof( PasteSpecialOptions.Offset ) );
		AddPropertyRow( so, nameof( PasteSpecialOptions.Rotation ) );

		Layout.AddStretchCell();

		var footer = Layout.AddRow();
		footer.Spacing = 8;
		footer.AddStretchCell();

		var pasteButton = footer.Add( new Button.Primary( "Paste" ) );
		pasteButton.Clicked = Submit;

		footer.Add( new Button( "Cancel" ) { Clicked = Close } );
	}

	void AddPropertyRow( SerializedObject so, string propertyName )
	{
		var prop = so.GetProperty( propertyName );
		var row = Layout.AddRow();
		row.Spacing = 8;
		row.Add( new Label( prop.DisplayName ) { MinimumWidth = 140 } );
		row.Add( ControlWidget.Create( prop ), 1 );
	}

	void Submit()
	{
		_onSubmit?.Invoke( _options );
		Close();
	}

	[EditorEvent.Frame]
	void CheckKeys()
	{
		if ( !Window.IsValid() || !Window.Visible )
			return;

		if ( Application.IsKeyDown( KeyCode.Escape ) )
		{
			Close();
		}
		else if ( Application.IsKeyDown( KeyCode.Enter ) || Application.IsKeyDown( KeyCode.Return ) )
		{
			Submit();
		}
	}

	/// <summary>
	/// Calculate world position and rotation for copy at the given index.
	/// </summary>
	static (Vector3 Position, Rotation Rotation) GetCopyTransform( int index, Vector3 basePosition, Rotation baseRotation, GameObject previous, PasteSpecialOptions options )
	{
		if ( options.RelativeToLast && previous is not null )
		{
			var localOffset = previous.WorldRotation * options.Offset;
			return (previous.WorldPosition + localOffset, previous.WorldRotation * options.Rotation.ToRotation());
		}

		if ( !options.CenterOriginal )
		{
			return (options.Offset * (index + 1), (options.Rotation * (index + 1)).ToRotation());
		}

		return (basePosition + options.Offset * index, baseRotation * (options.Rotation * index).ToRotation());
	}

	/// <summary>
	/// Apply grouping and selection to pasted objects.
	/// </summary>
	static void FinishAndSelect( List<GameObject> objects, PasteSpecialOptions options )
	{
		if ( options.GroupCopies && objects.Count > 0 )
		{
			var group = SceneEditorSession.Active.Scene.CreateObject();
			group.Name = objects.Count == 1 ? $"{objects[0].Name} (Copy)" : "Paste Group";

			foreach ( var go in objects )
				go.SetParent( group );

			EditorScene.Selection.Add( group );
		}
		else
		{
			foreach ( var go in objects )
				EditorScene.Selection.Add( go );
		}
	}

	internal static void Execute( PasteSpecialOptions options )
	{
		var text = EditorUtility.Clipboard.Paste();

		if ( string.IsNullOrWhiteSpace( text ) )
		{
			Log.Warning( "Paste Special: clipboard is empty. Copy some objects first." );
			return;
		}

		try
		{
			if ( Json.Deserialize<IEnumerable<JsonObject>>( text ) is not IEnumerable<JsonObject> serializedObjects )
				return;

			if ( !serializedObjects.Any() )
				return;

			var session = SceneEditorSession.Active;
			using var scene = session.Scene.Push();

			using ( session.UndoScope( $"Paste Special ({options.Copies} copies)" ).WithGameObjectCreations().Push() )
			{
				EditorScene.Selection.Clear();

				var allPasted = new List<GameObject>();

				for ( int i = 0; i < options.Copies; i++ )
				{
					foreach ( var jso in serializedObjects )
					{
						var go = session.Scene.CreateObject();
						SceneUtility.MakeIdGuidsUnique( jso );
						go.Deserialize( jso );

						var (pos, rot) = GetCopyTransform( i, go.WorldPosition, go.WorldRotation, allPasted.LastOrDefault(), options );
						go.WorldPosition = pos;
						go.WorldRotation = rot;

						go.MakeNameUnique();
						allPasted.Add( go );
					}
				}

				FinishAndSelect( allPasted, options );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Paste Special: clipboard doesn't contain valid scene objects. Copy some objects first. ({ex.Message})" );
		}
	}
}
