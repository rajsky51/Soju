using NBitcoin;
using System.Collections.Generic;
using Soju.Blockchain.Keys;

namespace Soju.WabiSabi.Client;

public interface IDestinationProvider
{
    IEnumerable<ScriptType> SupportedScriptTypes { get; }

    IEnumerable<IDestination> GetNextDestinations(int count, bool preferTaproot);

    public void TrySetScriptStates(KeyState state, IEnumerable<Script> scripts);
}