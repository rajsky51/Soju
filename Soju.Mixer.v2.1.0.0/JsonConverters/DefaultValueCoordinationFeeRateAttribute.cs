using System.ComponentModel;
using Soju.WabiSabi.Models;

namespace Soju.JsonConverters;

public class DefaultValueCoordinationFeeRateAttribute : DefaultValueAttribute
{
	public DefaultValueCoordinationFeeRateAttribute(double feeRate)
		: base(new CoordinationFeeRate((decimal)feeRate))
	{
	}
}
