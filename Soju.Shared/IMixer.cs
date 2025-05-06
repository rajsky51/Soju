using Soju.WabiSabi.Client.CoinJoin;
using Soju.Wallets;

namespace Soju;

public interface IMixer
{
    CoinjoinResult CompleteMix(IEnumerable<IWallet> wallets);
}