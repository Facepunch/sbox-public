namespace Editor.AnimGraph;

[CustomEditor( typeof( Nodes.AndNode.Condition ) )]
internal sealed class AndConditionControlWidget : ControlObjectWidget
{
	public override bool IncludeLabel => false;

	public AndConditionControlWidget( SerializedProperty property ) : base( property, true )
	{
		FixedHeight = Theme.RowHeight;
	}

	protected override void OnPaint()
	{
		Paint.SetPen( Theme.Text );
		Paint.DrawText( LocalRect.Shrink( 8f, 0f ), "And", TextFlag.LeftCenter );
	}
}
