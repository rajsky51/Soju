using NBitcoin;

namespace Soju;

public interface IMixer
{
	uint256 CompleteMix();
}