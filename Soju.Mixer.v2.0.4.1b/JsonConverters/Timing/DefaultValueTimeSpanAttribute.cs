using System.ComponentModel;

namespace Soju.JsonConverters.Timing;

public class DefaultValueTimeSpanAttribute : DefaultValueAttribute
{
	public DefaultValueTimeSpanAttribute(string json) : base(TimeSpanJsonConverter.Parse(json))
	{
	}
}
