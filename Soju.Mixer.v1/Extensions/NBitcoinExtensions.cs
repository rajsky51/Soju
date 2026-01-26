using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Soju.Blockchain.TransactionOutputs;
using Soju.Helpers;
using Soju.WabiSabi.Models;
using Soju.Crypto;

namespace Soju.Extensions;

public static class NBitcoinExtensions
{
	public static string ToHex(this IBitcoinSerializable me)
	{
		return ByteHelpers.ToHex(me.ToBytes());
	}

	public static void FromHex(this IBitcoinSerializable me, string hex)
	{
		Guard.NotNullOrEmptyOrWhitespace(nameof(hex), hex);
		me.FromBytes(ByteHelpers.FromHex(hex));
	}

/// <summary>
/// Based on transaction data, it decides if it's possible that native segwit script played a par in this transaction.
/// </summary>
	public static bool SegWitInvolved(this Transaction me) =>
		me.Inputs.Any(i => Script.IsNullOrEmpty(i.ScriptSig)) ||
		me.Outputs.Any(o => o.ScriptPubKey.IsScriptType(ScriptType.Witness));

	public static IEnumerable<(Money value, int count)> GetIndistinguishableOutputs(this Transaction me, bool includeSingle)
	{
		return me.Outputs.GroupBy(x => x.Value)
			.ToDictionary(x => x.Key, y => y.Count())
			.Select(x => (x.Key, x.Value))
			.Where(x => includeSingle || x.Value > 1);
	}

	public static int EstimateOutputVsize(this Script scriptPubKey) =>
		new TxOut(Money.Zero, scriptPubKey).GetSerializedSize();

	public static int EstimateInputVsize(this Script scriptPubKey) =>
		scriptPubKey.GetScriptType().EstimateInputVsize();

	public static int EstimateInputVsize(this ScriptType scriptType) =>
		scriptType switch
		{
			ScriptType.P2WPKH => Constants.P2wpkhInputVirtualSize,
			ScriptType.Taproot => Constants.P2trInputVirtualSize,
			ScriptType.P2PKH => Constants.P2pkhInputVirtualSize,
			ScriptType.P2SH => Constants.P2shInputVirtualSize,
			ScriptType.P2WSH => Constants.P2wshInputVirtualSize,
			_ => throw new NotImplementedException($"Size estimation isn't implemented for provided script type.")
		};

	public static int EstimateOutputVsize(this ScriptType scriptType) =>
		scriptType switch
		{
			ScriptType.P2WPKH => Constants.P2wpkhOutputVirtualSize,
			ScriptType.Taproot => Constants.P2trOutputVirtualSize,
			ScriptType.P2PKH => Constants.P2pkhOutputVirtualSize,
			ScriptType.P2SH => Constants.P2shOutputVirtualSize,
			ScriptType.P2WSH => Constants.P2wshOutputVirtualSize,
			_ => throw new NotImplementedException($"Size estimation isn't implemented for provided script type.")
		};

	public static Money EffectiveCost(this TxOut output, FeeRate feeRate) =>
		output.Value + feeRate.GetFee(output.ScriptPubKey.EstimateOutputVsize());

	public static Money EffectiveValue(this ICoin coin, FeeRate feeRate)
		=> EffectiveValue(coin.TxOut.Value, virtualSize: coin.TxOut.ScriptPubKey.EstimateInputVsize(), feeRate);

	public static Money EffectiveValue(this ISmartCoin coin, FeeRate feeRate)
		=> EffectiveValue(coin.Amount, virtualSize: coin.ScriptType.EstimateInputVsize(), feeRate);

	public static Money EffectiveValue(Money amount, int virtualSize, FeeRate feeRate)
	{
		var networkFee = feeRate.GetFee(virtualSize);
		return amount - networkFee;
	}
	
	// NOTE: Previous versions of the Wallet need EffectiveValue calculations with CoordinationFeeRate
	public static Money EffectiveValue(this ICoin coin, FeeRate feeRate, CoordinationFeeRate coordinationFeeRate)
		=> EffectiveValue(coin.TxOut.Value, virtualSize: coin.TxOut.ScriptPubKey.EstimateInputVsize(), feeRate, coordinationFeeRate);

	public static Money EffectiveValue(this ISmartCoin coin, FeeRate feeRate, CoordinationFeeRate coordinationFeeRate)
		=> EffectiveValue(coin.Amount, virtualSize: coin.ScriptType.EstimateInputVsize(), feeRate, coordinationFeeRate);

	private static Money EffectiveValue(Money amount, int virtualSize, FeeRate feeRate, CoordinationFeeRate coordinationFeeRate)
	{
		var networkFee = feeRate.GetFee(virtualSize);
		var coordinationFee = coordinationFeeRate.GetFee(amount);

		return amount - networkFee - coordinationFee;
	}

	public static T FromBytes<T>(byte[] input) where T : IBitcoinSerializable, new()
	{
		BitcoinStream inputStream = new(input);
		var instance = new T();
		inputStream.ReadWrite(instance);
		if (inputStream.Inner.Length != inputStream.Inner.Position)
		{
			throw new FormatException("Expected end of stream");
		}

		return instance;
	}

	/// <summary>
	/// Extracts a unique public key identifier. If it can't do that, then it returns the scriptPubKey byte array.
	/// </summary>
	public static byte[] ExtractKeyId(this Script scriptPubKey)
	{
		return scriptPubKey.TryGetScriptType() switch
		{
			ScriptType.P2WPKH => PayToWitPubKeyHashTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey)!.ToBytes(),
			ScriptType.P2PKH => PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey)!.ToBytes(),
			ScriptType.P2PK => PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey)!.ToBytes(),
			_ => scriptPubKey.ToBytes()
		};
	}

	public static ScriptType GetScriptType(this Script script)
	{
		return TryGetScriptType(script) ?? throw new NotImplementedException($"Unsupported script type.");
	}

	public static ScriptType? TryGetScriptType(this Script script)
	{
		foreach (ScriptType scriptType in new ScriptType[] { ScriptType.P2WPKH, ScriptType.P2PKH, ScriptType.P2PK, ScriptType.Taproot })
		{
			if (script.IsScriptType(scriptType))
			{
				return scriptType;
			}
		}

		return null;
	}

	public static BitcoinSecret GetBitcoinSecret(this ExtKey hdKey, Network network, Script scriptPubKey)
		=> GetBitcoinSecret(network, hdKey.PrivateKey, scriptPubKey);

	public static BitcoinSecret GetBitcoinSecret(Network network, Key privateKey, Script scriptPubKey)
	{
		var derivedScriptPubKeyType = scriptPubKey switch
		{
			_ when scriptPubKey.IsScriptType(ScriptType.P2WPKH) => ScriptPubKeyType.Segwit,
			_ when scriptPubKey.IsScriptType(ScriptType.Taproot) => ScriptPubKeyType.TaprootBIP86,
			_ => throw new NotSupportedException("Not supported script type.")
		};

		if (privateKey.PubKey.GetScriptPubKey(derivedScriptPubKeyType) != scriptPubKey)
		{
			throw new InvalidOperationException("The key cannot generate the utxo scriptPubKey. This could happen if the wallet password is not the correct one.");
		}

		return privateKey.GetBitcoinSecret(network);
	}

	public static OwnershipProof GetOwnershipProof(Key masterKey, BitcoinSecret secret, Script scriptPubKey, CoinJoinInputCommitmentData commitmentData)
	{
		var identificationMasterKey = Slip21Node.FromSeed(masterKey.ToBytes());
		var identificationKey = identificationMasterKey.DeriveChild("SLIP-0019")
			.DeriveChild("Ownership identification key").Key;

		var signingKey = secret.PrivateKey;
		var ownershipProof = OwnershipProof.GenerateCoinJoinInputProof(
			signingKey,
			new OwnershipIdentifier(identificationKey, scriptPubKey),
			commitmentData,
			scriptPubKey.IsScriptType(ScriptType.P2WPKH)
				? ScriptPubKeyType.Segwit
				: ScriptPubKeyType.TaprootBIP86);

		return ownershipProof;
	}
}