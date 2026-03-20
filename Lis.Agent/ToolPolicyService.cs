using System.IO.Enumeration;

using Lis.Persistence.Entities;

using Microsoft.SemanticKernel;

namespace Lis.Agent;

/// <summary>
/// Resolves which tools are available for a given agent based on tool profiles,
/// allow/deny globs, and exec security settings.
/// </summary>
public sealed class ToolPolicyService {

	// Profile → plugin prefixes included
	private static readonly Dictionary<string, string[]> Profiles = new(StringComparer.OrdinalIgnoreCase) {
		["minimal"]  = ["dt_", "resp_"],
		["standard"] = ["dt_", "resp_", "mem_", "prompt_", "cfg_", "web_"],
		["coding"]   = ["dt_", "resp_", "mem_", "prompt_", "cfg_", "web_", "exec_", "fs_"],
		["full"]     = [] // empty = everything
	};

	// Group shorthands → plugin prefixes
	private static readonly Dictionary<string, string[]> Groups = new(StringComparer.OrdinalIgnoreCase) {
		["group:runtime"] = ["exec_"],
		["group:fs"]      = ["fs_"],
		["group:web"]     = ["web_"],
		["group:browser"] = ["browser_"],
		["group:memory"]  = ["mem_"],
		["group:config"]  = ["cfg_"]
	};

	// Profile → allowed plugin names (registration names from AgentSetup)
	private static readonly Dictionary<string, HashSet<string>> ProfilePlugins = new(StringComparer.OrdinalIgnoreCase) {
		["minimal"]  = ["dt", "resp"],
		["standard"] = ["dt", "resp", "mem", "prompt", "cfg", "web"],
		["coding"]   = ["dt", "resp", "mem", "prompt", "cfg", "web", "exec", "fs"],
		["full"]     = [] // empty = everything
	};

	/// <summary>
	/// Returns the set of plugin names allowed for the given agent.
	/// Used with Kernel.Clone() to strip unwanted plugins before the API call.
	/// </summary>
	public HashSet<string> GetAllowedPluginNames(AgentEntity agent) {
		string profileName = agent.ToolProfile ?? "standard";

		if (!ProfilePlugins.TryGetValue(profileName, out HashSet<string>? baseSet) || baseSet.Count == 0) {
			// "full" or unknown profile → all plugins allowed, start with everything
			// Apply deny rules below if any
			HashSet<string> all = ["dt", "resp", "mem", "prompt", "cfg", "web", "exec", "fs", "browser"];

			if (agent.ExecSecurity == "deny") all.Remove("exec");
			if (agent.ToolsDeny is { Length: > 0 } deny) ApplyDenyGlobs(all, deny);

			return all;
		}

		HashSet<string> result = [..baseSet];

		// exec_security overrides profile inclusion
		if (agent.ExecSecurity == "deny") result.Remove("exec");

		// Apply deny globs
		if (agent.ToolsDeny is { Length: > 0 } denyPatterns) ApplyDenyGlobs(result, denyPatterns);

		// Apply allow globs (if set, further restrict to only matching)
		if (agent.ToolsAllow is { Length: > 0 } allowPatterns) ApplyAllowGlobs(result, allowPatterns);

		return result;
	}

	private static void ApplyDenyGlobs(HashSet<string> plugins, string patterns) {
		foreach (string raw in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			// Expand group shorthands
			if (Groups.TryGetValue(raw, out string[]? prefixes)) {
				foreach (string prefix in prefixes)
					plugins.RemoveWhere(p => (p + "_").Equals(prefix, StringComparison.OrdinalIgnoreCase));
				continue;
			}

			// Remove plugins whose prefix matches the glob
			plugins.RemoveWhere(p => FileSystemName.MatchesSimpleExpression(raw, p + "_*", ignoreCase: true));
		}
	}

	private static void ApplyAllowGlobs(HashSet<string> plugins, string patterns) {
		HashSet<string> allowed = [];
		foreach (string raw in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (Groups.TryGetValue(raw, out string[]? prefixes)) {
				foreach (string prefix in prefixes)
					foreach (string p in plugins)
						if ((p + "_").Equals(prefix, StringComparison.OrdinalIgnoreCase))
							allowed.Add(p);
				continue;
			}

			foreach (string p in plugins)
				if (FileSystemName.MatchesSimpleExpression(raw, p + "_*", ignoreCase: true))
					allowed.Add(p);
		}
		plugins.IntersectWith(allowed);
	}

	public IReadOnlyList<KernelFunction> ResolveAvailableTools(Kernel kernel, AgentEntity agent) {
		string profileName = agent.ToolProfile ?? "standard";
		List<KernelFunction> result = [];

		foreach (KernelPlugin plugin in kernel.Plugins) {
			foreach (KernelFunction function in plugin) {
				string fullName = $"{plugin.Name}_{function.Name}";

				// Step 1: Profile filter
				if (!MatchesProfile(fullName, profileName)) continue;

				// Step 2: Allow filter (if set, only matching pass)
				if (agent.ToolsAllow is { Length: > 0 } allow && !MatchesAny(fullName, allow)) continue;

				// Step 3: Deny filter (deny always wins)
				if (agent.ToolsDeny is { Length: > 0 } deny && MatchesAny(fullName, deny)) continue;

				// Step 4: Exec security — if deny, exclude exec tools
				if (agent.ExecSecurity == "deny" && fullName.StartsWith("exec_", StringComparison.OrdinalIgnoreCase)) continue;

				result.Add(function);
			}
		}

		return result;
	}

	private static bool MatchesProfile(string toolName, string profileName) {
		if (!Profiles.TryGetValue(profileName, out string[]? prefixes)) return true;

		// "full" profile has empty prefixes array → allow everything
		if (prefixes.Length == 0) return true;

		foreach (string prefix in prefixes)
			if (toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				return true;

		return false;
	}

	private static bool MatchesAny(string toolName, string patterns) {
		foreach (string raw in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			string pattern = raw;

			// Expand group shorthands
			if (Groups.TryGetValue(pattern, out string[]? prefixes)) {
				foreach (string prefix in prefixes)
					if (toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						return true;
				continue;
			}

			// Glob match
			if (FileSystemName.MatchesSimpleExpression(pattern, toolName, ignoreCase: true))
				return true;
		}

		return false;
	}
}
