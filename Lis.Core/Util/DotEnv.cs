namespace Lis.Core.Util;

public static class DotEnv {
	public static void Load(string fileName = ".env") {
		string? dir = Directory.GetCurrentDirectory();

		while (dir is not null) {
			string path = Path.Combine(dir, fileName);
			if (File.Exists(path)) {
				LoadFile(path);
				return;
			}

			dir = Directory.GetParent(dir)?.FullName;
		}
	}

	private static void LoadFile(string filePath) {
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
