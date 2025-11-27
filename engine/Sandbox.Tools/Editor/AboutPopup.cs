using System;
using System.IO;

namespace Editor;

public class AboutWidget : BaseWindow
{
	private ScrollArea ComponentsScrollArea;
	private Layout ContentLayout;
	public class DependencyIndex
	{
		public string Statement { get; set; }
		public List<DependencyComponent> Components { get; set; }
	}

	public class DependencyComponent
	{
		public string Name { get; set; }
		public string Comment { get; set; }
		public List<string> UsedBy { get; set; }
		public string License { get; set; }
		public string ProjectUrl { get; set; }
		public string Linkage { get; set; }
		public List<string> Copyright { get; set; }
		public bool IsLicenseExpanded { get; set; } = false;
	}

	public AboutWidget() : base()
	{
		WindowTitle = "About s&box editor";
		SetWindowIcon( "info" );
		DeleteOnClose = true;

		Size = new( 700, 600 );

		// Setup main layout
		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 4;

		// Add header
		var messageLabel = Layout.Add( new Label() );
		messageLabel.Text = "s&box editor © Facepunch Studios Ltd.";
		messageLabel.TextSelectable = false;

		// Add version
		var versionLabel = Layout.Add( new Label() );
		versionLabel.Text = $"Version {Sandbox.Application.Version}";
		versionLabel.TextSelectable = true;

		// Add content layout
		ContentLayout = Layout.AddColumn( 1 );
		ContentLayout.Margin = new Sandbox.UI.Margin( 0, 16, 0, 0 );
		ContentLayout.Spacing = 4;

		LoadThirdPartyData();
	}

	private void LoadThirdPartyData()
	{
		try
		{
			var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
			var indexData = Json.Deserialize<DependencyIndex>( fileData );

			if ( indexData?.Components != null )
			{
				var components = indexData.Components.OrderBy( c => c.Name ).ToList();
				PopulateComponentList( indexData, components );
			}
			else
			{
				var label = new Label( "No third-party component data found." );
				label.WordWrap = true;
				ComponentsScrollArea.Canvas.Layout.Add( label );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to load third party components: {ex.Message}" );
			var label = new Label( $"Failed to load third-party component information: {ex.Message}" );
			label.WordWrap = true;
			ContentLayout.Add( label );
		}
	}

	private void PopulateComponentList( DependencyIndex indexData, List<DependencyComponent> components )
	{
		// Heading
		var heading = new Label( "Third-Party Components" );
		heading.SetStyles( "font-size: 18px; font-weight: 700;" );
		ContentLayout.Add( heading );
		ContentLayout.AddSpacingCell( 10 );

		// Statement
		if ( !string.IsNullOrEmpty( indexData.Statement ) )
		{
			var statementLabel = new Label( indexData.Statement );
			statementLabel.WordWrap = true;
			ContentLayout.Add( statementLabel );
			ContentLayout.AddSpacingCell( 20 );
		}

		// Add scroll area to content layout
		ComponentsScrollArea = ContentLayout.Add( new ScrollArea( this ), 1 );
		ComponentsScrollArea.Canvas = new Widget( ComponentsScrollArea );
		ComponentsScrollArea.Canvas.Layout = Layout.Column();
		ComponentsScrollArea.Canvas.Layout.Margin = 4;
		ComponentsScrollArea.Canvas.Layout.Spacing = 8;

		foreach ( var component in components )
		{
			var componentRow = ComponentsScrollArea.Canvas.Layout.AddRow();
			componentRow.Spacing = 4;
			componentRow.Margin = new Sandbox.UI.Margin( 4 );

			var componentCol = componentRow.AddColumn();

			var nameLabel = new Label( component.Name );
			nameLabel.SetStyles( "font-size: 16px; font-weight: 700;" );
			componentCol.Add( nameLabel );
			componentCol.AddSpacingCell( 4 );

			if ( !string.IsNullOrEmpty( component.Comment ) )
			{
				var commentLabel = new Label( component.Comment );
				commentLabel.WordWrap = true;
				componentCol.Add( commentLabel );
				componentCol.AddSpacingCell( 4 );
			}

			var copyrightText = "Copyright © " + string.Join( "; ", component.Copyright ?? new List<string>() );
			var copyrightLabel = new Label( copyrightText );
			copyrightLabel.WordWrap = true;
			componentCol.Add( copyrightLabel );

			componentCol.AddSpacingCell( 2 );

			var licenseLabel = new Label( $"License: {component.License}" );
			licenseLabel.WordWrap = true;
			componentCol.Add( licenseLabel );

			componentCol.AddSpacingCell( 2 );

			var homepageLabel = new Label( $"Homepage: {component.ProjectUrl}" );
			homepageLabel.WordWrap = true;
			componentCol.Add( homepageLabel );

			componentCol.AddSpacingCell( 4 );

			var licenseTextEdit = new TextEdit( ComponentsScrollArea.Canvas );
			licenseTextEdit.ReadOnly = true;
			licenseTextEdit.Editable = false;
			licenseTextEdit.MinimumHeight = 200;
			licenseTextEdit.FocusMode = FocusMode.None;
			licenseTextEdit.Visible = false;
			licenseTextEdit.Name = $"LicenseText_{component.Name}";

			if ( component.License != "Public-Domain" )
			{
				// Add toggle button
				var licenseButton = new Button( "Full License Text" );
				licenseButton.Icon = component.IsLicenseExpanded ? "expand_more" : "navigate_next";
				licenseButton.Clicked = () =>
				{
					component.IsLicenseExpanded = !component.IsLicenseExpanded;
					licenseButton.Icon = component.IsLicenseExpanded ? "expand_more" : "navigate_next";
					licenseTextEdit.Visible = component.IsLicenseExpanded;

					if ( component.IsLicenseExpanded && string.IsNullOrEmpty( licenseTextEdit.PlainText ) )
					{
						try
						{
							var componentId = component.Name.ToLower().Replace( " ", "-" );
							var licenseFileName = $"thirdpartylegalnotices/licenses/{componentId}";
							var licenseText = File.ReadAllText( licenseFileName );
							licenseTextEdit.PlainText = licenseText;
						}
						catch ( Exception ex )
						{
							licenseTextEdit.PlainText = $"Unable to load license text: {ex.Message}";
						}
					}
				};
				componentCol.Add( licenseButton );
			}

			if ( component.License != "Public-Domain" )
			{
				ComponentsScrollArea.Canvas.Layout.Add( licenseTextEdit );
			}

			ComponentsScrollArea.Canvas.Layout.AddSpacingCell( 10 );
		}

		ComponentsScrollArea.Canvas.Layout.AddStretchCell();
	}
}
