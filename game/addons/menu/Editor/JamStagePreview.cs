using System;
using System.Threading.Tasks;
using Editor.Mcp;
using MenuProject;
using Sandbox.Services;

public static class JamStagePreview
{
	[McpTool( "jam_preview_status" )]
	public static object Status()
	{
		var voting = GameJamSystem.For( MainMenu.Instance.Panel );
		return new
		{
			Preview = Jam.IsEditorPreview,
			Jam.PreviewDays,
			voting.ActiveJam.Now,
			voting.ActiveJam.Results,
			voting.Phase,
			voting.IsLoadingFinalists,
			voting.FinalistsError,
			Categories = voting.Finalists?.Select( x => new { x.Id, x.Decided, x.Winner, x.RoundEnds, x.VotingOpen, x.Counts, Nominees = x.Nominees } ).ToArray()
		};
	}

	[McpTool( "jam_preview_results" )]
	public static async Task<object> Results()
	{
		var voting = GameJamSystem.For( MainMenu.Instance.Panel );
		if ( !Jam.IsEditorPreview ) throw new InvalidOperationException( "Start the jam preview first." );
		await voting.SeekEditorPreviewResultsAsync( voting.ActiveJam.Results.AddSeconds( 5 ) );
		return Status();
	}

	[McpTool( "jam_preview_grand_final" )]
	public static object Show()
	{
		var voting = GameJamSystem.For( MainMenu.Instance.Panel );
		var start = voting.ActiveJam.GrandFinal.Value;
		Jam.PreviewDays = 1;
		voting.SeekEditorPreview( start.AddMinutes( 5 ) );
		MainMenu.Instance.Navigator.Navigate( "/home" );
		var feed = MainMenu.Instance.Panel.Descendants.OfType<MenuProject.MenuUI.Front.FrontPageGames>().FirstOrDefault();
		feed?.ScrollTo( Vector2.Zero );
		return new { Preview = Jam.IsEditorPreview, voting.ActiveJam.Now };
	}
}
