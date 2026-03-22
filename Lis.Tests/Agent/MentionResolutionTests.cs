using Lis.Agent;

namespace Lis.Tests.Agent;

public class MentionResolutionTests {
	[Theory]
	[InlineData("552731911808@s.whatsapp.net", "552731911808")]
	[InlineData("552731911808:0@s.whatsapp.net", "552731911808")]
	[InlineData("5511999999999:42@s.whatsapp.net", "5511999999999")]
	[InlineData("5511999999999@s.whatsapp.net", "5511999999999")]
	[InlineData("552731911808", "552731911808")]
	[InlineData("552731911808:0", "552731911808")]
	[InlineData("user@lid", "user")]
	public void ExtractPhone_ReturnsPhonePart(string jid, string expected) {
		string result = ConversationService.ExtractPhone(jid);
		Assert.Equal(expected, result);
	}

	[Fact]
	public void ExtractPhone_NoAtSign_ReturnsFullString() {
		string result = ConversationService.ExtractPhone("552731911808");
		Assert.Equal("552731911808", result);
	}
}
