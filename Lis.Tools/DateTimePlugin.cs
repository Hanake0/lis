using System.ComponentModel;
using System.Globalization;

using Lis.Core.Util;

using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class DateTimePlugin {
	private static readonly TimeZoneInfo Tz      = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
	private static readonly CultureInfo  Culture = new("pt-BR");

	[KernelFunction("get_current_datetime")]
	[Description("Gets the current date and time in Brasilia timezone (BRT)")]
	public static async Task<string> GetCurrentDateTimeAsync() {
		await ToolContext.NotifyAsync("🕐 Getting current date/time");
		return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz)
			.ToString("dddd, dd 'de' MMMM 'de' yyyy, HH:mm:ss", Culture);
	}
}
