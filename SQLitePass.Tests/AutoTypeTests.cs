using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Tests;

public class AutoTypeTests
{
    private sealed class FakeInput : IAutoTypeInput
    {
        public IntPtr ForegroundWindow { get; set; } = new(123);
        public string Title { get; set; } = "Controlled test target";
        public uint CurrentProcessId { get; set; } = 1234;
        public string WindowTitle(IntPtr window) => Title;
        public bool ActivationSucceeds { get; set; } = true;
        public bool LoseFocusAfterCharacter { get; set; }
        public bool LoseFocusAfterKey { get; set; }
        public bool ChangeProcessAfterCharacter { get; set; }
        public bool ChangeTitleOnActivate { get; set; }
        public int ActivationCount { get; private set; }
        public List<char> Characters { get; } = [];
        public List<ushort> Keys { get; } = [];
        public IReadOnlyList<AutoTypeStep>? PreparedSteps { get; private set; }
        public AutoTypeStep[] PreparedSnapshot { get; private set; } = [];
        public uint ProcessId(IntPtr window) => CurrentProcessId;
        public void Prepare(IReadOnlyList<AutoTypeStep> steps)
        {
            PreparedSteps = steps;
            PreparedSnapshot = steps.ToArray();
        }
        public bool Activate(IntPtr window)
        {
            ActivationCount++;
            if (ChangeTitleOnActivate) Title = "Reused target title";
            return ActivationSucceeds;
        }
        public void Character(char character)
        {
            Characters.Add(character);
            if (LoseFocusAfterCharacter) ForegroundWindow = new(456);
            if (ChangeProcessAfterCharacter) CurrentProcessId++;
        }
        public void Key(ushort key) { Keys.Add(key); if (LoseFocusAfterKey) ForegroundWindow = new(456); }
    }

    [Fact]
    public async Task ZeroTargetNeverTypesIntoForegroundWindow()
    {
        var input = new FakeInput();
        await Assert.ThrowsAsync<InvalidOperationException>(() => AutoTypeService.ExecuteAutoTypeAsync(new PwEntry { Password = "synthetic" }, IntPtr.Zero, 0, input: input));
        Assert.Empty(input.Characters); Assert.Empty(input.Keys);
    }
    [Fact]
    public async Task DisabledEntryNeverTypes()
    {
        var input = new FakeInput();
        await Assert.ThrowsAsync<InvalidOperationException>(() => AutoTypeService.ExecuteAutoTypeAsync(new PwEntry { AutoTypeEnabled = false }, new(123), 0, input: input));
        Assert.Empty(input.Characters);
    }
    [Fact]
    public async Task FocusLossStopsBeforeRemainingCredentials()
    {
        var input = new FakeInput { LoseFocusAfterCharacter = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(new PwEntry { UserName = "alice", Password = "synthetic" }, new(123), 0, input: input));
        Assert.Equal(new[] { 'a' }, input.Characters); Assert.Empty(input.Keys);
        AssertAutoTypeStepsCleared(input);
    }
    [Fact]
    public async Task ProcessChangeAfterInputStopsImmediately()
    {
        var input = new FakeInput { ChangeProcessAfterCharacter = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(
            new PwEntry { UserName = "alice", Password = "synthetic" }, new(123), 0, input: input));
        Assert.Equal(new[] { 'a' }, input.Characters); Assert.Empty(input.Keys);
    }
    [Fact]
    public async Task ReusedWindowHandleWithDifferentProcessIsRejectedBeforeActivation()
    {
        var input = new FakeInput { CurrentProcessId = 5678 };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(
            new PwEntry { Password = "synthetic" }, new(123), 0, input: input, expectedProcessId: 1234));
        Assert.Equal(0, input.ActivationCount); Assert.Empty(input.Characters);
    }
    [Fact]
    public async Task FocusLossAfterFinalCharacterStillCancels()
    {
        var input = new FakeInput { LoseFocusAfterCharacter = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(
            new PwEntry { AutoTypeSequence = "x" }, new(123), 0, input: input));
        Assert.Equal(new[] { 'x' }, input.Characters);
    }
    [Fact]
    public async Task FocusLossAfterKeyStopsBeforeFollowingText()
    {
        var input = new FakeInput { LoseFocusAfterKey = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(
            new PwEntry { AutoTypeSequence = "{TAB}x" }, new(123), 0, input: input));
        Assert.Equal(new ushort[] { 9 }, input.Keys); Assert.Empty(input.Characters);
    }
    [Fact]
    public async Task FocusLossAfterFinalKeyStillCancels()
    {
        var input = new FakeInput { LoseFocusAfterKey = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(
            new PwEntry { AutoTypeSequence = "{TAB}" }, new(123), 0, input: input));
        Assert.Equal(new ushort[] { 9 }, input.Keys);
    }
    [Fact]
    public async Task TitleChangeDuringActivationIsRejected()
    {
        var input = new FakeInput { ChangeTitleOnActivate = true };
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(
            new PwEntry { Password = "synthetic" }, new(123), 0, input: input));
        Assert.Empty(input.Characters);
    }
    [Fact]
    public async Task PasswordBracesAreLiteralCharacters()
    {
        var input = new FakeInput();
        await AutoTypeService.ExecuteAutoTypeAsync(new PwEntry { Password = "{TAB}", AutoTypeSequence = "{PASSWORD}" }, new(123), 0, input: input);
        Assert.Equal("{TAB}", new string(input.Characters.ToArray())); Assert.Empty(input.Keys);
        AssertAutoTypeStepsCleared(input);
    }

    [Fact]
    public async Task AutoTypeParserPreservesCaseInsensitiveCustomFieldsAndDelayTokens()
    {
        var input = new FakeInput();
        var entry = new PwEntry
        {
            AutoTypeEnabled = true,
            UserName = "alice",
            Password = "synthetic-password",
            AutoTypeSequence = "head{uSeRnAmE}{TAB}{S:token}{DELAY 1}{PASSWORD}tail"
        };
        entry.CustomFields.Add(new PwCustomField { Key = "Token", Value = "synthetic-token" });

        await AutoTypeService.ExecuteAutoTypeAsync(entry, new(123), 0, input: input);

        Assert.Equal("headalicesynthetic-tokensynthetic-passwordtail", new string(input.Characters.ToArray()));
        Assert.Equal(new ushort[] { 9 }, input.Keys);
        AssertAutoTypeStepsCleared(input);
    }

    [Fact]
    public void AutoTypeValidationDoesNotEchoAnUnsupportedSequenceIntoErrors()
    {
        var entry = new PwEntry { AutoTypeEnabled = true, AutoTypeSequence = "synthetic-prefix{unsupported-secret}" };
        var error = Assert.Throws<InvalidDataException>(() => AutoTypeService.ValidateSequence(entry));
        Assert.DoesNotContain("unsupported-secret", error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ChangedTitleIsRejectedBeforeTyping()
    {
        var input = new FakeInput();
        await Assert.ThrowsAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(new PwEntry { Password = "synthetic" }, new(123), 0, input: input, expectedTitle: "Another tab"));
        Assert.Empty(input.Characters);
    }
    [Fact]
    public async Task CancelledSessionDoesNotActivateOrType()
    {
        var input = new FakeInput(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AutoTypeService.ExecuteAutoTypeAsync(new PwEntry(), new(123), 0, cancellation.Token, input));
        Assert.Empty(input.Characters);
    }

    private static void AssertAutoTypeStepsCleared(FakeInput input)
    {
        Assert.NotNull(input.PreparedSteps);
        Assert.Empty(input.PreparedSteps);
        Assert.All(input.PreparedSnapshot, step => Assert.Empty(step.Text));
    }
}
