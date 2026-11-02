namespace Editor.AnimGraph;

internal sealed class ClipProperties : Widget
{
	public Action<string> Committed { get; set; }

	public ClipProperties( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 4;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetDocument( ClipDocument document )
	{
		using var suspend = SuspendUpdates.For( this );
		Layout.Clear( true );

		if ( document is null )
		{
			Layout.Add( new Label( "A readable source .anim document is required to edit clip settings.", this ) { WordWrap = true } );
			return;
		}

		var serialized = EditorTypeLibrary.GetSerializedObject( document );
		var before = document.Snapshot();
		var editing = false;
		void Commit( SerializedProperty property )
		{
			var after = document.Snapshot();
			if ( after == before )
				return;

			before = after;
			Committed?.Invoke( $"Edit {property.DisplayName}" );
		}
		serialized.OnPropertyStartEdit += _ => editing = true;
		serialized.OnPropertyFinishEdit += property =>
		{
			editing = false;
			Commit( property );
		};
		serialized.OnPropertyChanged += property =>
		{
			if ( !editing )
				Commit( property );
		};

		var sheet = new ControlSheet();
		string[] groups = ["Import", "Import Settings", "Loop Smoothing", "Additive", "Alignment", "Animation Offsets", "Motion Extraction", "Morph Frame"];
		foreach ( var group in serialized.Where( property => property.HasAttribute<PropertyAttribute>() )
			.GroupBy( property => property.GroupName )
			.OrderBy( group => Array.IndexOf( groups, group.Key ) is var index && index >= 0 ? index : int.MaxValue ) )
		{
			sheet.AddGroup( group.Key, group.OrderBy( property => property.Name == nameof( ClipDocument.AdditiveType ) ? -1 : property.Order )
				.ThenBy( property => property.SourceLine ).ToArray() );
		}
		Layout.Add( sheet );
		foreach ( var control in this.GetDescendants<ControlWidget>() )
			control.MinimumWidth = 0;
	}
}
