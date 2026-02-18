using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using Soju.Bases;
using Soju.BitcoinRpc;
using Soju.Crypto.Randomness;
using Soju.Extensions;
using Soju.Logging;
using Soju.WabiSabi.Coordinator.Models;
using Soju.WabiSabi.Coordinator.Statistics;
using Soju.WabiSabi.Models;
using Soju.WabiSabi.Models.MultipartyTransaction;

namespace Soju.WabiSabi.Coordinator.Rounds;

public partial class Arena
{
	public Arena(
		WabiSabiConfig config,
		IRPCClient rpc,
		RoundParameterFactory roundParameterFactory,
		CoinJoinScriptStore? coinJoinScriptStore = null)
	{
		_config = config;
		Rpc = rpc;
		CoinJoinScriptStore = coinJoinScriptStore;
		_roundParameterFactory = roundParameterFactory;
		MaxSuggestedAmountProvider = new(_config);
	}

	public event EventHandler<Transaction>? CoinJoinBroadcast;

	public HashSet<Round> Rounds { get; } = new();
	public ImmutableList<RoundState> RoundStates { get; private set; } = ImmutableList<RoundState>.Empty;
	internal ConcurrentQueue<uint256> DisruptedRounds { get; } = new();
	private readonly WabiSabiConfig _config;
	internal IRPCClient Rpc { get; }
	public CoinJoinScriptStore? CoinJoinScriptStore { get; }
	private readonly RoundParameterFactory _roundParameterFactory;
	public MaxSuggestedAmountProvider MaxSuggestedAmountProvider { get; }

	public void SetRoundStates()
	{
		// Order rounds ascending by max suggested amount, then ascending by input count.
		// This will make sure WW2.0.1 clients register according to our desired order.
		var rounds = Rounds
						.OrderBy(x => x.Parameters.MaxSuggestedAmount)
						.ThenBy(x => x.InputCount)
						.ToList();

		RoundStates = rounds.Select(r => RoundState.FromRound(r, stateId: 0)).ToImmutableList();
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
				if (offendingAlices.Count != 0)
				{
					// TODO: Change this in other versions
					Debug.Assert(false);
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

					round.CoinjoinState = FinalizeTransaction(round.Id, coinjoin);

					SetRoundPhase(round, Phase.TransactionSigning);
				} 
				else 
				{
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
					FeeRate targetFeeRate = Rpc.EstimateConservativeSmartFee((int)_config.ConfirmationTarget).FeeRate;
					round.LogInfo($"Current Fee Rate on the Network: {targetFeeRate.SatoshiPerByte} sat/vByte. Confirmation target is: {(int)_config.ConfirmationTarget} blocks.");
				}
				catch (Exception ex)
				{
					Logger.LogDebug($"Could not log fee rate monitoring: '{ex.Message}'.");
				}

				round.LogInfo($"Number of inputs: {coinjoin.Inputs.Count}.");
				round.LogInfo($"Number of outputs: {coinjoin.Outputs.Count}.");
				round.LogInfo($"Serialized Size: {coinjoin.GetSerializedSize() / 1024.0} KB.");
				round.LogInfo($"VSize: {coinjoin.GetVirtualSize() / 1024.0} KB.");
				var indistinguishableOutputs = coinjoin.GetIndistinguishableOutputs(includeSingle: true);
				foreach (var (value, count) in indistinguishableOutputs.Where(x => x.count > 1))
				{
					round.LogInfo($"There are {count} occurrences of {value.ToString(true, false)} outputs.");
				}

				round.LogInfo(
					$"There are {indistinguishableOutputs.Count(x => x.count == 1)} occurrences of unique outputs.");

				// Broadcasting.
				Rpc.SendRawTransaction(coinjoin);
				EndRound(round, EndRoundState.TransactionBroadcasted);
				round.LogInfo($"Successfully broadcast the coinjoin: {coinjoin.GetHash()}.");

				var coordinatorScriptPubKey = _config.GetNextCleanCoordinatorScript();
				if (round.CoordinatorScript == coordinatorScriptPubKey)
				{
					_config.MakeNextCoordinatorScriptDirty();
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
			round.LogError($"Transaction broadcasting failed: '{ex}'.");
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
		// NOTE: If we are creating a new round, then the previous round should already 
		// have ended and been removed.
		Debug.Assert(Rounds.Count == 0);
		
		FeeRate feeRate = Rpc.EstimateConservativeSmartFee((int)_config.ConfirmationTarget).FeeRate;
		RoundParameters parameters = _roundParameterFactory.CreateRoundParameter(feeRate, MaxSuggestedAmountProvider.MaxSuggestedAmount);
		Round r = new Round(parameters, SecureRandom.Instance);
		AddRound(r);
		r.LogInfo($"Created round with parameters: {nameof(r.Parameters.MaxSuggestedAmount)}:'{r.Parameters.MaxSuggestedAmount}' BTC.");
	}

	public void TimeoutRounds()
	{
		Round[] expiredRounds = Rounds.Where(round => round.Phase == Phase.Ended).ToArray();
		Debug.Assert(Rounds.Count == 1 && expiredRounds.Length == 1);
		
		Rounds.Remove(expiredRounds[0]);
	}

	public static ConstructionState AddCoordinationFee(Round round, ConstructionState coinjoin, Script coordinatorScriptPubKey)
	{
		var sizeToPayFor = coinjoin.EstimatedVsize + coordinatorScriptPubKey.EstimateOutputVsize();
		var miningFee = round.Parameters.MiningFeeRate.GetFee(sizeToPayFor) + Money.Satoshis(1);

		var availableCoordinationFee = coinjoin.Balance - miningFee;

		round.LogInfo($"Available coordination: {availableCoordinationFee}.");

		// The coordinator must pay output creation at round's FeeRate, but then he can wait to spend the output.
		var minEconomicalOutput = round.Parameters.MiningFeeRate.GetFee(coordinatorScriptPubKey.EstimateOutputVsize()) +
		                          new FeeRate(1.0m).GetFee(coordinatorScriptPubKey.EstimateInputVsize());

		if (availableCoordinationFee > minEconomicalOutput)
		{
			var txOut = new TxOut(availableCoordinationFee, coordinatorScriptPubKey);
			if (!txOut.IsDust())
			{
				return coinjoin.AddOutputNoMinAmountCheck(txOut)
					.AsPayingForSharedOverhead();
			}
		}

		round.LogWarning($"Available coordination fee wasn't taken, because it was too small: {availableCoordinationFee}.");
		return coinjoin;
	}

	private Script GetCoordinatorScriptPreventReuse(Round round)
	{
		var coordinatorScriptPubKey = _config.GetNextCleanCoordinatorScript();

		// Prevent coordinator script reuse.
		if (Rounds.Any(r => r.CoordinatorScript == coordinatorScriptPubKey))
		{
			_config.MakeNextCoordinatorScriptDirty();
			coordinatorScriptPubKey = _config.GetNextCleanCoordinatorScript();
			round.LogWarning("Coordinator script pub key was already used by another round, making it dirty and taking a new one.");
		}

		return coordinatorScriptPubKey;
	}

	private void AddRound(Round round)
	{
		Rounds.Add(round);
	}

	public void AbortRound(uint256 roundId)
	{
		DisruptedRounds.Enqueue(roundId);
	}

	private void AbortDisruptedRounds()
	{
		while (DisruptedRounds.TryDequeue(out var disruptedRoundId))
		{
			var roundOrNull = Rounds.FirstOrDefault(x => x.Id == disruptedRoundId);
			if (roundOrNull is { } nonNullRound)
			{
				nonNullRound.LogInfo("Round aborted because it was disrupted by double spenders.");
				nonNullRound.EndRound(EndRoundState.AbortedDoubleSpendingDetected);
			}
		}
	}

	private void SetRoundPhase(Round round, Phase phase)
	{
		round.SetPhase(phase);
	}

	internal void EndRound(Round round, EndRoundState endRoundState)
	{
		round.EndRound(endRoundState);
	}

	private SigningState FinalizeTransaction(uint256 roundId, ConstructionState constructionState)
	{
		SigningState signingState = constructionState.Finalize();
		return signingState;
	}

	/// <summary>
	/// If too many inputs seem to misbehave, problem is probably on coordinator's side.
	/// Don't ban in that case to avoid huge amount of false-positives.
	/// </summary>
	private static bool ReasonableOffendersCount(int offendersCount, int minInputCount) => offendersCount <= minInputCount;
}
