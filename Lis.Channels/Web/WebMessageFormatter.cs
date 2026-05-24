using Lis.Core.Channel;

namespace Lis.Channels.Web;

/// <summary>
/// Passthrough formatter — web clients render markdown natively, no transformation needed.
/// </summary>
public sealed class WebMessageFormatter : IMessageFormatter {
	public string Format(string content) => content;
}
