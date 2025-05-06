using NBitcoin.DataEncoders;
using System.Text.Json;
using System.Text.Json.Serialization;
using Soju.Blockchain.TransactionOutputs;
using Soju.Blockchain.Transactions;
using Soju.Wallets;

namespace Soju.Json;

public class DumbTransactionConverter : JsonConverter<DumbTransaction>
{
    private readonly HexEncoder _encoder = new();
    
    public Dictionary<WalletId, IWallet> Wallets;
    
    public DumbTransactionConverter(IEnumerable<IWallet> wallets)
    {
        Wallets = [];
        foreach (var wallet in wallets) 
        {
            Wallets.Add(wallet.WalletId, wallet);
        }
    }
    
    public override DumbTransaction Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotImplementedException("JSON deserialization for DumbTransaction is not implemented.");
    }
    
    public override void Write(
        Utf8JsonWriter writer,
        DumbTransaction tx,
        JsonSerializerOptions options)
    {
        // First we need to convert transaction inputs and outputs from dictionaries to lists
        (WalletId walletId, DumbCoin coin)[] sortedInputs = tx.Inputs
            .SelectMany(kvp => kvp.Value, (kvp, coin) => (kvp.Key, coin))
            .OrderByDescending(entry => entry.coin.Amount)
            .ToArray();
        
        (WalletId walletId, DumbCoin coin)[] sortedOutputs = tx.Outputs
            .SelectMany(pair => pair.Value, (pair, coin) => (pair.Key, coin))
            .OrderBy(x => x.coin.Index) // Outputs should already be sorted by their amount
            .ToArray();
        
        writer.WriteString("txid", tx.Id.ToString());
        
        writer.WriteStartObject("inputs");
        for (int i = 0; i < sortedInputs.Count(); i++)
        {
            DumbCoin coin = sortedInputs[i].coin;
            writer.WriteStartObject(i.ToString());
               writer.WriteString("address", _encoder.EncodeData(coin.KeyId));
               writer.WriteString("txid", coin.TransactionId.ToString());
               writer.WriteNumber("value", coin.Amount.Satoshi);
               bool foundWallet = Wallets.TryGetValue(coin.WalletId, out IWallet? wallet);
               writer.WriteString("wallet_name", foundWallet ? wallet!.WalletName : "unknown");
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        
        writer.WriteStartObject("outputs");
        foreach (var output in sortedOutputs)
        {
            DumbCoin coin = output.coin;
            writer.WriteStartObject(coin.Index.ToString());
                writer.WriteString("address", _encoder.EncodeData(coin.KeyId));
                writer.WriteNumber("value", coin.Amount.Satoshi);
                bool foundWallet = Wallets.TryGetValue(coin.WalletId, out IWallet? wallet);
                writer.WriteString("wallet_name", foundWallet ? wallet!.WalletName : "unknown");
                writer.WriteNumber("anon_score", coin.AnonymitySet);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}