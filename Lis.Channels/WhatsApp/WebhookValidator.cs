using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Lis.Channels.WhatsApp;

public sealed class WebhookValidator(IOptions<GowaOptions> options) {
	private readonly byte[] secretBytes = Encoding.UTF8.GetBytes(options.Value.WebhookSecret);

	public bool Validate(string signature, byte[] body) {
		if (string.IsNullOrEmpty(signature)) {
			return false;
		}

		byte[] hash = HMACSHA256.HashData(this.secretBytes, body);
		string computed = Convert.ToHexStringLower(hash);

		return CryptographicOperations.FixedTimeEquals(
			Encoding.UTF8.GetBytes(computed),
			Encoding.UTF8.GetBytes(signature));
	}
}
