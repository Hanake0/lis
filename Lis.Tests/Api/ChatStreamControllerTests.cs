using System.Text;

using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Core.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lis.Tests.Api;

/// <summary>
/// Integration-style tests for <see cref="ChatStreamController"/>. We use the
/// real <see cref="EventBus"/> + a <see cref="DefaultHttpContext"/> with a
/// MemoryStream body so we can assert the SSE bytes that hit the wire.
/// </summary>
public class ChatStreamControllerTests {

	[Fact]
	public async Task Stream_Forwards_Events_Tagged_For_This_Chat() {
		EventBus              bus     = new();
		MemoryStream          body    = new();
		ChatStreamController  sut     = MakeController(bus, body, out CancellationTokenSource cts);

		Task streamTask = sut.Stream("c1", cts.Token);

		// Publish two on-topic events, one off-topic. The filter should drop the off-topic one.
		await bus.PublishAsync(new ServerEvent(0, ChatStreamEventNames.Chunk,
			new ChunkPayload("c1", "m1", "hello", false), DateTimeOffset.UtcNow, "chat:c1"));
		await bus.PublishAsync(new ServerEvent(0, ChatStreamEventNames.Chunk,
			new ChunkPayload("c2", "m2", "ignored", false), DateTimeOffset.UtcNow, "chat:c2"));
		await bus.PublishAsync(new ServerEvent(0, ChatStreamEventNames.Chunk,
			new ChunkPayload("c1", "m1", " world", true), DateTimeOffset.UtcNow, "chat:c1"));

		// Give the reader loop a beat to drain into the response body, then cut it.
		await WaitForBytesAsync(body, expected: 2, timeoutMs: 1000);
		await cts.CancelAsync();
		await streamTask;

		string text = Encoding.UTF8.GetString(body.ToArray());
		// Two events, both with event:chunk lines, both with chat_id=c1 in the payload.
		Assert.Equal(2, CountOccurrences(text, "event: chunk"));
		Assert.Contains("\"chat_id\":\"c1\"", text);
		Assert.DoesNotContain("\"chat_id\":\"c2\"", text);
	}

	[Fact]
	public async Task Stream_Writes_Sse_Frames_With_Id_Event_And_Data_Lines() {
		EventBus              bus  = new();
		MemoryStream          body = new();
		ChatStreamController  sut  = MakeController(bus, body, out CancellationTokenSource cts);

		Task streamTask = sut.Stream("c1", cts.Token);
		await bus.PublishAsync(new ServerEvent(0, ChatStreamEventNames.Typing,
			new TypingPayload("c1"), DateTimeOffset.UtcNow, "chat:c1"));

		await WaitForBytesAsync(body, expected: 1, timeoutMs: 1000);
		await cts.CancelAsync();
		await streamTask;

		string text = Encoding.UTF8.GetString(body.ToArray());
		Assert.Contains("id: 1\n",          text);
		Assert.Contains("event: typing\n",  text);
		Assert.Contains("data: ",           text);
		Assert.EndsWith("\n\n",             text);
	}

	[Fact]
	public async Task Stream_Sets_The_SSE_Headers() {
		EventBus              bus  = new();
		MemoryStream          body = new();
		ChatStreamController  sut  = MakeController(bus, body, out CancellationTokenSource cts);

		Task streamTask = sut.Stream("c1", cts.Token);
		// Headers are set BEFORE the first read; cancel immediately to terminate quickly.
		await cts.CancelAsync();
		await streamTask;

		Assert.Equal("text/event-stream", sut.Response.Headers.ContentType.ToString());
		Assert.Equal("no-cache",          sut.Response.Headers.CacheControl.ToString());
		Assert.Equal("no",                sut.Response.Headers["X-Accel-Buffering"].ToString());
	}

	[Fact]
	public async Task Stream_Filters_Strictly_On_Subject_Prefix_Mismatch() {
		EventBus              bus  = new();
		MemoryStream          body = new();
		ChatStreamController  sut  = MakeController(bus, body, out CancellationTokenSource cts);

		Task streamTask = sut.Stream("c1", cts.Token);

		// `chat:c10` shares the prefix `chat:c1` but is a different chat — should NOT match.
		// `chat:c1` should match.
		await bus.PublishAsync(new ServerEvent(0, ChatStreamEventNames.Typing, new TypingPayload("c10"), DateTimeOffset.UtcNow, "chat:c10"));
		await bus.PublishAsync(new ServerEvent(0, ChatStreamEventNames.Typing, new TypingPayload("c1"),  DateTimeOffset.UtcNow, "chat:c1"));

		await WaitForBytesAsync(body, expected: 1, timeoutMs: 1000);
		await cts.CancelAsync();
		await streamTask;

		string text = Encoding.UTF8.GetString(body.ToArray());
		Assert.Equal(1, CountOccurrences(text, "event: typing"));
		Assert.DoesNotContain("\"chat_id\":\"c10\"", text);
	}

	// ── Helpers ────────────────────────────────────────────────────

	private static ChatStreamController MakeController(EventBus bus, Stream body, out CancellationTokenSource cts) {
		ChatStreamController controller = new(bus);
		DefaultHttpContext   http       = new();
		http.Response.Body = body;
		controller.ControllerContext = new ControllerContext { HttpContext = http };
		cts = new CancellationTokenSource();
		return controller;
	}

	/// <summary>
	/// Polls the response body's length until it contains at least <paramref name="expected"/>
	/// SSE frames (counted by "\n\n" terminators). Short polling loop so async writes get a
	/// chance to land without a brittle hard-coded sleep.
	/// </summary>
	private static async Task WaitForBytesAsync(MemoryStream body, int expected, int timeoutMs) {
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTime.UtcNow < deadline) {
			if (CountOccurrences(Encoding.UTF8.GetString(body.ToArray()), "\n\n") >= expected) return;
			await Task.Delay(20);
		}
	}

	private static int CountOccurrences(string haystack, string needle) {
		int count = 0;
		int idx = 0;
		while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) {
			count++;
			idx += needle.Length;
		}
		return count;
	}
}
