using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>Retains a scope's identity when accepting autocomplete, including names shared by several sites.</summary>
internal sealed class DevToolsProfilerSearchEntry : TextEntry
{
	internal sealed record Suggestion( long CaptureId, uint SiteId, string Name, string File, int Line, int Calls, double? SelfMs )
	{
		public override string ToString() => Name;
	}

	internal Action<Suggestion> Accepted;
	internal bool IsPreviewing => AutoCompletePanel?.SelectedChild.IsValid() == true;
	internal void CancelSuggestions() => AutoCompleteCancel();

	private protected override void AutoCompleteSelected( object value )
	{
		var canAccept = CanEdit;
		base.AutoCompleteSelected( value );
		if ( canAccept && value is Suggestion suggestion ) Accepted?.Invoke( suggestion );
	}
}
