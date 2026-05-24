namespace Lis.Core.Channel;

/// <summary>
/// How a channel wants an incoming message dispatched.
/// Lets channels opt out of debounce per-message without bypassing the abstraction.
/// </summary>
public enum MessageDispatchMode {
	/// <summary>Default: debounce timer + queue-if-responding (WhatsApp behavior).</summary>
	Default,

	/// <summary>Skip debounce and respond now. Still respects the per-chat serialization gate.</summary>
	Immediate,

	/// <summary>Persist the message with queued=true and do not trigger a response.</summary>
	QueueOnly,

	/// <summary>Cancel the active AI response (if any), then respond like Immediate.</summary>
	Interrupt,
}
