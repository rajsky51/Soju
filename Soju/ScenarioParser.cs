using System.Text.Json;
using NBitcoin;
using Soju.Blockchain.Keys;

namespace Soju;

// TODO: satoshis -> int64
public record ScenarioFund(int Satoshis, int DelayRounds);
public record ScenarioWallet(List<ScenarioFund> Funds, int AnonScoreTarget, bool RedCoinIsolation, int StopRounds);
public record ScenarioBackend
(
	string? CoordinatorIdentifier,
	int?    MaxInputCountByRound,
	double? MinInputCountByRoundMultiplier,
	Money?  MinRegistrableAmount,
	Money?  MaxRegistrableAmount
);
public record ScenarioConfig(ScenarioBackend Backend, List<ScenarioWallet> Wallets, string Name, int Rounds);

public class ScenarioParser
{
	public List<string> Warnings;
	public List<string> Errors;
	
	public ScenarioParser()
	{
		Warnings = [];
		Errors = [];
	}
	
	public ScenarioConfig? Parse(string path)
	{
		
		if (!File.Exists(path))
		{
			Errors.Add($"Config file not found: {path}");
			return null;
		}
		
		string jsonText = File.ReadAllText(path);
		using JsonDocument doc = JsonDocument.Parse(jsonText);
		JsonElement root = doc.RootElement;
		
		string name = GetString(root, "name", "unknown", Requirement.OptionalWarning, "root");
		int blocks = GetInt(root, "blocks", 0, Requirement.OptionalWarning, "root");
		int rounds = GetInt(root, "rounds", 0, Requirement.OptionalWarning, "root");
		int defaultAnonScoreTarget = GetInt(root, "default_anon_score_target", KeyManager.DefaultAnonScoreTarget, Requirement.OptionalWarning, "root");
		bool defaultRedcoinIsolation = GetBool(root, "default_redcoin_isolation", KeyManager.DefaultRedCoinIsolation, Requirement.OptionalWarning, "root");
		
		ScenarioBackend? backend = new ScenarioBackend(null, null, null, null, null);
		if (root.TryGetProperty("backend", out JsonElement backendElement) && backendElement.ValueKind == JsonValueKind.Object) 
			backend = ParseBackend(backendElement);
		
		List<ScenarioWallet> wallets = []; 
		if (root.TryGetProperty("wallets", out JsonElement walletsElement) && walletsElement.ValueKind == JsonValueKind.Array) 
			wallets = ParseWallets(walletsElement, defaultAnonScoreTarget, defaultRedcoinIsolation);
		else 
			Errors.Add($"root: property 'wallets' not specified");
		
		return new ScenarioConfig(
			Backend: backend,
			Wallets: wallets,
			Name: name,
			Rounds: rounds > 0 ? rounds : blocks);
	}
	
	private ScenarioBackend ParseBackend(JsonElement element)
	{
		string? coordinatorIdentifier = GetString(element, nameof(ScenarioBackend.CoordinatorIdentifier), null, Requirement.OptionalNoWarning, "backend");
		int? maxInputCountByRound = GetIntNullable(element, nameof(ScenarioBackend.MaxInputCountByRound), Requirement.OptionalNoWarning, "backend");
		double? minInputCountByRoundMultiplier = GetDoubleNullable(element, nameof(ScenarioBackend.MinInputCountByRoundMultiplier), Requirement.OptionalNoWarning, "backend");
		// TODO: Maybe decimals?
		double? minRegistrableAmountD = GetDoubleNullable(element, nameof(ScenarioBackend.MinRegistrableAmount), Requirement.OptionalNoWarning, "backend");
		double? maxRegistrableAmountD = GetDoubleNullable(element, nameof(ScenarioBackend.MaxRegistrableAmount), Requirement.OptionalNoWarning, "backend");
		Money? minRegistrableAmount = minRegistrableAmountD is not null ? new Money(
			(decimal)minRegistrableAmountD, MoneyUnit.BTC) : null;
		Money? maxRegistrableAmount = maxRegistrableAmountD is not null ? new Money(
			(decimal)maxRegistrableAmountD, MoneyUnit.BTC) : null;
		
		return new ScenarioBackend(
			CoordinatorIdentifier: coordinatorIdentifier,
			MaxInputCountByRound: maxInputCountByRound,
			MinInputCountByRoundMultiplier: minInputCountByRoundMultiplier,
			MinRegistrableAmount: minRegistrableAmount,
			MaxRegistrableAmount: maxRegistrableAmount);
	}
	
	private List<ScenarioWallet> ParseWallets(JsonElement array, int defaultAnonScoreTarget, bool defaultRedcoinIsolation)
	{
		List<ScenarioWallet> wallets = [];
		
		int i = 0;
		foreach (JsonElement element in array.EnumerateArray())
		{
			string ctx = $"wallets[{i}]";
			if (element.ValueKind != JsonValueKind.Object)
			{
				Errors.Add($"{ctx}: expected object, got {element.ValueKind}");
				i++;
				continue;
			}
			int anonScoreTarget = GetInt(element, "anon_score_target", defaultAnonScoreTarget, Requirement.OptionalNoWarning, ctx);
			bool redcoinIsolation = GetBool(element, "redcoin_isolation", defaultRedcoinIsolation, Requirement.OptionalNoWarning, ctx);
			
			int stopBlocks = GetInt(element, "stop_blocks", 0, Requirement.OptionalNoWarning, ctx);
			int stopRounds = GetInt(element, "stop_rounds", 0, Requirement.OptionalNoWarning, ctx);
			int stop = stopRounds != 0 ? stopRounds : stopBlocks;
			
			int delayBlocks = GetInt(element, "delay_blocks", 0, Requirement.OptionalNoWarning, ctx);
			int delayRounds = GetInt(element, "delay_blocks", 0, Requirement.OptionalNoWarning, ctx);
			int delay = delayRounds != 0 ? delayRounds : delayBlocks;
			
			List<ScenarioFund> funds;
			if (element.TryGetProperty("funds", out JsonElement fundsElement) && fundsElement.ValueKind == JsonValueKind.Array) 
			{
				funds = ParseFunds(fundsElement, delay, ctx);
			} 
			else 
			{
				Errors.Add($"wallet[{i}]: property 'funds' not specified");
				i++;
				continue;
			}
			wallets.Add(new ScenarioWallet(funds, anonScoreTarget, redcoinIsolation, stop));
			i++;
		}
		return wallets;
	}
	
	private List<ScenarioFund> ParseFunds(JsonElement array, int defaultDelay, string ctx)
	{
		List<ScenarioFund> funds = [];
		
		int i = 0;
		foreach (JsonElement element in array.EnumerateArray())
		{
			if (element.ValueKind == JsonValueKind.Number) 
			{
				funds.Add(new ScenarioFund(element.GetInt32(), defaultDelay));
			}
			else if (element.ValueKind == JsonValueKind.Object)
			{
				int satoshis = GetInt(element, "value", 0, Requirement.Required, $"{ctx}.fund[{i}]");
				int? delayBlocks = GetIntNullable(element, "delay_blocks", Requirement.OptionalNoWarning, $"{ctx}.fund[{i}]");
				int? delayRounds = GetIntNullable(element, "delay_rounds", Requirement.OptionalNoWarning, $"{ctx}.fund[{i}]");
				
				int delay = defaultDelay;
				if (delayRounds is not null)      delay = (int)delayRounds;
				else if (delayBlocks is not null) delay = (int)delayBlocks;
				
				funds.Add(new ScenarioFund(satoshis, delay));
			} else {
				Errors.Add($"{ctx}.fund[{i}]: expected number or object, got {element.ValueKind}");
			}
			i++;
		}
		return funds;
	}
	
	private string GetString(JsonElement element, string name, string defaultValue, Requirement requirement, string ctx)
	{
		if (!element.TryGetProperty(name, out JsonElement value))
		{
			if (requirement == Requirement.Required)
				Errors.Add($"{ctx}: missing required property '{name}'");
			else if (requirement == Requirement.OptionalWarning)
				Warnings.Add($"{ctx}: missing property '{name}', defaulting to '{defaultValue}'");
			return defaultValue;
		}
		if (value.ValueKind != JsonValueKind.String) 
		{
			Errors.Add($"{ctx}: '{name}' expected string, got {value.ValueKind}");
			return defaultValue;
		}
		return value.GetString()!;
	}
	
	private int? GetIntNullable(JsonElement element, string name, Requirement requirement, string ctx)
	{
		if (!element.TryGetProperty(name, out JsonElement value))
		{
			if (requirement == Requirement.Required)
				Errors.Add($"{ctx}: missing required property '{name}'");
			else if (requirement == Requirement.OptionalWarning)
				Warnings.Add($"{ctx}: missing property '{name}', defaulting to null");
			return null;
		}
		if (!value.TryGetInt32(out int n)) 
		{
			Errors.Add($"{ctx}: '{name}' expected int, got {value.ValueKind}");
			return null;
		}
		return n;
	}
	
	private int GetInt(JsonElement element, string name, int defaultValue, Requirement requirement, string ctx)
	{
		int? n = GetIntNullable(element, name, Requirement.OptionalNoWarning, ctx);
		if (n is null) {
			n = defaultValue;
			if (requirement == Requirement.Required)
				Errors.Add($"{ctx}: missing required property '{name}'");
			else if (requirement == Requirement.OptionalWarning)
				Warnings.Add($"{ctx}: missing property '{name}', defaulting to {defaultValue}");
		}
		return (int)n;
	}
	
	private double? GetDoubleNullable(JsonElement element, string name, Requirement requirement, string ctx)
	{
		if (!element.TryGetProperty(name, out JsonElement value))
		{
			if (requirement == Requirement.Required)
				Errors.Add($"{ctx}: missing required property '{name}'");
			else if (requirement == Requirement.OptionalWarning)
				Warnings.Add($"{ctx}: missing property '{name}', defaulting to null");
			return null;
		}
		if (!value.TryGetDouble(out double n)) 
		{
			Errors.Add($"{ctx}: '{name}' expected double, got {value.ValueKind}");
			return null;
		}
		return n;
	}
	
	private bool GetBool(JsonElement element, string name, bool defaultValue, Requirement requirement, string ctx)
	{
		if (!element.TryGetProperty(name, out JsonElement value))
		{
			if (requirement == Requirement.Required)
				Errors.Add($"{ctx}: missing required property '{name}'");
			else if (requirement == Requirement.OptionalWarning)
				Warnings.Add($"{ctx}: missing property '{name}', defaulting to {defaultValue}");
			return defaultValue;
		}
		if (value.ValueKind == JsonValueKind.True) return true;
		if (value.ValueKind == JsonValueKind.False) return false;
		
		Errors.Add($"{ctx}: '{name}' expected bool, got {value.ValueKind}");
		return defaultValue;
	}
	
	private enum Requirement
	{
		Required,
		OptionalWarning,
		OptionalNoWarning,
	}
}