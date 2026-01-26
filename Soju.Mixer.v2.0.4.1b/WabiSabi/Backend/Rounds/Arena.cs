using NBitcoin;
using NBitcoin.RPC;
using System.Diagnostics;
using Soju.BitcoinCore.Rpc;
using Soju.Crypto.Randomness;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;
using Soju.WabiSabi.Backend.Rounds.CoinJoinStorage;
using Soju.WabiSabi.Backend.Statistics;
using System.Collections.Immutable;
using Soju.WabiSabi.Models;
using Soju.Extensions;
using Soju.Logging;

namespace Soju.WabiSabi.Backend.Rounds;

public partial class Arena
{
	public Arena(
		WabiSabiConfig config,
		IRPCClient rpc,
		ICoinJoinIdStore coinJoinIdStore,
		RoundParameterFactory roundParameterFactory,
		CoinJoinScriptStore? coinJoinScriptStore = null)
	{
		Config = config;
		Rpc = rpc;
		CoinJoinIdStore = coinJoinIdStore;
		CoinJoinScriptStore = coinJoinScriptStore;
		RoundParameterFactory = roundParameterFactory;
		MaxSuggestedAmountProvider = new(Config);
	}

	public event EventHandler<Transaction>? CoinJoinBroadcast;

	public HashSet<Round> Rounds { get; } = new();
	private IEnumerable<RoundState> RoundStates { get; set; } = Enumerable.Empty<RoundState>();
	private WabiSabiConfig Config { get; }
	internal IRPCClient Rpc { get; }
	public CoinJoinScriptStore? CoinJoinScriptStore { get; }
	private ICoinJoinIdStore CoinJoinIdStore { get; set; }
	private RoundParameterFactory RoundParameterFactory { get; }
	public MaxSuggestedAmountProvider MaxSuggestedAmountProvider { get; }
	
	// NOTE: My addition
	public IEnumerable<RoundState> GetRoundStates()
	{
		return RoundStates;
	}

	public void SetRoundStates()
	{
		// Order rounds ascending by max suggested amount, then ascending by input count.
		// This will make sure WW2.0.1 clients register according to our desired order.
		var rounds = Rounds
						.OrderBy(x => x.Parameters.MaxSuggestedAmount)
						.ThenBy(x => x.InputCount)
						.ToList();

		RoundStates = rounds.Select(r => RoundState.FromRound(r, stateId: 0));
	}

	public void StepInputRegistrationPhase()
	{
		Round[] inputRegRounds = Rounds.Where(x => x.Phase == Phase.InputRegistration).ToArray();
		Debug.Assert(inputRegRounds.Length == 1);
		foreach (var round in inputRegRounds)
		{
			try
			{
				List<Alice> offendingAlices = CheckTxoSpendStatus(round);
				if (offendingAlices.Any())
				{
					round.Alices.RemoveAll(x => offendingAlices.Contains(x));
				}
				if (round.InputCount < round.Parameters.MinInputCountByRound)
				{
					MaxSuggestedAmountProvider.StepMaxSuggested(round, false);
					EndRound(round, EndRoundState.AbortedNotEnoughAlices);
					round.LogInfo($"Not enough inputs ({round.InputCount}) in {nameof(Phase.InputRegistration)} phase. The minimum is ({round.Parameters.MinInputCountByRound}). {nameof(round.Parameters.MaxSuggestedAmount)} was '{round.Parameters.MaxSuggestedAmount}' BTC.");
				}
				else
				{
					MaxSuggestedAmountProvider.StepMaxSuggested(round, true);
					SetRoundPhase(round, Phase.ConnectionConfirmation);
				}
			}
			catch (Exception ex)
			{
				EndRound(round, EndRoundState.AbortedWithError);
				round.LogError(ex.Message);
			}
		}
	}

	public void StepConnectionConfirmationPhase()
	{
		Round[] ccRounds = Rounds.Where(x => x.Phase == Phase.ConnectionConfirmation).ToArray();
		Debug.Assert(ccRounds.Length == 1);
		foreach (var round in ccRounds)
		{
			try
			{
				if (round.Alices.All(x => x.ConfirmedConnection))
				{
					SetRoundPhase(round, Phase.OutputRegistration);
				}
				else 
				{
					Debug.Assert(false, "We don't handle unconfirmed alices");
				}
			}
			catch (Exception ex)
			{
				EndRound(round, EndRoundState.AbortedWithError);
				round.LogError(ex.Message);
			}
		}
	}

	public void StepOutputRegistrationPhase()
	{
		Round[] outputRegRounds = Rounds.Where(x => x.Phase == Phase.OutputRegistration).ToArray();
		Debug.Assert(outputRegRounds.Length == 1);
		foreach (var round in outputRegRounds)
		{
			try
			{
				// TTODO: Mixer.v1
				// var allReady = round.Alices.All(a => a.ReadyToSign);
				var allReady = true;
				if (allReady)
				{
					var coinjoin = round.Assert<ConstructionState>();

					round.LogInfo($"{coinjoin.Inputs.Count()} inputs were added.");
					round.LogInfo($"{coinjoin.Outputs.Count()} outputs were added.");

					round.CoordinatorScript = GetCoordinatorScriptPreventReuse(round);
					coinjoin = AddCoordinationFee(round, coinjoin, round.CoordinatorScript);
					
					FeeRate highestFeeRate = Rpc.EstimateConservativeSmartFee(2).FeeRate;

					coinjoin = TryAddBlameScript(round, coinjoin, allReady, round.CoordinatorScript, highestFeeRate);
					
					round.CoinjoinState = FinalizeTransaction(round.Id, coinjoin);
					
					SetRoundPhase(round, Phase.TransactionSigning);
				}
				else 
				{
					// NOTE: Contrary to the original code we are ending the round here.
					// In the original they would go to FastSigningPhase
					EndRound(round, EndRoundState.AbortedWithError);
				}
			}
			catch (Exception ex)
			{
				EndRound(round, EndRoundState.AbortedWithError);
				round.LogError(ex.Message);
			}
		}
	}

	// NOTE: Returns the coinjoin transaction's id, if everything goes well
	public uint256 StepTransactionSigningPhase()
	{
		Round[] txSigningRounds = Rounds.Where(x => x.Phase == Phase.TransactionSigning).ToArray();
		Debug.Assert(txSigningRounds.Length == 1);
		
		Round round = txSigningRounds[0];
		var state = round.Assert<SigningState>();
		
		try
		{
			// TTODO: Mixer.v1
			Debug.Assert(state.IsFullySigned);
			if (state.IsFullySigned)
			{
				Transaction coinjoin = state.CreateTransaction();

				// Logging.
				round.LogInfo("Trying to broadcast coinjoin.");
				Coin[] spentCoins = round.CoinjoinState.Inputs.ToArray();
				Money networkFee = coinjoin.GetFee(spentCoins);
				round.LogInfo($"Network Fee: {networkFee.ToString(false, false)} BTC.");
				uint256 roundId = round.Id;
				FeeRate feeRate = coinjoin.GetFeeRate(spentCoins);
				round.LogInfo($"Network Fee Rate: {feeRate.SatoshiPerByte} sat/vByte.");
				round.LogInfo($"Desired Fee Rate: {round.Parameters.MiningFeeRate.SatoshiPerByte} sat/vByte.");

				// Added for monitoring reasons.
				try
				{
					FeeRate targetFeeRate = Rpc.EstimateConservativeSmartFee((int)Config.ConfirmationTarget).FeeRate;
					round.LogInfo($"Current Fee Rate on the Network: {targetFeeRate.SatoshiPerByte} sat/vByte. Confirmation target is: {(int)Config.ConfirmationTarget} blocks.");
				}
				catch (Exception ex)
				{
					Logger.LogDebug($"Could not log fee rate monitoring: '{ex.Message}'.");
				}

				round.LogInfo($"Number of inputs: {coinjoin.Inputs.Count}.");
				round.LogInfo($"Number of outputs: {coinjoin.Outputs.Count}.");
				round.LogInfo($"Serialized Size: {coinjoin.GetSerializedSize() / 1024} KB.");
				round.LogInfo($"VSize: {coinjoin.GetVirtualSize() / 1024} KB.");
				var indistinguishableOutputs = coinjoin.GetIndistinguishableOutputs(includeSingle: true);
				foreach (var (value, count) in indistinguishableOutputs.Where(x => x.count > 1))
				{
					round.LogInfo($"There are {count} occurrences of {value.ToString(true, false)} outputs.");
				}

				round.LogInfo(
					$"There are {indistinguishableOutputs.Count(x => x.count == 1)} occurrences of unique outputs.");

				// Broadcasting.
				Rpc.SendRawTransaction(coinjoin);

				var coordinatorScriptPubKey = Config.GetNextCleanCoordinatorScript();
				if (round.CoordinatorScript == coordinatorScriptPubKey)
				{
					Config.MakeNextCoordinatorScriptDirty();
				}

				foreach (var address in coinjoin.Outputs
					.Select(x => x.ScriptPubKey)
					.Where(script => CoinJoinScriptStore?.Contains(script) is true))
				{
					if (address == round.CoordinatorScript)
					{
						round.LogError(
							$"Coordinator script pub key reuse detected: {round.CoordinatorScript.ToHex()}");
					}
					else
					{
						round.LogError($"Output script pub key reuse detected: {address.ToHex()}");
					}
				}

				EndRound(round, EndRoundState.TransactionBroadcasted);
				round.LogInfo($"Successfully broadcast the coinjoin: {coinjoin.GetHash()}.");

				CoinJoinScriptStore?.AddRange(coinjoin.Outputs.Select(x => x.ScriptPubKey));
				CoinJoinBroadcast?.Invoke(this, coinjoin);
				
				return coinjoin.GetHash();
			}
			else
			{
				// TTODO: Mixer.v1
				Debug.Assert(false);
			}
		}
		catch (RPCException ex)
		{
			round.LogWarning($"Transaction broadcasting failed: '{ex}'.");
			EndRound(round, EndRoundState.TransactionBroadcastFailed);
		}
		catch (Exception ex)
		{
			round.LogWarning($"Signing phase failed, reason: '{ex}'.");
			EndRound(round, EndRoundState.AbortedWithError);
		}
		return uint256.Zero;
	}

	private List<Alice> CheckTxoSpendStatus(Round round)
	{
		List<Alice> alices = [];
		foreach (Alice alice in round.Alices)
		{
			OutPoint aliceOutpoint = alice.Coin.Outpoint;
			if (Rpc.GetTxOut(aliceOutpoint.Hash, (int)aliceOutpoint.N) is null)
			{
				alices.Add(alice);
			}
		}
		return alices;
	}

	public void CreateRound()
	{
		Debug.Assert(Rounds.Count == 0);
		
		FeeRate feeRate = Rpc.EstimateConservativeSmartFee((int)Config.ConfirmationTarget).FeeRate;
		RoundParameters parameters = RoundParameterFactory.CreateRoundParameter(feeRate, MaxSuggestedAmountProvider.MaxSuggestedAmount);

		Round r = new Round(parameters, SecureRandom.Instance);
		AddRound(r);
		r.LogInfo($"Created round with parameters: {nameof(r.Parameters.MaxSuggestedAmount)}:'{r.Parameters.MaxSuggestedAmount}' BTC.");
	}

	public void TimeoutRounds()
	{
		Round[] expiredRounds = Rounds.Where(x =>x.Phase == Phase.Ended).ToArray();
		foreach (var expiredRound in expiredRounds)
		{
			Rounds.Remove(expiredRound);
		}
	}

	internal static ConstructionState TryAddBlameScript(Round round, ConstructionState coinjoin, bool allReady, Script blameScript, FeeRate highestFeeRate)
	{
		// SharedOverhead calculated into EstimatedVsize.
		var sizeToPayFor = coinjoin.EstimatedVsize + blameScript.EstimateOutputVsize();
		var miningFee = sizeToPayFor == 0
			? Money.Zero
			: round.Parameters.MiningFeeRate.GetFee(sizeToPayFor);

		// Subtract 1 sat to avoid off-by-one error coming from roundings.
		var diffMoney = coinjoin.Balance - miningFee - Money.Satoshis(1);

		if (diffMoney > round.Parameters.AllowedOutputAmounts.Min)
		{
			// ToDo: This condition could be more sophisticated by always trying to max out the miner fees to target 2 and only deal with the remaining diffMoney.
			if (coinjoin.EffectiveFeeRate > highestFeeRate)
			{
				coinjoin = coinjoin.AddOutput(new TxOut(diffMoney, blameScript)).AsPayingForSharedOverhead();

				if (allReady)
				{
					round.LogInfo($"Filled up the outputs to build a reasonable transaction, all Alices signaled ready. Added amount: '{diffMoney}'.");
				}
				else
				{
					round.LogWarning($"Filled up the outputs to build a reasonable transaction because some alice failed to provide its output. Added amount: '{diffMoney}'.");
				}
			}
			else
			{
				if (allReady)
				{
					round.LogInfo($"There were some leftover satoshis. Added amount to miner fees: '{diffMoney}'.");
				}
				else
				{
					round.LogWarning($"Some alices failed to signal ready. There were some leftover satoshis. Added amount to miner fees: '{diffMoney}'.");
				}
			}
		}
		else if (!allReady)
		{
			round.LogWarning($"Could not add blame script, because the amount was too small: {diffMoney}.");
		}

		return coinjoin;
	}

	private ConstructionState AddCoordinationFee(Round round, ConstructionState coinjoin, Script coordinatorScriptPubKey)
	{
		var coordinationFee = round.Alices.Where(a => !a.IsCoordinationFeeExempted).Sum(x => round.Parameters.CoordinationFeeRate.GetFee(x.Coin.Amount));
		if (coordinationFee == 0)
		{
			round.LogInfo($"Coordination fee wasn't taken, because it was free for everyone. Hurray!");
		}
		else
		{
			var effectiveCoordinationFee = coordinationFee - round.Parameters.MiningFeeRate.GetFee(coordinatorScriptPubKey.EstimateOutputVsize() + coinjoin.UnpaidSharedOverhead);

			if (effectiveCoordinationFee > round.Parameters.AllowedOutputAmounts.Min)
			{
				coinjoin = coinjoin.AddOutput(new TxOut(effectiveCoordinationFee, coordinatorScriptPubKey)).AsPayingForSharedOverhead();
			}
			else
			{
				round.LogWarning($"Effective coordination fee wasn't taken, because it was too small: {effectiveCoordinationFee}.");
			}
		}

		return coinjoin;
	}

	private Script GetCoordinatorScriptPreventReuse(Round round)
	{
		var coordinatorScriptPubKey = Config.GetNextCleanCoordinatorScript();

		// Prevent coordinator script reuse.
		if (Rounds.Any(r => r.CoordinatorScript == coordinatorScriptPubKey))
		{
			Config.MakeNextCoordinatorScriptDirty();
			coordinatorScriptPubKey = Config.GetNextCleanCoordinatorScript();
			round.LogWarning("Coordinator script pub key was already used by another round, making it dirty and taking a new one.");
		}

		return coordinatorScriptPubKey;
	}

	private void AddRound(Round round)
	{
		Rounds.Add(round);
	}

	private void SetRoundPhase(Round round, Phase phase)
	{
		round.SetPhase(phase);
	}

	private void EndRound(Round round, EndRoundState endRoundState)
	{
		round.EndRound(endRoundState);
	}

	private SigningState FinalizeTransaction(uint256 roundId, ConstructionState constructionState)
	{
		SigningState signingState = constructionState.Finalize();
		return signingState;
	}
}
