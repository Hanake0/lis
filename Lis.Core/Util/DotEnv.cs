namespace Lis.Core.Util;

public static class DotEnv {
	public static void Load(string filePath = ".env") {
		if (!File.Exists(filePath)) return;

		foreach (string line in File.ReadAllLines(filePath)) {
			string[] parts = line.Split('=', StringSplitOptions.RemoveEmptyEntries);

			// Skip empty/malformed lines
			if (parts.Length <= 1) continue;

			// Skip commented out lines
			if (parts[0].StartsWith('#')) continue;

			// Join the rest of the parts in case the value has a '='
			string env = string.Join('=', parts.Skip(1));

			Environment.SetEnvironmentVariable(parts[0], env);
		}
	}
}
