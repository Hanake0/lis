using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Lis.Channels.WhatsApp;

public sealed class WebhookValidator(IOptions<GowaOptions> options) {
	private readonly byte[] secretBytes = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);

	public bool Validate(string signature, byte[] body) {
		if (string.IsNullOrEmpty(signature)) return false;

		// Strip "sha256=" prefix if present (GitHub/GOWA webhook format)
		string hex = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
			? signature[7..]
			: signature;

		byte[] hash = HMACSHA256.HashData(this.secretBytes, body);
		string computed = Convert.ToHexStringLower(hash);

		return CryptographicOperations.FixedTimeEquals(
			Encoding.UTF8.GetBytes(computed),
			Encoding.UTF8.GetBytes(hex));
	}
}
