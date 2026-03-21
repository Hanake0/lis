namespace Lis.Core.Util;

public static class TimeZoneHelper {
	/// <summary>
	/// Resolves a timezone ID that works on both Windows (e.g. "E. South America Standard Time")
	/// and Linux/macOS (e.g. "America/Sao_Paulo"). Tries the ID directly first, then attempts
	/// Windows-to-IANA or IANA-to-Windows conversion.
	/// </summary>
	public static TimeZoneInfo Find(string id) {
		try {
			return TimeZoneInfo.FindSystemTimeZoneById(id);
		} catch (TimeZoneNotFoundException) {
			// Try converting Windows → IANA or IANA → Windows
			if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out string? iana))
				return TimeZoneInfo.FindSystemTimeZoneById(iana);

			if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out string? windows))
				return TimeZoneInfo.FindSystemTimeZoneById(windows);

			throw;
		}
	}
}
