namespace Editor;

partial class ViewportTools
{
	private Widget toolExtensionContainer;
	private EditorTool lastTool;

	/// <summary>
	/// Builds the toolbar extension
	/// </summary>
	private void BuildToolExtensionToolbar( Layout toolbar )
	{
		toolExtensionContainer = new Widget();
		toolExtensionContainer.Layout = Layout.Row();
		toolExtensionContainer.Layout.Spacing = Spacing;

		toolbar.Add( toolExtensionContainer );
	}

	/// <summary>
	/// Updates the toolbar extension when the tool changes
	/// </summary>
	private void UpdateToolExtensionToolbar()
	{
		if ( !toolExtensionContainer.IsValid() )
			return;

		var currentTool = sceneViewWidget.Tools?.CurrentTool;
		var currentSubTool = sceneViewWidget.Tools?.CurrentSubTool;

		bool needsRebuild = lastTool != currentTool;

		if ( !needsRebuild && currentTool != null )
		{
			needsRebuild = currentTool.CurrentTool != currentSubTool;
		}

		if ( !needsRebuild )
			return;

		lastTool = currentTool;

		toolExtensionContainer.Layout.Clear( true );

		if ( currentTool != null )
		{
			var widgetsToAdd = new List<Widget>();

			if ( currentSubTool != null && currentSubTool != currentTool )
			{
				var subToolWidget = currentSubTool.CreateToolbarWidget();
				if ( subToolWidget != null )
				{
					widgetsToAdd.Add( subToolWidget );
				}
			}

			var mainToolWidget = currentTool.CreateToolbarWidget();
			if ( mainToolWidget != null )
			{
				widgetsToAdd.Add( mainToolWidget );
			}

			if ( widgetsToAdd.Count > 0 )
			{
				AddSeparator( toolExtensionContainer.Layout );

				foreach ( var widget in widgetsToAdd )
				{
					toolExtensionContainer.Layout.Add( widget );
				}
			}
		}
	}
}
