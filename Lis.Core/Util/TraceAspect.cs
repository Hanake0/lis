using System.Diagnostics;

using AspectInjector.Broker;

namespace Lis.Core.Util;

[Injection(typeof(TraceAspect))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TraceAttribute(string name, ActivityKind kind = ActivityKind.Internal) :Attribute {
	public string       Name { get; } = name;
	public ActivityKind Kind { get; } = kind;
}

[Aspect(Scope.Global)]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class TraceAspect :Attribute {
	public static readonly ActivitySource ActivitySource = new("TraceAttribute");

	[Advice(Kind.Around, Targets = Target.Method)]
	public object Around([Argument(Source.Target)] Func<object[], object> target, [Argument(Source.Arguments)] object[] args, [Argument(Source.Triggers)] Attribute[] triggers) {
		Activity?      parent   = Activity.Current;
		TraceAttribute ta       = (TraceAttribute)triggers.Last(t => t is TraceAttribute);
		Activity?      activity = ActivitySource.StartActivity(ta.Name, ta.Kind);
		try {
			object result = target(args);

			if (result is Task or ValueTask) {
				Activity.Current = parent;
				return AwaitTask((dynamic)result, activity);
			}

			activity?.SetStatus(ActivityStatusCode.Ok);
			activity?.Dispose();

			return result;
		} catch (Exception ex) {
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			activity?.Dispose();
			throw;
		}
	}

	private static async Task AwaitTask(Task t, Activity? activity) {
		try {
			await t;
			activity?.SetStatus(ActivityStatusCode.Ok);
		} catch (Exception ex) {
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		} finally { activity?.Dispose(); }
	}

	private static async Task<T> AwaitTask<T>(Task<T> t, Activity? activity) {
		try {
			T result = await t;
			activity?.SetStatus(ActivityStatusCode.Ok);

			return result;
		} catch (Exception ex) {
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		} finally { activity?.Dispose(); }
	}

	private static async ValueTask AwaitTask(ValueTask t, Activity? activity) {
		try {
			await t;
			activity?.SetStatus(ActivityStatusCode.Ok);
		} catch (Exception ex) {
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		} finally { activity?.Dispose(); }
	}

	private static async ValueTask<T> AwaitTask<T>(ValueTask<T> t, Activity? activity) {
		try {
			T result = await t;
			activity?.SetStatus(ActivityStatusCode.Ok);

			return result;
		} catch (Exception ex) {
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		} finally { activity?.Dispose(); }
	}
}
