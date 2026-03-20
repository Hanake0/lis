using System.Reflection;

using Lis.Core.Util;

using Microsoft.SemanticKernel;

namespace Lis.Agent;

/// <summary>
/// Maps registered kernel function names to their <see cref="ToolAuthLevel"/>.
/// Built once at startup from the kernel's plugin collection via reflection,
/// since Semantic Kernel does not propagate custom attributes to KernelFunction metadata.
/// </summary>
public sealed class ToolAuthRegistry {
	private readonly Dictionary<string, ToolAuthLevel> map = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Scans all plugins in the kernel and caches the <see cref="ToolAuthorizationAttribute"/>
	/// level for each function. Call once after all plugins are registered.
	/// </summary>
	public void Build(Kernel kernel) {
		this.map.Clear();

		foreach (KernelPlugin plugin in kernel.Plugins) {
			foreach (KernelFunction function in plugin) {
				ToolAuthLevel level = ResolveAuthLevel(plugin, function);
				string key = BuildKey(plugin.Name, function.Name);
				this.map[key] = level;
			}
		}
	}

	/// <summary>
	/// Looks up the authorization level for a tool call.
	/// Returns <see cref="ToolAuthLevel.Open"/> if not found.
	/// </summary>
	public ToolAuthLevel GetLevel(string? pluginName, string functionName) {
		string key = BuildKey(pluginName ?? "", functionName);
		return this.map.TryGetValue(key, out ToolAuthLevel level) ? level : ToolAuthLevel.Open;
	}

	private static string BuildKey(string pluginName, string functionName) =>
		$"{pluginName}-{functionName}";

	private static ToolAuthLevel ResolveAuthLevel(KernelPlugin plugin, KernelFunction function) {
		// KernelFunction wraps a method — try to get the MethodInfo via the underlying delegate
		MethodInfo? method = function.Metadata.AdditionalProperties
			.Where(kv => kv.Value is MethodInfo)
			.Select(kv => (MethodInfo)kv.Value!)
			.FirstOrDefault();

		// Fallback: look up by name on the plugin's CLR type
		if (method is null) {
			Type? pluginType = plugin.GetType().GenericTypeArguments.FirstOrDefault()
			                   ?? plugin.FirstOrDefault()?.GetType().DeclaringType;

			method = pluginType?
				.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
				.FirstOrDefault(m => m.GetCustomAttribute<KernelFunctionAttribute>()?.Name == function.Name);
		}

		ToolAuthorizationAttribute? attr = method?.GetCustomAttribute<ToolAuthorizationAttribute>();
		return attr?.Level ?? ToolAuthLevel.Open;
	}
}
