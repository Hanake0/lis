using System.Security.Cryptography;
using System.Text;
using Lis.Channels.WhatsApp;
using Microsoft.Extensions.Options;

namespace Lis.Tests.Channel;

public sealed class WebhookValidatorTests {
	private const string SECRET = "test-secret";
	private readonly WebhookValidator validator = new(Options.Create(new GowaOptions {
		BaseUrl = "http://localhost",
		DeviceId = "test",
		WebhookSecret = SECRET,
	}));

	[Fact]
	public void Validate_ValidSignature_ReturnsTrue() {
		byte[] body = Encoding.UTF8.GetBytes("{\"test\":true}");
		string signature = ComputeSignature(body);

		bool result = this.validator.Validate(signature, body);

		Assert.True(result);
	}

	[Fact]
	public void Validate_InvalidSignature_ReturnsFalse() {
		byte[] body = Encoding.UTF8.GetBytes("{\"test\":true}");

		bool result = this.validator.Validate("invalid-signature", body);

		Assert.False(result);
	}

	[Fact]
	public void Validate_EmptySignature_ReturnsFalse() {
		byte[] body = Encoding.UTF8.GetBytes("{\"test\":true}");

		bool result = this.validator.Validate("", body);

		Assert.False(result);
	}

	[Fact]
	public void Validate_TamperedBody_ReturnsFalse() {
		byte[] originalBody = Encoding.UTF8.GetBytes("{\"test\":true}");
		string signature = ComputeSignature(originalBody);
		byte[] tamperedBody = Encoding.UTF8.GetBytes("{\"test\":false}");

		bool result = this.validator.Validate(signature, tamperedBody);

		Assert.False(result);
	}

	private static string ComputeSignature(byte[] body) {
		byte[] key = Encoding.UTF8.GetBytes(SECRET);
		byte[] hash = HMACSHA256.HashData(key, body);
		return Convert.ToHexStringLower(hash);
	}
}
