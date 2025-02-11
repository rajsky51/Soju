using NBitcoin;
using NBitcoin.DataEncoders;
using System.Text;
using Soju;

public class JSONBuilder {
    private static string EncodeBase32(byte[] bytes) => Encoders.Base32.EncodeData(bytes);
    private static string EncodeBase32(uint256 value) => EncodeBase32(value.ToBytes());

    public string Indent;

    public Dictionary<WalletId, Wallet> Wallets;

    public JSONBuilder(IEnumerable<Wallet> wallets, string indent) 
    {
        Wallets = [];
        foreach (var wallet in wallets) 
        {
            Wallets.Add(wallet.WalletId, wallet);
        }

        Indent = indent;
    }

    public string CoinjoinResultsToJSON(IEnumerable<CoinjoinResult> cjResults, int indentCount) 
    {
        StringBuilder jsonBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        jsonBuilder.Append(indentation + "\"coinjoins\": {\n");

        foreach (var result in cjResults) 
        {
            jsonBuilder.Append(CoinjoinToJSON(result, indentCount + 1));
            jsonBuilder.Append(",\n");
        }
        jsonBuilder.Remove(jsonBuilder.Length - 2, 1); // Trimming the last comma

        jsonBuilder.Append(indentation + "}");

        return jsonBuilder.ToString();
    }

    public string CoinjoinToJSON(CoinjoinResult cj, int indentCount) 
    {
        StringBuilder cjBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        cjBuilder.Append(indentation + $"\"{EncodeBase32(cj.RoundId)}\": " + "{\n");

        indentation += Indent;
        var tx = cj.Transaction;

        cjBuilder.Append(indentation + $"\"txid\": \"{tx.Id}\",\n");

        cjBuilder.Append(InputsToJSON(tx.Inputs, indentCount + 1));
        cjBuilder.Append(",\n");

        cjBuilder.Append(OutputsToJSON(tx.Outputs, indentCount + 1));
        cjBuilder.Append(",\n");

        cjBuilder.Append(indentation + $"\"round_id\": {EncodeBase32(cj.RoundId)}\n");
        
        indentation = indentation[..^Indent.Length]; // Trimming indentation back

        cjBuilder.Append(indentation + "}");

        return cjBuilder.ToString();
    }

    public string TransactionToJSON(DumbTransaction tx, int indentCount) 
    {
        StringBuilder txBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        txBuilder.Append(indentation + $"\"txid\": \"{tx.Id}\",\n");

        txBuilder.Append(InputsToJSON(tx.Inputs, indentCount));
        txBuilder.Append(",\n");

        txBuilder.Append(OutputsToJSON(tx.Outputs, indentCount));
        txBuilder.Append("\n");

        return txBuilder.ToString();
    }

    public string InputsToJSON(IDictionary<WalletId, HashSet<DumbCoin>> inputs, int indentCount) {
        StringBuilder inputsBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        inputsBuilder.Append(indentation + "\"inputs\": {\n");

        var inputsWithWalletIds = inputs.SelectMany(pair => pair.Value, (pair, coin) => (pair.Key, coin))
                                        .OrderBy(x => x.coin.Index)
                                        .ToList();

        // The inputs are stored in sets in a dictionary. We need to convert it 
        // to an array and sort it by their amounts descending.
        var sortedInputs = inputs
            .SelectMany(kvp => kvp.Value, (kvp, coin) => (kvp.Key, coin))
            .OrderByDescending(entry => entry.coin.Amount)
            .ToArray();

        for (int i = 0; i < sortedInputs.Length; i++)
        {
            var input = sortedInputs[i];
            string retString = InputToJSON(input.coin, Wallets.TryGet(input.Key), i, indentCount + 1);
            inputsBuilder.Append(retString);
            inputsBuilder.Append(",\n");
        }
        if (sortedInputs.Length > 0) 
        {
            // There actually were inputs, so we need to trim the last comma
            inputsBuilder.Remove(inputsBuilder.Length - 2, 1);
        }

        inputsBuilder.Append(indentation + "}");

        return inputsBuilder.ToString();
    }

    public string InputToJSON(DumbCoin input, Wallet wallet, int index, int indentCount)
    {
        StringBuilder inputBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        inputBuilder.Append(indentation);
        inputBuilder.Append($"\"{index}\": " + "{\n");

        indentation += Indent;

        inputBuilder.Append(indentation + $"\"address\": \"{EncodeBase32(input.KeyId)}\",\n");
        inputBuilder.Append(indentation + $"\"txid\": \"{EncodeBase32(input.TransactionId)}\",\n");
        inputBuilder.Append(indentation + $"\"value\": {input.Amount.Satoshi},\n");
        inputBuilder.Append(indentation + $"\"wallet_name\": \"{wallet.WalletName}\",\n");

        indentation = indentation[..^Indent.Length]; // Trimming indentation back

        inputBuilder.Append(indentation + "}");

        return inputBuilder.ToString();
    }

    public string OutputsToJSON(IDictionary<WalletId, HashSet<DumbCoin>> outputs, int indentCount) {
        StringBuilder outputsBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        outputsBuilder.Append(indentation + "\"outputs\": {\n");

        var outputsWithWalletIds = outputs.SelectMany(pair => pair.Value, (pair, coin) => (pair.Key, coin))
                                         .OrderBy(x => x.coin.Index)
                                         .ToList();

        foreach (var output in outputsWithWalletIds)
        {
            string retString = OutputToJSON(output.coin, Wallets.TryGet(output.Key), indentCount + 1);
            outputsBuilder.Append(retString);
            outputsBuilder.Append(",\n");
        }
        if (outputs.Count > 0) 
        {
            // There actually were outputs so we need to trim the last comma
            outputsBuilder.Remove(outputsBuilder.Length - 2, 1);
        }

        outputsBuilder.Append(indentation + "}");

        return outputsBuilder.ToString();
    }

    public string OutputToJSON(DumbCoin output, Wallet wallet, int indentCount)
    {
        StringBuilder outputBuilder = new();
        string indentation = string.Concat(Enumerable.Repeat(Indent, indentCount));

        outputBuilder.Append(indentation);
        outputBuilder.Append($"\"{output.Index}\": " + "{\n");

        indentation += Indent;

        outputBuilder.Append(indentation + $"\"address\": \"{EncodeBase32(output.KeyId)}\",\n");
        outputBuilder.Append(indentation + $"\"value\": {output.Amount.Satoshi},\n");
        outputBuilder.Append(indentation + $"\"wallet_name\": \"{wallet.WalletName}\",\n");
        outputBuilder.Append(indentation + $"\"anon_score\": \"{output.AnonymitySet}\",\n");

        indentation = indentation[..^Indent.Length]; // Trimming indentation back

        outputBuilder.Append(indentation + "}");

        return outputBuilder.ToString();
    }
}