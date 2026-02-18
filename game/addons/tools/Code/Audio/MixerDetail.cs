using Sandbox.Audio;

namespace Editor.Audio;

public class MixerDetail : NavigationView
{
	MixerDock ParentDock;
	Mixer mixer;

	Layout ProcessorSection;

	public MixerDetail( MixerDock parent, Mixer mixer ) : base( null )
	{
		ParentDock = parent;
		this.mixer = mixer;

		{
			var headerLayout = Layout.Row();
			var icon = headerLayout.Add( new Label( "settings_input_component" ) );
			icon.SetStyles( "* { font-family: Material Icons; margin-right: 8px; }" );
			headerLayout.Add( new Label( mixer.Name ) );
			headerLayout.AddStretchCell( 1 );
			MenuTop.Add( headerLayout );
		}

		AddSectionHeader( "Config" );
		AddPage( "Mixer Settings", "settings", MixerPage() );
		AddPage( "Monitor", "settings", null );


		{
			var headerRow = AddSectionHeader( "Processors" );
			headerRow.Add( new IconButton( "add" ) { Background = Color.Transparent, OnClick = AddProcessorMenu } );
		}

		ProcessorSection = MenuContents.AddColumn();

		UpdateProcessorList();


		// Sidebar like the asset browser

		// - basic settings (volume), dashboard
		// - metering
		// - processor list

	}

	void UpdateProcessorList()
	{
		ProcessorSection.Clear( true );

		foreach ( var p in mixer.GetProcessors() )
		{
			var td = EditorTypeLibrary.GetType( p.GetType() );

			var o = new ProcessorOption( p, td.Icon ?? "people" );
			var so = p.GetSerialized();
			so.OnPropertyChanged += ( prop ) =>
			{
				ParentDock.SetDirty();
			};
			o.CreatePage = () => new ProcessorPageWidget( so );
			o.OpenContextMenu = () => ProcessorContextMenu( p );

			AddPage( o );

			ProcessorSection.Add( o );
		}
	}

	class ProcessorOption : Option
	{
		AudioProcessor processor;

		public ProcessorOption( AudioProcessor processor, string icon, NavigationView parent = null ) : base( processor.ToString(), icon, parent )
		{
			this.processor = processor;
		}

		protected override void OnPaint()
		{
			var fg = Theme.Text.WithAlpha( 0.5f );

			if ( IsSelected )
			{
				fg = Theme.Text;
			}

			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground.WithAlpha( 0.0f ) );

			if ( Paint.HasMouseOver )
			{
				fg = Theme.Text.WithAlpha( 0.8f );
			}

			Paint.TextAntialiasing = true;
			Paint.Antialiasing = true;


			Paint.DrawRect( LocalRect.Shrink( 0 ) );

			var inner = LocalRect.Shrink( 8, 0, 0, 0 );
			var iconRect = inner;
			iconRect.Width = iconRect.Height;

			Paint.SetPen( fg );
			Paint.DrawIcon( iconRect, Icon, 14, TextFlag.Center );

			inner.Left += iconRect.Width + 4;

			Paint.SetPen( Color.Lerp( fg.WithAlphaMultiplied( 0.8f ), Theme.Green, processor.Mix ) );
			Paint.SetHeadingFont( 8, 440 );
			Paint.DrawText( inner, processor.ToString(), TextFlag.LeftCenter );
		}

		[EditorEvent.Frame]
		public void Frame()
		{
			if ( !Visible ) return;

			SetContentHash( System.HashCode.Combine( processor.Enabled, processor.Mix ) );
		}
	}

	private void ProcessorContextMenu( AudioProcessor p )
	{
		var menu = new ContextMenu( this );

		menu.AddOption( "Delete", "clear", () => RemoveProcessor( p ) );

		menu.OpenAtCursor();
	}

	Widget MixerPage()
	{
		var w = new Widget();
		w.Layout = Layout.Column();

		var cs = new ControlSheet();
		var so = mixer.GetSerialized();
		so.OnPropertyChanged += ( prop ) =>
		{
			ParentDock.SetDirty();
		};
		cs.AddObject( so );

		w.Layout.Add( cs );
		w.Layout.AddStretchCell();

		return w;
	}

	void AddProcessorMenu()
	{
		var menu = new ContextMenu( this );

		foreach ( var type in EditorTypeLibrary.GetTypes<AudioProcessor>().OrderBy( x => x.Title ) )
		{
			var o = menu.AddOption( type.Title, type.Icon, () => AddProcessor( type ) );
			o.ToolTip = type.Description;
		}

		menu.OpenAtCursor();
	}

	private void AddProcessor( TypeDescription type )
	{
		AudioProcessor ap = type.Create<AudioProcessor>();
		mixer.AddProcessor( ap );

		UpdateProcessorList();
		ParentDock.SetDirty();
	}

	private void RemoveProcessor( AudioProcessor p )
	{
		mixer.RemoveProcessor( p );
		UpdateProcessorList();
		ParentDock.SetDirty();
	}

	int _hc;

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !Visible ) return;

		var h = mixer.ProcessorCount;

		if ( _hc != h )
		{
			_hc = h;
			UpdateProcessorList();
		}

	}
}

internal class ProcessorPageWidget : Widget
{
	private SerializedObject p;

	public ProcessorPageWidget( SerializedObject p )
	{
		this.p = p;

		Layout = Layout.Column();

		var cs = new ControlSheet();
		cs.AddObject( p );

		Layout.Add( cs );
		Layout.AddStretchCell();

	}
}
