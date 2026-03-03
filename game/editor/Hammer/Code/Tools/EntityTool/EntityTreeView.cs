namespace Editor.MapEditor;

public class EntityTreeView : TreeView
{
	public Action<string> OnItemSelected { get; set; }

	public EntityTreeView( Widget parent ) : base( parent )
	{
		ItemDrag = ( a ) =>
		{
			if ( a is not EntityDataNode entityNode )
				return false;

			var drag = new Drag( this );
			drag.Data.Text = $"entity:{entityNode.Value.Name}";
			drag.Execute();

			return true;
		};
		ItemSelected = OnItemClicked;
	}

	protected void OnItemClicked( object value )
	{
		if ( value is MapClass mapClass )
		{
			OnItemSelected?.Invoke( mapClass.Name );
			return;
		}

		if ( value is EntityDataNode entityNode )
		{
			OnItemSelected?.Invoke( entityNode.Value.Name );
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		base.OnPaint();
	}
}
