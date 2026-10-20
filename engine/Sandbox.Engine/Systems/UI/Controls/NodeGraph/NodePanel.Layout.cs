namespace Sandbox.UI.NodeGraph;

//
// Plug panels and layout, rebuilt from the node whenever it changes.
//
public partial class NodePanel
{
	bool _updatingPlugs;

	void UpdatePlugs()
	{
		// Cutting a removed plug's wires marks this node changed again; this pass covers it
		if ( _updatingPlugs ) return;
		_updatingPlugs = true;

		try
		{
			SyncPlugs();
		}
		finally
		{
			_updatingPlugs = false;
		}
	}

	void SyncPlugs()
	{
		for ( var i = Inputs.Count - 1; i >= 0; --i )
		{
			var plugIn = Inputs[i];
			if ( !Node.Inputs.Contains( plugIn.Inner ) )
			{
				Inputs.RemoveAt( i );
				if ( !IsPreview ) Graph.RemoveConnections( plugIn );
				plugIn.Delete( true );
			}
		}

		for ( var i = Outputs.Count - 1; i >= 0; --i )
		{
			var plugOut = Outputs[i];
			if ( !Node.Outputs.Contains( plugOut.Inner ) )
			{
				Outputs.RemoveAt( i );
				if ( !IsPreview ) Graph.RemoveConnections( plugOut );
				plugOut.Delete( true );
			}
		}

		var index = 0;
		foreach ( var plug in Node.Inputs )
		{
			var match = Inputs.FirstOrDefault( x => x.Inner == plug );

			if ( match is null )
			{
				Inputs.Insert( Math.Min( index, Inputs.Count ), AddChild( new PlugInPanel( this, plug ) ) );
			}
			else if ( Inputs.IndexOf( match ) != index )
			{
				Inputs.Remove( match );
				Inputs.Insert( Math.Min( index, Inputs.Count ), match );
			}

			++index;
		}

		index = 0;
		foreach ( var plug in Node.Outputs )
		{
			var match = Outputs.FirstOrDefault( x => x.Inner == plug );

			if ( match is null )
			{
				Outputs.Insert( Math.Min( index, Outputs.Count ), AddChild( new PlugOutPanel( this, plug ) ) );
			}
			else if ( Outputs.IndexOf( match ) != index )
			{
				Outputs.Remove( match );
				Outputs.Insert( Math.Min( index, Outputs.Count ), match );
			}

			++index;
		}

		Layout();
	}

	/// <summary>
	/// A node without a title bar but with a thumbnail is drawn as the image itself, plugs on its edges.
	/// </summary>
	public bool IsThumbnailCard => !Node.HasTitleBar && Node.Thumbnail is not null;

	const float HeaderPadLeft = 9f;
	const float HeaderPadRight = 14f;

	/// <summary>
	/// The icon column: wider when a subtitle makes the header two lines tall and the icon bigger.
	/// </summary>
	static float HeaderIconWidth( bool twoLines ) => twoLines ? 27f : 20f;

	/// <summary>
	/// Width the header needs for its icon, title and subtitle, in the fonts they're drawn with.
	/// </summary>
	float HeaderWidth()
	{
		var text = NodePaint.Measure( DisplayInfo.Name, 8f, 600 ).x;

		if ( Node.HasSubtitle )
		{
			text = MathF.Max( text, NodePaint.Measure( Node.Subtitle, 7f ).x );
		}

		return HeaderPadLeft + (DisplayInfo.Icon is null ? 0f : HeaderIconWidth( Node.HasSubtitle )) + text + HeaderPadRight;
	}

	protected virtual void Layout()
	{
		var hasThumb = !Node.HasTitleBar && Node.DisplayInfo.Icon is not null || Node.Thumbnail is not null;
		var inputHeight = Inputs.Sum( x => x.Inner.InTitleBar ? 0f : 24f );
		var outputHeight = Outputs.Sum( x => x.Inner.InTitleBar ? 0f : 24f );
		var thumbnailSize = IsThumbnailCard ? 96f : hasThumb ? Node.Thumbnail is not null ? 88f : 24f : 0f;
		var bodyHeight = MathF.Max( MathF.Max( inputHeight, outputHeight ), thumbnailSize );

		var totalWidth = 160f;
		var inputWidth = 80f;
		var outputWidth = 80f;

		if ( Node.AutoSize )
		{
			var titleWidth = Node.HasTitleBar ? HeaderWidth() : 0f;

			if ( Inputs.Any( x => x.Inner.InTitleBar ) )
			{
				titleWidth += 24f;
			}

			if ( Outputs.Any( x => x.Inner.InTitleBar ) )
			{
				titleWidth += 24f;
			}

			inputWidth = Inputs
				.Select( x => x.PreferredWidth )
				.DefaultIfEmpty( 0f )
				.Max();
			outputWidth = Outputs
				.Select( x => x.PreferredWidth )
				.DefaultIfEmpty( 0f )
				.Max();

			totalWidth = Math.Max( 24f, Math.Max( titleWidth, inputWidth + outputWidth + thumbnailSize + (Node.HasTitleBar ? 8f : 0f) ) );
		}

		// The card's plugs are unlabelled, so the image takes the whole width
		if ( IsThumbnailCard )
		{
			inputWidth = 0f;
			outputWidth = 0f;
			totalWidth = thumbnailSize;
		}

		var verticalCenter = !Node.HasTitleBar;

		totalWidth += Node.ExpandSize.x;
		bodyHeight += Node.ExpandSize.y;

		if ( verticalCenter )
		{
			bodyHeight = MathF.Ceiling( bodyHeight / Graph.GridSize ) * Graph.GridSize;
		}

		var totalHeight = TitleHeight + bodyHeight;

		if ( !Node.HasTitleBar )
		{
			var size = Math.Max( totalWidth, totalHeight );

			(totalWidth, totalHeight) = (Math.Max( 36f, size ), size);
		}

		// Up, not to nearest, so the header keeps its right padding
		totalWidth = MathF.Ceiling( totalWidth / Graph.GridSize ) * Graph.GridSize;
		totalHeight = totalHeight.SnapToGrid( Graph.GridSize );

		var top = TitleHeight + (verticalCenter ? totalHeight - inputHeight : 0f) * 0.5f;
		var index = 0;

		top = top.SnapToGrid( Graph.GridSize );

		var handleOffset = 6f;

		foreach ( var input in Inputs )
		{
			if ( input.Inner.InTitleBar )
			{
				input.Position = new Vector2( -handleOffset, 0f );
				input.Size = input.Size.WithX( 24f );
			}
			else
			{
				var plugWidth = inputWidth;

				if ( index >= Outputs.Count && input.Inner.AllowStretch )
				{
					plugWidth = totalWidth;
				}

				plugWidth = Math.Max( 24f, plugWidth );

				input.Position = new Vector2( -handleOffset, top );
				input.Size = input.Size.WithX( plugWidth );

				top += 24f;
				++index;
			}

			input.Layout();
		}

		top = TitleHeight + (verticalCenter ? totalHeight - outputHeight : 0f) * 0.5f;
		top = top.SnapToGrid( Graph.GridSize );

		index = 0;

		foreach ( var output in Outputs )
		{
			if ( output.Inner.InTitleBar )
			{
				output.Position = new Vector2( totalWidth - 24f + handleOffset, 0f );
				output.Size = output.Size.WithX( 24f );
			}
			else
			{
				var plugWidth = Math.Max( 24f, outputWidth );

				if ( index >= Inputs.Count && output.Inner.AllowStretch )
				{
					plugWidth = totalWidth;
				}

				output.Position = new Vector2( totalWidth - plugWidth + handleOffset, top );
				output.Size = output.Size.WithX( plugWidth );

				top += 24f;
				++index;
			}

			output.Layout();
		}

		Size = new Vector2( totalWidth, totalHeight );
		Style.Width = totalWidth;
		Style.Height = totalHeight;

		_thumbRect = IsThumbnailCard
			? new Rect( 0f, Size ).Shrink( 4f )
			: new Rect( inputWidth, TitleHeight > 0f ? TitleHeight - 2f : 0f, Size.x - inputWidth - outputWidth, Size.y - TitleHeight );

		if ( hasThumb && Node.HasTitleBar )
		{
			_thumbRect = _thumbRect.Shrink( 6f, 5f, 6f, 4f );

			if ( inputWidth <= 0f != outputWidth <= 0f )
			{
				_thumbRect = _thumbRect.Align( thumbnailSize - 16f, inputWidth <= 0f ? TextFlag.LeftCenter : TextFlag.RightCenter );
			}
		}
	}
}
