using NBitcoin;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Soju.Crypto;
using Soju.Logging;
using Soju.WabiSabi.Backend.Models;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Models;
using Soju.Blockchain.TransactionOutputs;
using Soju.Extensions;
using System.Net.Http;
using Soju.WabiSabi.Client.RoundStateAwaiters;
using WabiSabi.Crypto.ZeroKnowledge;
using Soju.WabiSabi.Models.MultipartyTransaction;

namespace Soju.WabiSabi.Client.CoinJoin.Client;

public class AliceClient
{
	private AliceClient(
		Guid aliceId,
		RoundState roundState,
		ArenaClient arenaClient,
		SmartCoin coin,
		IEnumerable<Credential> issuedAmountCredentials,
		IEnumerable<Credential> issuedVsizeCredentials)
	{
		var roundParameters = roundState.CoinjoinState.Parameters;
		AliceId = aliceId;
		RoundId = roundState.Id;
		_arenaClient = arenaClient;
		SmartCoin = coin;
		_feeRate = roundParameters.MiningFeeRate;
		IssuedAmountCredentials = issuedAmountCredentials;
		IssuedVsizeCredentials = issuedVsizeCredentials;
		_maxVsizeAllocationPerAlice = roundParameters.MaxVsizeAllocationPerAlice;
	}

	public Guid AliceId { get; }
	public uint256 RoundId { get; }
	private readonly ArenaClient _arenaClient;
	public SmartCoin SmartCoin { get; }
	private readonly FeeRate _feeRate;
	public IEnumerable<Credential> IssuedAmountCredentials { get; private set; }
	public IEnumerable<Credential> IssuedVsizeCredentials { get; private set; }
	private readonly long _maxVsizeAllocationPerAlice;
	
	public static AliceClient CreateAndRegisterInput(
		RoundState roundState,
		ArenaClient arenaClient,
		SmartCoin coin)
	{
		return RegisterInput(roundState, arenaClient, coin);
	}
	
	public static async Task<AliceClient> CreateRegisterAndConfirmInputAsync(
		RoundState roundState,
		ArenaClient arenaClient,
		SmartCoin coin,
		RoundStateUpdater roundStatusUpdater)
	{
		AliceClient aliceClient = RegisterInput(roundState, arenaClient, coin);
		try
		{
			await aliceClient.ConfirmConnectionAsync(roundStatusUpdater, confirmationCancellationToken).ConfigureAwait(false);

			Logger.LogInfo($"Round ({aliceClient.RoundId}), Alice ({aliceClient.AliceId}): Connection was confirmed.");
		}
		catch (WabiSabiProtocolException wpe) when (wpe.ErrorCode
			is WabiSabiProtocolErrorCode.RoundNotFound
			or WabiSabiProtocolErrorCode.WrongPhase
			or WabiSabiProtocolErrorCode.AliceAlreadyRegistered
			or WabiSabiProtocolErrorCode.AliceAlreadyConfirmedConnection)
		{
			// Do not unregister.
			throw;
		}
		catch (UnexpectedRoundPhaseException)
		{
			// Do not unregister.
			throw;
		}
		catch (Exception) when (aliceClient is { })
		{
			// NOTE: Always unregister
			// Unregistering coins is only possible before connection confirmation phase.
			aliceClient.TryToUnregisterAlices();
			throw;
		}

		return aliceClient;
	}

	private static AliceClient RegisterInput(RoundState roundState, ArenaClient arenaClient, SmartCoin coin)
	{
		ArenaResponse<Guid> response = arenaClient.RegisterInput(roundState.Id, coin.Coin.Outpoint);
		AliceClient aliceClient = new(response.Value, roundState, arenaClient, coin, response.IssuedAmountCredentials, response.IssuedVsizeCredentials);
		coin.CoinJoinInProgress = true;

		Logger.LogInfo($"Round ({roundState.Id}), Alice ({aliceClient.AliceId}): Registered {coin.Outpoint}.");

		return aliceClient;
	}

	private async Task ConfirmConnection(RoundStateUpdater roundStatusUpdater)
	{
		long[] amountsToRequest = { EffectiveValue.Satoshi };
		long[] vsizesToRequest = { _maxVsizeAllocationPerAlice - SmartCoin.ScriptPubKey.EstimateInputVsize() };
		
		TryConfirmConnection(amountsToRequest, vsizesToRequest);
		do
		{

			try
			{
				await roundStatusUpdater
					.CreateRoundAwaiterAsync(
						RoundId,
						Phase.ConnectionConfirmation,
						cts.Token)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				cancellationToken.ThrowIfCancellationRequested();
			}
		}
		while (!await TryConfirmConnectionAsync(amountsToRequest, vsizesToRequest, cancellationToken).ConfigureAwait(false));
	}

	private bool TryConfirmConnection(IEnumerable<long> amountsToRequest, IEnumerable<long> vsizesToRequest)
	{
		var response = _arenaClient
			.ConfirmConnection(
				RoundId,
				AliceId,
				amountsToRequest,
				vsizesToRequest,
				IssuedAmountCredentials,
				IssuedVsizeCredentials);

		IssuedAmountCredentials = response.IssuedAmountCredentials;
		IssuedVsizeCredentials = response.IssuedVsizeCredentials;

		LastSuccessfulInputConnectionConfirmation = DateTimeOffset.UtcNow;

		var isConfirmed = response.Value;
		return isConfirmed;
	}

	public void TryToUnregisterAlices()
	{
		try
		{
			RemoveInput();
			SmartCoin.CoinJoinInProgress = false;
			Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Unregistered {SmartCoin.Outpoint}.");
		}
		catch (OperationCanceledException e)
		{
			Logger.LogTrace(e);
		}
		catch (Exception e) when (e is HttpRequestException or WabiSabiProtocolException)
		{
			Logger.LogDebug($"Unregistration failed for coin '{SmartCoin.Coin.Outpoint}'.", e);
		}
		catch (Exception e)
		{
			// Log and swallow the exception because there is nothing else that can be done here.
			Logger.LogWarning($"{SmartCoin.Coin.Outpoint} unregistration failed with {e}.");
		}
	}

	public void Finish()
	{
		SmartCoin.CoinJoinInProgress = false;
	}

	public void RemoveInput()
	{
		_arenaClient.RemoveInput(RoundId, AliceId);
		SmartCoin.CoinJoinInProgress = false;
		Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Inputs removed.");
	}

	public async Task SignTransactionAsync(TransactionWithPrecomputedData unsignedCoinJoin, IKeyChain keyChain, CancellationToken cancellationToken)
	{
		await _arenaClient.SignTransactionAsync(RoundId, SmartCoin.Coin, keyChain, unsignedCoinJoin, cancellationToken).ConfigureAwait(false);

		Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Posted a signature.");
	}

	public async Task ReadyToSignAsync(CancellationToken cancellationToken)
	{
		await _arenaClient.ReadyToSignAsync(RoundId, AliceId, cancellationToken).ConfigureAwait(false);
		Logger.LogInfo($"Round ({RoundId}), Alice ({AliceId}): Ready to sign.");
	}

	public Money EffectiveValue => SmartCoin.EffectiveValue(_feeRate);
}
