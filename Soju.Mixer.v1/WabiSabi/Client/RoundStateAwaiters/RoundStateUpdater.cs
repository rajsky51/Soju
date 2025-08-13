using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soju.Bases;
using Soju.WabiSabi.Backend.PostRequests;
using Soju.WabiSabi.Backend.Rounds;
using Soju.WabiSabi.Models;

namespace Soju.WabiSabi.Client.RoundStateAwaiters;

public class RoundStateUpdater
{
	public RoundStateUpdater(IWabiSabiApiRequestHandler arenaRequestHandler)
	{
		_arenaRequestHandler = arenaRequestHandler;
	}

	private readonly IWabiSabiApiRequestHandler _arenaRequestHandler;
	private IDictionary<uint256, RoundState> RoundStates { get; set; } = new Dictionary<uint256, RoundState>();
	public Dictionary<TimeSpan, FeeRate> CoinJoinFeeRateMedians { get; private set; } = new();

	private readonly List<RoundStateAwaiter> _awaiters = new();
	private readonly object _awaitersLock = new();

	public bool AnyRound => RoundStates.Any();
	
	private DateTimeOffset LastSuccessfulRequestTime { get; set; }

	protected void Action()
	{

		var request = new RoundStateRequest(
			RoundStates.Select(x => new RoundStateCheckpoint(x.Key, x.Value.CoinjoinState.Events.Count)).ToImmutableList());

		using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(30));
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

		var response = await _arenaRequestHandler.GetStatusAsync(request, linkedCts.Token).ConfigureAwait(false);
		RoundState[] roundStates = response.RoundStates;

		CoinJoinFeeRateMedians = response.CoinJoinFeeRateMedians.ToDictionary(a => a.TimeFrame, a => a.MedianFeeRate);

		var updatedRoundStates = roundStates
			.Where(rs => RoundStates.ContainsKey(rs.Id))
			.Select(rs => (NewRoundState: rs, CurrentRoundState: RoundStates[rs.Id]))
			.Select(x => x.NewRoundState with { CoinjoinState = x.NewRoundState.CoinjoinState.AddPreviousStates(x.CurrentRoundState.CoinjoinState, x.NewRoundState.Id) })
			.ToList();

		var newRoundStates = roundStates
			.Where(rs => !RoundStates.ContainsKey(rs.Id));

		if (newRoundStates.Any(r => !r.IsRoundIdMatching()))
		{
			throw new InvalidOperationException(
				"Coordinator is cheating by creating rounds that do not match the parameters.");
		}

		// Don't use ToImmutable dictionary, because that ruins the original order and makes the server unable to suggest a round preference.
		// ToDo: ToDictionary doesn't guarantee the order by design so .NET team might change this out of our feet, so there's room for improvement here.
		RoundStates = newRoundStates.Concat(updatedRoundStates).ToDictionary(x => x.Id, x => x);

		lock (_awaitersLock)
		{
			foreach (var awaiter in _awaiters.Where(awaiter => awaiter.IsCompleted(RoundStates)).ToArray())
			{
				// The predicate was fulfilled.
				_awaiters.Remove(awaiter);
				break;
			}
		}

		LastSuccessfulRequestTime = DateTimeOffset.UtcNow;
	}

	private Task<RoundState> CreateRoundAwaiterAsync(uint256? roundId, Phase? phase, Predicate<RoundState>? predicate, CancellationToken cancellationToken)
	{
		RoundStateAwaiter? roundStateAwaiter = null;

		lock (_awaitersLock)
		{
			roundStateAwaiter = new RoundStateAwaiter(predicate, roundId, phase, cancellationToken);
			_awaiters.Add(roundStateAwaiter);
		}

		cancellationToken.Register(() =>
		{
			lock (_awaitersLock)
			{
				_awaiters.Remove(roundStateAwaiter);
			}
		});

		return roundStateAwaiter.Task;
	}

	public Task<RoundState> CreateRoundAwaiterAsync(Predicate<RoundState> predicate, CancellationToken cancellationToken)
	{
		return CreateRoundAwaiterAsync(null, null, predicate, cancellationToken);
	}

	public Task<RoundState> CreateRoundAwaiterAsync(uint256 roundId, Phase phase, CancellationToken cancellationToken)
	{
		return CreateRoundAwaiterAsync(roundId, phase, null, cancellationToken);
	}

	public Task<RoundState> CreateRoundAwaiter(Phase phase, CancellationToken cancellationToken)
	{
		return CreateRoundAwaiterAsync(null, phase, null, cancellationToken);
	}

	/// <summary>
	/// This might not contain up-to-date states. Make sure it is updated.
	/// </summary>
	public bool TryGetRoundState(uint256 roundId, [NotNullWhen(true)] out RoundState? roundState)
	{
		return RoundStates.TryGetValue(roundId, out roundState);
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		lock (_awaitersLock)
		{
			foreach (var awaiter in _awaiters)
			{
				awaiter.Cancel();
			}
		}
		return base.StopAsync(cancellationToken);
	}
}
