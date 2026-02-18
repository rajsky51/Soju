using NBitcoin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Soju.Extensions;
using Soju.WabiSabi.Coordinator.Rounds;

namespace Soju.WabiSabi.Models.MultipartyTransaction;

public interface IEvent
{ }

public record RoundCreated(RoundParameters RoundParameters) : IEvent;
public record InputAdded(Coin Coin) : IEvent;
public record OutputAdded(TxOut Output) : IEvent;

public abstract record MultipartyTransactionState
{
	protected MultipartyTransactionState(RoundParameters parameters)
	{
		var builder = ImmutableList.CreateBuilder<IEvent>();
		builder.Add(new RoundCreated(parameters));
		Events = builder.ToImmutable();
	}

	public RoundParameters Parameters => Events.OfType<RoundCreated>().Single().RoundParameters;

	public IEnumerable<Coin> Inputs => Events.OfType<InputAdded>().Select(x => x.Coin);
	public IEnumerable<TxOut> Outputs => Events.OfType<OutputAdded>().Select(x => x.Output);

	public Money Balance => Inputs.Sum(x => x.Amount) - Outputs.Sum(x => x.Value);
	public int EstimatedInputsVsize => Inputs.Sum(x => x.TxOut.ScriptPubKey.EstimateInputVsize());
	public int OutputsVsize => Outputs.Sum(x => x.ScriptPubKey.EstimateOutputVsize());

	public int EstimatedVsize => MultipartyTransactionParameters.SharedOverhead + EstimatedInputsVsize + OutputsVsize;

	public int UnpaidSharedOverhead { get; init; } = MultipartyTransactionParameters.SharedOverhead;

	public FeeRate EffectiveFeeRate => new(Balance, EstimatedVsize - UnpaidSharedOverhead);

	public ImmutableList<IEvent> Events { get; init; } = ImmutableList<IEvent>.Empty;

	public MultipartyTransactionState GetStateFrom(int stateId) =>
		this with
		{
			Events = Events.Skip(stateId).ToImmutableList()
		};

	public MultipartyTransactionState Merge(MultipartyTransactionState diff) =>
		this with
		{
			Events = Events.AddRange(diff.Events)
		};
}
