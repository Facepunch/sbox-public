namespace Editor.AnimGraph;

[CustomEditor( typeof( int ), NamedEditor = "animgraph.take" )]
sealed class AnimationTakeControlWidget : DropdownControlWidget<int>
{
	private readonly ClipDocument _document;

	private bool IsAlignmentReference => SerializedProperty.Name == nameof( ClipDocument.AlignmentReferenceTake );
	private string SourceFilename => IsAlignmentReference ? _document.AlignmentReference : _document.SourceFilename;

	public AnimationTakeControlWidget( SerializedProperty property ) : base( property )
	{
		_document = property.Parent.Targets.OfType<ClipDocument>().First();
	}

	protected override int ValueHash => HashCode.Combine( base.ValueHash, SourceFilename );

	private IReadOnlyList<string> GetTakes( bool refresh = false )
		=> IsAlignmentReference ? _document.GetAlignmentReferenceTakes( refresh ) : _document.GetAnimationTakes( refresh );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		ToolTip = string.IsNullOrWhiteSpace( SourceFilename ) ? "Choose an animation source file."
			: GetTakes().Count == 0 ? "Couldn't read animation takes. See the console for details."
			: "Choose an animation take from the source file.";
	}

	protected override IEnumerable<object> GetDropdownValues()
		=> GetTakes( refresh: true ).Select( (name, index) => new Entry { Value = index, Label = name } ).Cast<object>();

	protected override string GetDisplayText()
	{
		var takes = GetTakes();
		var take = SerializedProperty.GetValue<int>();
		return take >= 0 && take < takes.Count ? takes[take]
			: takes.Count == 0 ? $"Take {take} (unavailable)" : $"Take {take} (missing)";
	}

	protected override void OnItemSelected( object item )
	{
		if ( IsControlDisabled )
			return;

		PropertyStartEdit();
		base.OnItemSelected( item );
		PropertyFinishEdit();
	}
}
