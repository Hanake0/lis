using Lis.Channels.WhatsApp;

namespace Lis.Tests.Channel;

public class WhatsAppFormatterTests {
	private readonly WhatsAppFormatter _sut = new();

	// ── Guard Clauses ───────────────────────────────────────────────

	[Fact]
	public void Format_Null_ReturnsEmpty() {
		string result = this._sut.Format(null!);
		Assert.Equal(string.Empty, result);
	}

	[Fact]
	public void Format_Empty_ReturnsEmpty() {
		string result = this._sut.Format("");
		Assert.Equal(string.Empty, result);
	}

	[Fact]
	public void Format_Whitespace_ReturnsEmpty() {
		string result = this._sut.Format("   ");
		Assert.Equal(string.Empty, result);
	}

	[Fact]
	public void Format_PlainText_Unchanged() {
		string result = this._sut.Format("Hello, world!");
		Assert.Equal("Hello, world!", result);
	}

	// ── Bold ────────────────────────────────────────────────────────

	[Fact]
	public void Format_Bold_ConvertsToSingleAsterisk() {
		string result = this._sut.Format("This is **bold** text.");
		Assert.Equal("This is *bold* text.", result);
	}

	[Fact]
	public void Format_MultipleBold_ConvertsAll() {
		string result = this._sut.Format("**one** and **two**");
		Assert.Equal("*one* and *two*", result);
	}

	// ── Strikethrough ───────────────────────────────────────────────

	[Fact]
	public void Format_Strikethrough_ConvertsTilde() {
		string result = this._sut.Format("This is ~~deleted~~ text.");
		Assert.Equal("This is ~deleted~ text.", result);
	}

	// ── Headers ─────────────────────────────────────────────────────

	[Fact]
	public void Format_H1_ConvertsToBold() {
		string result = this._sut.Format("# Main Title");
		Assert.Equal("*Main Title*", result);
	}

	[Fact]
	public void Format_H2_ConvertsToBold() {
		string result = this._sut.Format("## Sub Title");
		Assert.Equal("*Sub Title*", result);
	}

	[Fact]
	public void Format_H3_ConvertsToBold() {
		string result = this._sut.Format("### Section");
		Assert.Equal("*Section*", result);
	}

	// ── Bullet Lists ────────────────────────────────────────────────

	[Fact]
	public void Format_DashBullets_ConvertsToBulletChar() {
		string input  = "- First\n- Second\n- Third";
		string result = this._sut.Format(input);
		Assert.Equal("• First\n• Second\n• Third", result);
	}

	[Fact]
	public void Format_AsteriskBullets_ConvertsToBulletChar() {
		string input  = "* First\n* Second";
		string result = this._sut.Format(input);
		Assert.Equal("• First\n• Second", result);
	}

	// ── Code Blocks ─────────────────────────────────────────────────

	[Fact]
	public void Format_FencedCodeBlock_PreservedUnchanged() {
		string input  = "Before\n```\n**bold** inside code\n```\nAfter";
		string result = this._sut.Format(input);
		Assert.Contains("**bold** inside code", result);
		Assert.Contains("```", result);
	}

	[Fact]
	public void Format_InlineCode_PreservedUnchanged() {
		string input  = "Use `**not bold**` in code";
		string result = this._sut.Format(input);
		Assert.Contains("`**not bold**`", result);
	}

	// ── Links ───────────────────────────────────────────────────────

	[Fact]
	public void Format_Link_ConvertsToTextAndUrl() {
		string result = this._sut.Format("Visit [Google](https://google.com) now.");
		Assert.Equal("Visit Google (https://google.com) now.", result);
	}

	[Fact]
	public void Format_LinkTextEqualsUrl_ShowsUrlOnly() {
		string result = this._sut.Format("[https://google.com](https://google.com)");
		Assert.Equal("https://google.com", result);
	}

	// ── Horizontal Rules ────────────────────────────────────────────

	[Fact]
	public void Format_HorizontalRule_ConvertsToDash() {
		string result = this._sut.Format("Above\n---\nBelow");
		Assert.Equal("Above\n───\nBelow", result);
	}

	// ── Blank Line Collapsing ───────────────────────────────────────

	[Fact]
	public void Format_ExcessiveBlankLines_CollapsedToTwo() {
		string result = this._sut.Format("A\n\n\n\nB");
		Assert.Equal("A\n\nB", result);
	}

	// ── Tables ──────────────────────────────────────────────────────

	[Fact]
	public void Format_Table_ConvertsToBullets() {
		string input = "| Campo | Valor |\n|---|---|\n| Nome | Lis |\n| Modelo | claude-opus-4-6 |";
		string result = this._sut.Format(input);

		Assert.Contains("*Lis*", result);
		Assert.Contains("• Valor: claude-opus-4-6", result);
		Assert.DoesNotContain("|", result);
	}

	// ── Mixed Formatting ────────────────────────────────────────────

	[Fact]
	public void Format_MixedFormatting_AllConverted() {
		string input = "# Title\n\n**Bold** and ~~strike~~\n\n- Item 1\n- Item 2\n\n---\n\nVisit [here](https://example.com)";
		string result = this._sut.Format(input);

		Assert.Contains("*Title*", result);
		Assert.Contains("*Bold*", result);
		Assert.Contains("~strike~", result);
		Assert.Contains("• Item 1", result);
		Assert.Contains("• Item 2", result);
		Assert.Contains("───", result);
		Assert.Contains("here (https://example.com)", result);
		Assert.DoesNotContain("**", result);
		Assert.DoesNotContain("~~", result);
	}

	// ── False Positives ─────────────────────────────────────────────

	[Fact]
	public void Format_AsterisksInMath_NotMangled() {
		// Standalone asterisks (not wrapping text) should pass through
		string result = this._sut.Format("2 * 3 = 6");
		Assert.Equal("2 * 3 = 6", result);
	}

	[Fact]
	public void Format_DashInText_NotConvertedToBullet() {
		// Dashes in the middle of text should not become bullets
		string result = this._sut.Format("well-known fact");
		Assert.Equal("well-known fact", result);
	}
}
