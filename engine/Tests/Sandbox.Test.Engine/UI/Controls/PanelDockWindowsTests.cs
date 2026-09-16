using Editor;
using Sandbox.UI;
using System;
using System.Text.Json;

namespace UITests.Controls;

/// <summary>Desktop workspace validation without native windows.</summary>
[TestClass]
public class PanelDockWindowsTests
{
	static PanelDockWindows.WorkspaceState Workspace()
	{
		var main = new DockLayout();
		main.Dock( "a" );
		var floating = new DockLayout();
		floating.Dock( "b" );
		floating.Dock( "c", "b", DockPosition.Bottom, 0.3f );
		return new()
		{
			Version = 1,
			Main = new() { Items = ["a"], Layout = main.Save() },
			Windows = [new()
			{
				X = -420, Y = 70, Width = 640, Height = 480,
				Region = new() { Items = ["b", "c", "closed"], Layout = floating.Save() }
			}]
		};
	}

	static string Json( PanelDockWindows.WorkspaceState state ) => JsonSerializer.Serialize( state, PanelDockWindows.WorkspaceState.JsonOptions );

	/// <summary>Window geometry, split fractions and closed registrations survive serialization.</summary>
	[TestMethod]
	public void WorkspaceRoundTripIncludesFloatingState()
	{
		var original = Workspace();
		Assert.IsTrue( PanelDockWindows.WorkspaceState.TryRead( Json( original ), Ids, ["b"], out var restored ) );
		Assert.AreEqual( Json( original ), Json( restored ) );
		Assert.AreEqual( -420, restored.Windows[0].X );
		CollectionAssert.Contains( restored.Windows[0].Region.Items, "closed" );
	}

	/// <summary>State is ordinary nested JSON and older escaped layout strings remain readable.</summary>
	[TestMethod]
	public void WorkspaceSupportsNestedAndLegacyLayoutJson()
	{
		var original = Workspace();
		var json = Json( original );
		using var document = JsonDocument.Parse( json );
		Assert.AreEqual( JsonValueKind.Object, document.RootElement.GetProperty( "main" ).GetProperty( "layout" ).ValueKind );
		var legacy = json.Replace( original.Main.Layout, JsonSerializer.Serialize( original.Main.Layout ) )
			.Replace( original.Windows[0].Region.Layout, JsonSerializer.Serialize( original.Windows[0].Region.Layout ) );
		Assert.IsTrue( PanelDockWindows.WorkspaceState.TryRead( legacy, Ids, [], out var restored ) );
		Assert.AreEqual( json, Json( restored ) );
	}

	/// <summary>Unknown, duplicate and mismatched registrations or invalid geometry are rejected.</summary>
	[TestMethod]
	[DataRow( "duplicate" )]
	[DataRow( "unknown" )]
	[DataRow( "missing-registration" )]
	[DataRow( "empty-window" )]
	[DataRow( "width" )]
	[DataRow( "position" )]
	[DataRow( "version" )]
	public void InvalidWorkspaceIsRejected( string error )
	{
		var state = Workspace();
		switch ( error )
		{
			case "duplicate": state.Main.Items = ["a", "closed"]; break;
			case "unknown": state.Main.Items = ["a", "unknown"]; break;
			case "missing-registration": state.Windows[0].Region.Items = ["c", "closed"]; break;
			case "empty-window": state.Windows[0].Region.Layout = new DockLayout().Save(); break;
			case "width": state.Windows[0].Width = 0; break;
			case "position": state.Windows[0].X = float.MaxValue; break;
			case "version": state.Version = 2; break;
		}
		Assert.IsFalse( PanelDockWindows.WorkspaceState.TryRead( Json( state ), Ids, [], out _ ) );
	}

	/// <summary>Nonclosable panels must remain open somewhere, including in floating windows.</summary>
	[TestMethod]
	public void WorkspaceValidatesNonclosablePanelsAcrossWindows()
	{
		Assert.IsTrue( PanelDockWindows.WorkspaceState.TryRead( Json( Workspace() ), Ids, ["b"], out _ ) );
		Assert.IsFalse( PanelDockWindows.WorkspaceState.TryRead( Json( Workspace() ), Ids, ["closed"], out _ ) );
	}

	/// <summary>Malformed workspace data never starts a restore.</summary>
	[TestMethod]
	[DataRow( "null" )]
	[DataRow( "{}" )]
	[DataRow( "{" )]
	public void MalformedWorkspaceIsRejected( string json )
	{
		Assert.IsFalse( PanelDockWindows.WorkspaceState.TryRead( json, Ids, [], out _ ) );
	}

	static readonly string[] Ids = ["a", "b", "c", "closed"];

}
