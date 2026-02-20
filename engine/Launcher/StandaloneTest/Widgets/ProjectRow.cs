using System.Text;

namespace Editor;

public partial class ProjectRow : ItemRow
{
	public delegate void OpenProjectDelegate( string args );

	public Action OnPinStateChanged { get; set; }
	public Action OnProjectRemove { get; set; }
	public OpenProjectDelegate OnProjectOpen { get; set; }

	protected Project Project { get; }
	protected Package Package { get; set; }

	private ControlModeSettings ControlModes => Project.Config.GetMetaOrDefault( "ControlModes", new ControlModeSettings() );

	private IconButton PinButton { get; set; }
	private IconButton MoreButton { get; set; }

	public ProjectRow( Project project, Widget parent ) : base( parent )
	{
		Project = project;
		Title = project.Config.Title;

		_ = UpdatePackageAsync();

		Init();
	}

	protected override List<InfoItem> GetInfo()
	{
		List<InfoItem> info;

		if ( Project.Config.Org == "local" )
		{
			info = new()
			{
				// Type
				( "folder", "Local" ),

				// Last opened
				( "schedule", Project.LastOpened.LocalDateTime.ToRelativeTimeString() ?? "never" )
			};
		}
		else
		{
			info = new()
			{
				// Owner
				( "group", Project.Package?.Org.Title ),

				// Last opened
				( "schedule", Project.LastOpened.LocalDateTime.ToRelativeTimeString() ?? "never" )
			};
		}

		// Control mode info
		if ( ControlModes.VR )
		{
			info.Add( ("panorama_photosphere", ControlModes.IsVROnly ? "VR Only" : "VR Compatible") );
		}

		return info;
	}

	protected override void CreateUI()
	{
		Cursor = CursorShape.Finger;

		// Add menu button
		MoreButton = AddButton( "more_vert", "More..." );
		MoreButton.OnClick = () =>
		{
			var menu = OpenContextMenu();
			menu.OpenAt( MoreButton.ScreenPosition );
		};

		MoreButton.OnPaintOverride = () =>
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();

			Paint.SetPen( Theme.SurfaceLightBackground );

			if ( Paint.HasMouseOver )
				Paint.SetPen( Theme.TextLight );

			Paint.DrawIcon( MoreButton.LocalRect, "more_vert", 16.0f );

			return true;
		};

		MoreButton.Visible = false;

		// Add pin button
		string GetTooltip() => Project.Pinned ? "Unpin this project" : "Pin this project";
		PinButton = AddButton( "push_pin", GetTooltip(), () =>
		{
			Project.Pinned = !Project.Pinned;
			PinButton.ToolTip = GetTooltip();

			OnPinStateChanged?.Invoke();
		} );

		PinButton.OnPaintOverride = () =>
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();

			if ( Project.Pinned )
				Paint.SetPen( Theme.Text );
			else if ( Paint.HasMouseOver )
				Paint.SetPen( Theme.TextLight );
			else
				Paint.SetPen( Theme.SurfaceLightBackground );

			Paint.DrawIcon( PinButton.LocalRect, "push_pin", 16.0f );

			return true;
		};

		// Only visible on mouse over or if pinned
		PinButton.Visible = Project.Pinned || false;

		_ = UpdatePackageAsync();
	}

	protected async Task UpdatePackageAsync()
	{
		if ( Project.Config.Org == "local" )
			return;

		Package = await Package.Fetch( Project.Config.FullIdent, true );

		if ( !this.IsValid() )
			return;

		Update();
	}

	public override void OnClick()
	{
		OpenProject();
	}

	private string GetLaunchArgs( LaunchFlags launchFlags )
	{
		var args = new StringBuilder();
		if ( launchFlags.Contains( LaunchFlags.VR ) ) args.Append( " -vr" );
		if ( launchFlags.Contains( LaunchFlags.VulkanValidation ) ) args.Append( " -vulkan_enable_validation -vulkan_validation_error_assert" );
		if ( launchFlags.Contains( LaunchFlags.VRDebug ) ) args.Append( " -vrdebug" );

		return args.ToString();
	}

	private void OpenProject( LaunchFlags launchFlags = LaunchFlags.None )
	{
		if ( (DateTime.Now - Project.LastOpened).TotalSeconds < 2.0f )
			return;

		var args = GetLaunchArgs( launchFlags );
		OnProjectOpen?.Invoke( args );

		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var secondsSinceOpen = (float)(DateTime.Now - Project.LastOpened).TotalSeconds;

		if ( secondsSinceOpen < 2.0f )
		{
			var delta = secondsSinceOpen.Remap( 0, 2, 0, 1 );
			var inout = MathX.Clamp( MathF.Sin( delta * MathF.PI ) * 2.0f, 0, 1 );
			var alpha = MathF.Sin( RealTime.Now * 4.0f ).Remap( -1, 1, 0.5f, 0.8f ) * inout;
			Paint.SetBrushAndPen( Theme.Green.WithAlpha( alpha * 0.5f ), Theme.Green.WithAlphaMultiplied( alpha ), 2.0f );
			Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );

			var textRect = LocalRect.Shrink( 8 );


			Paint.SetHeadingFont( 15, 500 );
			Paint.Pen = Theme.Green.WithAlpha( inout );

			textRect.Left += -600 + delta * 600.0f;
			Paint.DrawText( textRect, "LAUNCHING LAUNCHING LAUNCHING LUNCHING LAUNCHING LAUNCHING LAUNCHING LAUNCHING LAUNCHING LAUNCHING LAUNCHING", TextFlag.LeftCenter );

			Update();
			return;
		}
	}

	protected override void OnPaintIcon( Rect iconRect )
	{
		bool hasThumb = !string.IsNullOrEmpty( Package?.Thumb ) && Package.Thumb.StartsWith( "http" );

		if ( hasThumb )
		{
			Paint.Draw( iconRect, Package.Thumb, borderRadius: 4 );
		}
		else
		{
			Paint.SetBrushAndPen( Theme.SurfaceBackground );
			Paint.DrawRect( iconRect, 4 );

			Paint.Pen = Theme.Text;
			Paint.DrawIcon( iconRect, "sports_esports", iconRect.Height * 0.6f );
		}
	}

	protected override void OnMouseEnter()
	{
		// Pin button is always visible if a project is pinned
		PinButton.Visible = Project.Pinned || true;
		PinButton.Update();

		MoreButton.Visible = true;
		MoreButton.Update();

		Update();
	}

	protected override void OnMouseLeave()
	{
		// Pin button is always visible if a project is pinned
		PinButton.Visible = Project.Pinned || false;
		PinButton.Update();

		MoreButton.Visible = false;
		MoreButton.Update();

		Update();
	}
}

