using Sandbox.Internal;

namespace Editor.AnimGraph;

internal sealed class ClipEventProperties : Widget
{
	public Action Edited { get; set; }
	public int FrameCount { get; set; }

	private ClipDocument.Event _item;
	private string _eventClass;
	private bool _readOnly;
	private Action _commit;

	public ClipEventProperties( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		SetEvent( null, null, true );
	}

	public void SetEvent( ClipDocument.Event item, string eventClass, bool readOnly )
	{
		CommitEdit();
		_item = item;
		_eventClass = eventClass;
		_readOnly = readOnly;
		BuildEditor();
	}

	public void CommitEdit()
	{
		if ( IsValid && Application.FocusWidget is { } focus && GetDescendants<Widget>().Contains( focus ) )
			focus.Blur();
		_commit?.Invoke();
	}

	private void BuildEditor()
	{
		Layout.Clear( true );
		_commit = null;
		if ( _item is null )
		{
			Layout.Add( new Label( "Select an event in the timeline to edit its properties.", this )
			{ WordWrap = true, Margin = 8, Color = Theme.TextControl } );
			Layout.AddStretchCell();
			return;
		}

		var item = _item;
		var readOnly = _readOnly;
		var serialized = item.GetSerialized();
		var before = item.Json.ToJsonString();
		var editing = false;
		var rebuild = false;
		void Commit()
		{
			var after = item.Json.ToJsonString();
			if ( readOnly || after == before )
				return;

			before = after;
			Edited?.Invoke();
		}
		void Finish()
		{
			Commit();
			if ( !rebuild )
				return;

			rebuild = false;
			BuildEditor();
		}
		_commit = Commit;
		serialized.OnPropertyStartEdit += _ => editing = true;
		serialized.OnPropertyFinishEdit += _ =>
		{
			editing = false;
			Finish();
		};
		serialized.OnPropertyChanged += _ =>
		{
			if ( !editing )
				Finish();
		};

		var scroll = Layout.Add( new ScrollArea( this )
		{
			HorizontalScrollbarMode = ScrollbarMode.Off,
			NoSystemBackground = true,
			TranslucentBackground = true,
			MinimumWidth = 300
		}, 1 );
		scroll.SetStyles( "QScrollArea { border: 0px; }" );
		var body = new Widget( scroll )
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanGrow,
			Enabled = !readOnly
		};
		scroll.Canvas = body;
		body.Layout.Spacing = 4;
		var properties = new ControlSheet();
		body.Layout.Add( properties );
		var last = MathF.Max( FrameCount - 1, 0 );
		var frames = EditorTypeLibrary.CreateProperty( "Frames", () => new Vector2( item.StartFrame, item.EndFrame ), value =>
		{
			if ( readOnly )
				return;

			item.StartFrame = value.x.Clamp( 0, last );
			item.DurationFrames = value.y.Clamp( item.StartFrame, last ) - item.StartFrame;
		}, [new MinMaxSliderAttribute( 0, last ), new StepAttribute( 1 )], serialized );

		var fields = new List<SerializedProperty>();
		if ( item.IsTypedEvent )
		{
			var type = EditorTypeLibrary.CreateProperty( "Type", () => item.DataType?.FullName ?? item.DataTypeName, name =>
			{
				if ( readOnly || name == (item.DataType?.FullName ?? item.DataTypeName) )
					return;

				var description = EditorTypeLibrary.GetType<Sandbox.Animation.AnimationEvent>( name );
				var data = description?.Create<Sandbox.Animation.AnimationEvent>()
					?? throw new InvalidOperationException( $"Couldn't create animation event '{name}'." );
				item.Data = new( data );
				rebuild = true;
			}, [new EditorAttribute( "animgraph.event-type" )], serialized );
			fields.Add( type );
			fields.Add( frames );
			var data = item.Data;
			if ( data.HasValue )
			{
				var payload = data.Value.GetSerialized();
				payload.ParentProperty = EditorTypeLibrary.CreateProperty( "Data", () => data.Value, value =>
				{
					if ( !readOnly )
						item.Data = new( value );
				}, parent: serialized );
				fields.AddRange( payload );
			}
			else
				body.Layout.Add( new Label( string.IsNullOrEmpty( item.DataTypeName )
					? "Select an event type to edit its properties."
					: $"'{item.DataTypeName}' is unavailable. Its saved payload is preserved." )
				{ WordWrap = true, Color = Theme.TextControl, Margin = 6 } );
		}
		else
		{
			var payload = ClipEventTypes.PayloadProperty( _eventClass ) is { } name ? serialized.GetProperty( name ) : null;
			if ( payload is not null )
				fields.Add( payload );
			fields.Add( frames );
		}

		if ( _eventClass == ClipEventTypes.Generic )
			fields.Add( serialized.GetProperty( nameof( ClipDocument.Event.FireDuringTransitions ) ) );

		IControlSheet.FilterSortAndAdd( properties, fields );
		body.Layout.AddStretchCell();
		foreach ( var group in body.GetDescendants<ControlSheetGroup>() )
			group.UpdateVisibility();
		foreach ( var control in body.GetDescendants<ControlWidget>() )
			control.MinimumWidth = 0;
	}

	public override void OnDestroyed()
	{
		CommitEdit();
		base.OnDestroyed();
	}
}

[CustomEditor( typeof( string ), NamedEditor = "animgraph.event-type" )]
public sealed class ClipEventTypeControlWidget : ControlWidget
{
	private readonly ComboBox _types;
	private readonly List<string> _typeNames = [];

	public ClipEventTypeControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		PaintBackground = false;
		_types = Layout.Add( new ComboBox( this ) { FixedHeight = Theme.RowHeight } );

		foreach ( var type in EditorTypeLibrary.GetTypes( typeof( Sandbox.Animation.AnimationEvent ) )
			.Where( type => !type.IsAbstract && !type.TargetType.ContainsGenericParameters )
			.OrderBy( type => type.Title ) )
		{
			_typeNames.Add( type.FullName );
			_types.AddItem( type.Title, type.Icon );
		}

		OnValueChanged();
		_types.ItemChanged += () =>
		{
			if ( IsControlDisabled || _types.CurrentIndex < 0 )
				return;

			var name = _typeNames[_types.CurrentIndex];
			if ( name == SerializedProperty.GetValue<string>() )
				return;

			PropertyStartEdit();
			SerializedProperty.SetValue( name );
			PropertyFinishEdit();
		};
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		if ( !_types.IsValid() )
			return;

		var name = SerializedProperty.GetValue<string>();
		var index = _typeNames.IndexOf( name );
		if ( index < 0 )
		{
			index = _typeNames.Count;
			_typeNames.Add( name );
			_types.AddItem( string.IsNullOrEmpty( name ) ? "Select event type..." : $"{name} (unavailable)" );
		}
		_types.CurrentIndex = index;
	}
}
