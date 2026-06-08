using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface ICodeApi
	{
		/// <summary>
		/// Search published package source code. <paramref name="q"/> supports single words and
		/// "quoted phrases". Optional filters narrow to one package, type, code kind or publish year.
		/// Only open-source code from publicly listed packages is returned.
		/// </summary>
		[Get( "/code/search/1" )]
		Task<CodeSearchResult> Search( [Query] string q, [Query] int take = 30, [Query] int skip = 0,
			[Query] string ident = null, [Query] string type = null, [Query] string kind = null, [Query] int? year = null );
	}
}
