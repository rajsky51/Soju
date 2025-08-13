using NBitcoin;
using System.Collections.Concurrent;
using System.Diagnostics;
using Soju.Blockchain.Analysis;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Helpers;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Client.CoinJoin;
using Soju.WabiSabi.Client.CoinJoin.Client;
using Soju.WabiSabi.Client.CoinJoin.Client.Decomposer;
using Soju.Wallets;

namespace Soju;

public class  Mixer : IMixer
{
    public RoundParameters RoundParams { get; }
    private readonly BlockchainAnalyzer _bcAnalyzer;

    public Mixer(RoundParameters roundParams)
    {
        RoundParams = roundParams;
        // TODO: Look more into why BlockchainAnalyzer isn't a static class.
        _bcAnalyzer = new BlockchainAnalyzer();
    }

    public CoinjoinResult CompleteMix(IEnumerable<IWallet> participatingWallets)
    {
    }
}