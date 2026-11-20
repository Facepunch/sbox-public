namespace Sandbox;

/// <summary>
/// Loads a workshop query one page at a time. Restarting it drops pages of the old query that are still on their way.
/// </summary>
public sealed class WorkshopPager<T>
{
	readonly Func<Storage.Query> _createQuery;
	readonly Func<Storage.QueryItem, T> _select;
	readonly Action _changed;

	Storage.QueryResult _last;
	int _generation;

	public List<T> Items { get; } = new();
	public bool Loading { get; private set; }

	/// <summary>
	/// Whether the first page has been asked for yet.
	/// </summary>
	public bool Started => _generation > 0;

	/// <param name="createQuery">Builds the query for the first page.</param>
	/// <param name="select">Turns a result into what <see cref="Items"/> holds. Return null to skip it.</param>
	/// <param name="changed">Called when <see cref="Items"/> or <see cref="Loading"/> change.</param>
	public WorkshopPager( Func<Storage.Query> createQuery, Func<Storage.QueryItem, T> select, Action changed )
	{
		_createQuery = createQuery;
		_select = select;
		_changed = changed;
	}

	public Task Restart()
	{
		_generation++;
		Items.Clear();
		_last = null;
		return Next();
	}

	/// <summary>
	/// The next page, one at a time.
	/// </summary>
	public void LoadMore()
	{
		if ( Loading ) return;
		_ = Next();
	}

	async Task Next()
	{
		var generation = _generation;
		Loading = true;

		Task<Storage.QueryResult> request;
		if ( _last is null )
		{
			_changed?.Invoke();
			request = _createQuery().Run();
		}
		else
		{
			request = _last.HasMoreResults() ? _last.GetNextResults() : null;
		}

		var result = request is null ? null : await request;
		if ( generation != _generation ) return;

		if ( result is not null )
		{
			_last = result;
			if ( result.Items is not null )
				Items.AddRange( result.Items.Select( _select ).Where( x => x is not null ) );
		}

		Loading = false;
		_changed?.Invoke();
	}
}
