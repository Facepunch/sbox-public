using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface ICodeApi
	{
		/// <summary>
		/// Search published package source code. Plain words in <paramref name="q"/> match as
		/// words; a query wrapped in double quotes, or containing any symbol, matches that exact
		/// code sequence - e.g. <c>.DrawSomething(</c> finds calls, not just the word. Optional
		/// filters narrow to one package, type, code kind or publish year. Only open-source code
		/// from publicly listed packages is returned.
		/// </summary>
		[Get( "/code/search/1" )]
		Task<CodeSearchResult> Search( [Query] string q, [Query] int take = 30, [Query] int skip = 0,
			[Query] string ident = null, [Query] string type = null, [Query] string kind = null, [Query] int? year = null );
	}
}
