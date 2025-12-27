using NBitcoin;
using Newtonsoft.Json;
using System.Collections.Immutable;
using System.ComponentModel;
using Soju.Bases;
using Soju.Helpers;
using Soju.JsonConverters;
using Soju.JsonConverters.Bitcoin;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Backend;

[JsonObject(MemberSerialization.OptIn)]
public class WabiSabiConfig : ConfigBase
{
	public WabiSabiConfig() : base()
	{
	}

	public WabiSabiConfig(string filePath) : base(filePath)
	{
	}

	[DefaultValue(108)]
	[JsonProperty(PropertyName = "ConfirmationTarget", DefaultValueHandling = DefaultValueHandling.Populate)]
	public uint ConfirmationTarget { get; set; } = 108;
	
	[DefaultValueMoneyBtc("0.00005")]
	[JsonProperty(PropertyName = "MinRegistrableAmount", DefaultValueHandling = DefaultValueHandling.Populate)]
	[JsonConverter(typeof(MoneyBtcJsonConverter))]
	public Money MinRegistrableAmount { get; set; } = Money.Coins(0.00005m);
	
	/// <summary>
	/// The width of the range proofs are calculated from this, so don't choose stupid numbers.
	/// </summary>
	[DefaultValueMoneyBtc("43000")]
	[JsonProperty(PropertyName = "MaxRegistrableAmount", DefaultValueHandling = DefaultValueHandling.Populate)]
	[JsonConverter(typeof(MoneyBtcJsonConverter))]
	public Money MaxRegistrableAmount { get; set; } = Money.Coins(43_000m);
	
	[DefaultValue(100)]
	[JsonProperty(PropertyName = "MaxInputCountByRound", DefaultValueHandling = DefaultValueHandling.Populate)]
	public int MaxInputCountByRound { get; set; } = 100;
	
	[DefaultValue(0.5)]
	[JsonProperty(PropertyName = "MinInputCountByRoundMultiplier", DefaultValueHandling = DefaultValueHandling.Populate)]
	public double MinInputCountByRoundMultiplier { get; set; } = 0.5;
	
	public int MinInputCountByRound => Math.Max(1, (int)(MaxInputCountByRound * MinInputCountByRoundMultiplier));
	
	[DefaultValueCoordinationFeeRate(0.0)]
	[JsonProperty(PropertyName = "CoordinationFeeRate", DefaultValueHandling = DefaultValueHandling.Populate)]
	public CoordinationFeeRate CoordinationFeeRate { get; set; } = new(0.0m);
	
	[JsonProperty(PropertyName = "CoordinatorExtPubKey")]
	public ExtPubKey CoordinatorExtPubKey { get; private set; } = NBitcoinHelpers.BetterParseExtPubKey(Constants.WabiSabiFallBackCoordinatorExtPubKey);
	
	[DefaultValue(1)]
	[JsonProperty(PropertyName = "CoordinatorExtPubKeyCurrentDepth", DefaultValueHandling = DefaultValueHandling.Populate)]
	public int CoordinatorExtPubKeyCurrentDepth { get; private set; } = 1;
	
	[DefaultValueMoneyBtc("0.1")]
	[JsonProperty(PropertyName = "MaxSuggestedAmountBase", DefaultValueHandling = DefaultValueHandling.Populate)]
	[JsonConverter(typeof(MoneyBtcJsonConverter))]
	public Money MaxSuggestedAmountBase { get; set; } = Money.Coins(0.1m);
	
	[DefaultValue("CoinJoinCoordinatorIdentifier")]
	[JsonProperty(PropertyName = "CoordinatorIdentifier", DefaultValueHandling = DefaultValueHandling.Populate)]
	public string CoordinatorIdentifier { get; set; } = "CoinJoinCoordinatorIdentifier";
	
	[DefaultValue(true)]
	[JsonProperty(PropertyName = "AllowP2wpkhInputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2wpkhInputs { get; set; } = true;
	
	[DefaultValue(true)]
	[JsonProperty(PropertyName = "AllowP2trInputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2trInputs { get; set; } = true;
	
	[DefaultValue(true)]
	[JsonProperty(PropertyName = "AllowP2wpkhOutputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2wpkhOutputs { get; set; } = true;
	
	[DefaultValue(true)]
	[JsonProperty(PropertyName = "AllowP2trOutputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2trOutputs { get; set; } = true;
	
	[DefaultValue(false)]
	[JsonProperty(PropertyName = "AllowP2pkhOutputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2pkhOutputs { get; set; } = false;
	
	[DefaultValue(false)]
	[JsonProperty(PropertyName = "AllowP2shOutputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2shOutputs { get; set; } = false;
	
	[DefaultValue(false)]
	[JsonProperty(PropertyName = "AllowP2wshOutputs", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool AllowP2wshOutputs { get; set; } = false;
	
	[DefaultValue(true)]
	[JsonProperty(PropertyName = "IsCoordinationEnabled", DefaultValueHandling = DefaultValueHandling.Populate)]
	public bool IsCoordinationEnabled { get; set; } = true;

	public ImmutableSortedSet<ScriptType> AllowedInputTypes => GetScriptTypes(AllowP2wpkhInputs, AllowP2trInputs, false, false, false);
	
	public ImmutableSortedSet<ScriptType> AllowedOutputTypes => GetScriptTypes(AllowP2wpkhOutputs, AllowP2trOutputs, AllowP2pkhOutputs, AllowP2shOutputs, AllowP2wshOutputs);
	
	public Script GetNextCleanCoordinatorScript() => DeriveCoordinatorScript(CoordinatorExtPubKeyCurrentDepth);
	
	public Script DeriveCoordinatorScript(int index) => CoordinatorExtPubKey.Derive(0, false).Derive(index, false).PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit);
	
	public void MakeNextCoordinatorScriptDirty()
	{
		CoordinatorExtPubKeyCurrentDepth++;
		if (!string.IsNullOrWhiteSpace(FilePath))
		{
			ToFile();
		}
	}

	private static ImmutableSortedSet<ScriptType> GetScriptTypes(bool p2wpkh, bool p2tr, bool p2pkh, bool p2sh, bool p2wsh)
	{
		var scriptTypes = new List<ScriptType>();
		if (p2wpkh)
		{
			scriptTypes.Add(ScriptType.P2WPKH);
		}
		if (p2tr)
		{
			scriptTypes.Add(ScriptType.Taproot);
		}
		if (p2pkh)
		{
			scriptTypes.Add(ScriptType.P2PKH);
		}
		if (p2sh)
		{
			scriptTypes.Add(ScriptType.P2SH);
		}
		if (p2wsh)
		{
			scriptTypes.Add(ScriptType.P2WSH);
		}
	
		// When adding new script types, please see
		// https://github.com/WalletWasabi/WalletWasabi/issues/5440
	
		return scriptTypes.ToImmutableSortedSet();
	}
}
