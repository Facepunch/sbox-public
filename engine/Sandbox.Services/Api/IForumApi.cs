using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	/// <summary>
	/// Read the forums. Everything is paged; leave the sort null for the site's default order.
	/// Hidden forums, hidden threads and the support forum only come back for admins.
	/// </summary>
	public interface IForumApi
	{
		[Get( "/forum" )]
		Task<BasePagedResponse<ForumDto>> GetForums( ForumSort? sort = null, int page = 1, int perPage = 50 );

		/// <summary>
		/// Threads in one forum by its url slug. 404 if the forum doesn't exist or isn't visible.
		/// </summary>
		[Get( "/forum/{forum}/threads" )]
		Task<BasePagedResponse<ForumThreadDto>> GetThreads( string forum, ThreadSort? sort = null, ThreadFilter? filter = null, int page = 1, int perPage = 30 );

		/// <summary>
		/// Posts in one thread. 404 if the thread doesn't exist or isn't visible.
		/// </summary>
		[Get( "/forum/thread/{thread}/posts" )]
		Task<BasePagedResponse<ForumPostDto>> GetPosts( long thread, PostSort? sort = null, int page = 1, int perPage = 30 );
	}
}
