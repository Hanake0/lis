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

	/// <summary>
	/// Returns the list of kernel functions available for the given agent.
	/// </summary>
	/// <summary>
	/// Checks whether a single tool is allowed by the agent's policy.
	/// Used by ToolRunner to block tools at execution time.
	/// </summary>
	public bool IsToolAllowed(string pluginName, string functionName, AgentEntity agent) {
		string fullName    = $"{pluginName}_{functionName}";
		string profileName = agent.ToolProfile ?? "standard";

		if (!MatchesProfile(fullName, profileName)) return false;
		if (agent.ToolsAllow is { Length: > 0 } allow && !MatchesAny(fullName, allow)) return false;
		if (agent.ToolsDeny is { Length: > 0 } deny && MatchesAny(fullName, deny)) return false;
		if (agent.ExecSecurity == "deny" && fullName.StartsWith("exec_", StringComparison.OrdinalIgnoreCase)) return false;

		return true;
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
