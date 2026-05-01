namespace Editor.DooEditor;

/// <summary>
/// Control widget for editing Doo properties in the inspector.
/// Shows an "Edit" button that opens the Doo editor.
/// </summary>
[CustomEditor( typeof( Doo ) )]
public class DooControlWidget : ControlWidget
{
	public DooControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = 2;

		Layout.AddStretchCell();

		var clearBtn = Layout.Add( new IconButton( "clear" ) );
		clearBtn.ToolTip = "Clear";
		clearBtn.Background = Color.Transparent;
		clearBtn.OnClick = OnClearClicked;
		clearBtn.MaximumHeight = Theme.RowHeight - 4;
		clearBtn.FixedHeight = clearBtn.MaximumHeight;

		Cursor = CursorShape.Finger;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		OnEditClicked();
	}

	void OnEditClicked()
	{
		var doo = SerializedProperty.GetValue<Doo>();

		if ( doo == null )
		{
			doo = new Doo();
			SerializedProperty.SetValue( doo );
		}

		SerializedProperty.TryGetAsObject( out var so );

		var title = SerializedProperty.Name ?? "Doo";
		var editor = DooEditorWidget.Open( so, title );

	}

	void OnClearClicked()
	{
		SerializedProperty.SetValue<Doo>( null );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var doo = SerializedProperty.GetValue<Doo>();
		if ( doo is null || doo.IsEmpty() )
		{
			var iconRect = new Rect( LocalRect.Left + 4, LocalRect.Center.y - 8, 16, 16 );
			Paint.SetBrushAndPen( Theme.Blue.Desaturate( 0.33f ).WithAlpha( 0.4f ) );
			Paint.DrawRect( iconRect, 2 );

			Paint.Pen = Theme.Blue.Lighten( 10f ).Desaturate( 0.85f ).WithAlpha( 0.3f );
			Paint.SetDefaultFont( 7 );
			Paint.DrawText( iconRect, $"▶️", TextFlag.Center );

			Paint.SetDefaultFont();
			Paint.Pen = Theme.TextLight.WithAlpha( 0.5f );
			Paint.DrawText( LocalRect.Shrink( 28, 4, 8, 4 ), $"Empty", TextFlag.LeftCenter );

			return;
		}
		else
		{
			var iconRect = new Rect( LocalRect.Left + 4, LocalRect.Center.y - 8, 16, 16 );
			Paint.SetBrushAndPen( Theme.Blue );
			Paint.DrawRect( iconRect, 2 );

			Paint.Pen = Theme.Blue.Lighten( 10f ).Desaturate( 0.85f );
			Paint.SetDefaultFont( 7 );
			Paint.DrawText( iconRect, $"▶️", TextFlag.Center );

			Paint.SetDefaultFont();
			Paint.Pen = Theme.Blue.Lighten( 0.5f );
			Paint.DrawText( LocalRect.Shrink( 28, 4, 8, 4 ), $"{doo.GetLabel()}", TextFlag.LeftCenter );
		}

		// Don't paint default background
	}
}
