using NBitcoin;

namespace Soju.WabiSabi.Backend.Statistics;

public class CoinJoinScriptStore
{
	public CoinJoinScriptStore()
		: this([])
	{
	}

	public CoinJoinScriptStore(IEnumerable<Script> scripts)
	{
		_scripts = new HashSet<Script>(scripts);
	}

	private readonly HashSet<Script> _scripts = new();

	public void AddRange(IEnumerable<Script> scripts)
	{
		foreach (var script in scripts)
		{
			_scripts.Add(script);
		}
	}

	public bool Contains(Script script)
	{
		return _scripts.Contains(script);
	}
}