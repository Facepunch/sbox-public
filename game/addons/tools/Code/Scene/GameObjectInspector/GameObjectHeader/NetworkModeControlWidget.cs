namespace Editor;

class NetworkModeControlWidget : Widget
{
	SerializedObject Target;
	SerializedProperty ModeProperty;

	Widget additionalOptions;

	public NetworkModeControlWidget( SerializedObject targetObject )
	{
		Target = targetObject;
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.CanShrink;
		ModeProperty = Target.GetProperty( nameof( GameObject.NetworkMode ) );
		ToolTip = "View Network Settings";
	}


	protected override Vector2 SizeHint()
	{
		return new( Theme.RowHeight, Theme.RowHeight );
	}

	protected override Vector2 MinimumSizeHint()
	{
		return new( Theme.RowHeight, Theme.RowHeight );
	}

	protected override void OnDoubleClick( MouseEvent e ) { }

	protected override void OnMousePress( MouseEvent e )
	{
		if ( ReadOnly ) return;
		OpenNetworkSettings();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( ModeProperty is null ) return;

		var rect = LocalRect.Shrink( 2 );
		var icon = "wifi_off";

		var value = (NetworkMode)ModeProperty.As.Int;

		if ( value == NetworkMode.Object )
			icon = "wifi";
		else if ( value == NetworkMode.Snapshot )
			icon = "network_wifi_2_bar";

		if ( value != NetworkMode.Never )
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.3f ), 1 );
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect, 2 );

			Paint.SetPen( Theme.Blue );
			Paint.DrawIcon( rect, icon, 13 );
		}
		else
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, icon, 13 );
		}

		if ( IsUnderMouse )
		{
			Paint.SetPen( Theme.Blue.WithAlpha( 0.5f ), 1 );
			Paint.ClearBrush();
			Paint.DrawRect( rect, 1 );
		}
	}

	void OpenNetworkSettings()
	{
		var menu = new PopupWidget( null )
		{
			Layout = Layout.Column(),
			MinimumWidth = 250,
			MaximumWidth = 300
		};
		menu.Layout.Margin = 8;
		menu.Layout.Spacing = 4;

		var gameObjects = Target.Targets.Cast<GameObject>().ToArray();
		var gameObject = gameObjects.FirstOrDefault();
		if ( !gameObject.IsValid() ) return;

		var accessorType = TypeLibrary.GetType( "NetworkAccessor" );
		if ( accessorType is null ) return;

		var modeProperty = Target.GetProperty( nameof( GameObject.NetworkMode ) );
		var modeWidget = CreateOptionsWidget( modeProperty.DisplayName, modeProperty.Description, modeProperty.GetValue<NetworkMode>(), v =>
		{
			using ( SceneEditorSession.Active.UndoScope( modeProperty.DisplayName ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push() )
			{
				foreach ( var go in gameObjects )
				{
					go.NetworkMode = v;
				}
			}

			additionalOptions.Enabled = v == NetworkMode.Object;
			Update();
		} );
		menu.Layout.Add( modeWidget );

		additionalOptions = new()
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.Default,
			Enabled = modeProperty.GetValue<NetworkMode>() == NetworkMode.Object
		};
		additionalOptions.Layout.Spacing = 4;

		menu.Layout.Add( additionalOptions );

		{
			if ( Target.GetProperty( nameof( GameObject.Network ) ).TryGetAsObject( out var networkProperty ) )
			{
				var flagsProperty = networkProperty.GetProperty( nameof( GameObject.Network.Flags ) );
				var networkFlagsWidget = CreateFlagsWidget( "Network Flags", flagsProperty, gameObject.Network.Flags, v =>
				{
					using ( SceneEditorSession.Active.UndoScope( "Network Flags" ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push() )
					{
						foreach ( var go in gameObjects )
						{
							go.Network.Flags = v;
						}
					}
				} );

				additionalOptions.Layout.Add( networkFlagsWidget );
			}
		}

		var orphanedModeProperty = accessorType.GetProperty( nameof( GameObject.Network.NetworkOrphaned ) );
		var orphanedModeWidget = CreateOptionsWidget( "Orphaned Mode", orphanedModeProperty.Description, gameObject.Network.NetworkOrphaned, v =>
		{
			using ( SceneEditorSession.Active.UndoScope( "Orphaned Mode" ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push() )
			{
				foreach ( var go in gameObjects )
				{
					go.Network.SetOrphanedMode( v );
				}
			}
		} );
		additionalOptions.Layout.Add( orphanedModeWidget );

		var ownerTransferProperty = accessorType.GetProperty( nameof( GameObject.Network.OwnerTransfer ) );
		var ownerTransferWidget = CreateOptionsWidget( "Owner Transfer", ownerTransferProperty.Description, gameObject.Network.OwnerTransfer, v =>
		{
			using ( SceneEditorSession.Active.UndoScope( "Owner Transfer" ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push() )
			{
				foreach ( var go in gameObjects )
				{
					go.Network.SetOwnerTransfer( v );
				}
			}
		} );
		additionalOptions.Layout.Add( ownerTransferWidget );

		{
			var property = accessorType.GetProperty( nameof( GameObject.Network.AlwaysTransmit ) );
			var widget = CreateBoolWidget( property, () => gameObject.Network.AlwaysTransmit, "Always Transmit", menu, v =>
			{
				using ( SceneEditorSession.Active.UndoScope( "Always Transmit" ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push() )
				{
					foreach ( var go in gameObjects )
					{
						go.Network.AlwaysTransmit = v;
					}
				}

				Update();
			} );

			additionalOptions.Layout.Add( widget );
		}

		menu.Position = ScreenRect.BottomLeft;
		menu.Visible = true;
		menu.AdjustSize();
		menu.ConstrainToScreen();
		menu.OnPaintOverride = PaintMenuBackground;
	}

	private Widget CreateBoolWidget( PropertyDescription property, Func<bool> getter, string title, PopupWidget menu, Action<bool> onChanged )
	{
		var widget = new Widget
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanShrink
		};

		var label = new Label( title );
		label.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );
		label.HorizontalSizeMode = SizeMode.CanShrink;
		label.VerticalSizeMode = SizeMode.CanShrink;
		widget.Layout.Add( label );

		var content = new Widget
		{
			Layout = Layout.Row(),
			VerticalSizeMode = SizeMode.CanShrink
		};
		widget.Layout.Add( content );

		BoolControlWidget boolWidget = null;

		var fakeProperty = TypeLibrary.CreateProperty( title, getter, onChanged );

		boolWidget = new BoolControlWidget( fakeProperty )
		{
			MaximumWidth = Theme.RowHeight,
			MaximumHeight = Theme.RowHeight
		};
		content.Layout.Add( boolWidget );
		content.Layout.AddSpacingCell( 4 );

		var iconLabel = new Label( "linear_scale" );
		iconLabel.Color = Theme.Text.WithAlpha( menu.Enabled ? 1 : 0.5f );
		iconLabel.SetStyles( "font-family: Material Icons; font-size: 16px;" );
		iconLabel.HorizontalSizeMode = SizeMode.CanShrink;
		content.Layout.Add( iconLabel );
		content.Layout.AddSpacingCell( 4 );

		var description = new Label( "<p>" + property.Description + "</p>" );
		description.Color = Theme.TextLight.WithAlpha( menu.Enabled ? 1 : 0.5f );
		description.WordWrap = true;
		description.HorizontalSizeMode = SizeMode.CanGrow;
		description.VerticalSizeMode = SizeMode.CanShrink;
		content.Layout.Add( description );
		return widget;
	}

	private Widget CreateBoolWidget( SerializedProperty property, string title, PopupWidget menu )
	{
		var widget = new Widget
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanShrink
		};

		var label = new Label( title );
		label.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );
		label.HorizontalSizeMode = SizeMode.CanShrink;
		label.VerticalSizeMode = SizeMode.CanShrink;
		widget.Layout.Add( label );

		var content = new Widget
		{
			Layout = Layout.Row(),
			VerticalSizeMode = SizeMode.CanShrink
		};
		widget.Layout.Add( content );

		var boolWidget = new BoolControlWidget( property );
		boolWidget.MaximumWidth = Theme.RowHeight;
		boolWidget.MaximumHeight = Theme.RowHeight;
		content.Layout.Add( boolWidget );
		content.Layout.AddSpacingCell( 4 );

		var iconLabel = new Label( "linear_scale" );
		iconLabel.Color = Theme.Text.WithAlpha( menu.Enabled ? 1 : 0.5f );
		iconLabel.SetStyles( "font-family: Material Icons; font-size: 16px;" );
		iconLabel.HorizontalSizeMode = SizeMode.CanShrink;
		content.Layout.Add( iconLabel );
		content.Layout.AddSpacingCell( 4 );

		var description = new Label( "<p>" + property.Description + "</p>" );
		description.Color = Theme.TextLight.WithAlpha( menu.Enabled ? 1 : 0.5f );
		description.WordWrap = true;
		description.HorizontalSizeMode = SizeMode.CanGrow;
		description.VerticalSizeMode = SizeMode.CanShrink;
		content.Layout.Add( description );
		return widget;
	}

	Widget CreateFlagsWidget<T>( string name, SerializedProperty property, T value, Action<T> onSelected = null ) where T : Enum
	{
		var widget = new Widget
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.Default
		};

		var column = widget.Layout;

		var label = new Label( name );
		label.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );
		label.HorizontalSizeMode = SizeMode.CanShrink;
		label.VerticalSizeMode = SizeMode.CanShrink;
		column.Add( label );

		var descriptionLabel = new Label( "<p>" + property.Description + "</p>" );
		descriptionLabel.SetStyles( "color: gray;" );
		descriptionLabel.WordWrap = true;
		descriptionLabel.VerticalSizeMode = SizeMode.CanShrink;
		column.Add( descriptionLabel );

		column.AddSpacingCell( 4 );

		var selector = new EnumControlWidget( property );
		selector.VerticalSizeMode = SizeMode.CanShrink;
		column.Add( selector );

		return widget;
	}

	Widget CreateOptionsWidget<T>( string name, string description, T value, Action<T> onSelected = null ) where T : Enum
	{
		var enumDesc = EditorTypeLibrary.GetEnumDescription( typeof( T ) );
		var widget = new Widget
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanShrink
		};

		var column = widget.Layout;

		var label = new Label( name );
		label.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );
		label.HorizontalSizeMode = SizeMode.CanShrink;
		label.VerticalSizeMode = SizeMode.CanShrink;
		column.Add( label );

		var descriptionLabel = new Label( "<p>" + description + "</p>" );
		descriptionLabel.SetStyles( "color: gray;" );
		descriptionLabel.WordWrap = true;
		descriptionLabel.VerticalSizeMode = SizeMode.CanShrink;
		column.Add( descriptionLabel );

		column.AddSpacingCell( 4 );

		var currentValue = (int)(object)value;
		var radioSelect = new RadioSelectWidget<T>();
		radioSelect.OnValueChanged += ( v ) =>
		{
			onSelected?.Invoke( v );
		};

		foreach ( var e in enumDesc )
		{
			var option = radioSelect.AddOption( e.Title, e.Icon, (T)e.ObjectValue );
			option.ToolTip = e.Description;

			if ( currentValue == e.IntegerValue )
				option.IsSelected = true;
		}

		column.Add( radioSelect );
		return widget;
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
		return true;
	}
}
