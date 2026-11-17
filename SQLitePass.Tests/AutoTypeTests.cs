using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Tests;

public class AutoTypeTests
{
    private sealed class FakeInput : IAutoTypeInput
    {
        public IntPtr ForegroundWindow { get; set; } = new(123);
        public string Title { get; set; } = "Controlled test target";
        public string WindowTitle(IntPtr window) => Title;
        public bool ActivationSucceeds { get; set; } = true;
        public bool LoseFocusAfterCharacter { get; set; }
        public List<char> Characters { get; } = [];
        public List<ushort> Keys { get; } = [];
        public bool Activate(IntPtr window) => ActivationSucceeds;
        public void Character(char character) { Characters.Add(character); if (LoseFocusAfterCharacter) ForegroundWindow = new(456); }
        public void Key(ushort key) => Keys.Add(key);
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
    }
    [Fact]
    public async Task PasswordBracesAreLiteralCharacters()
    {
        var input = new FakeInput();
        await AutoTypeService.ExecuteAutoTypeAsync(new PwEntry { Password = "{TAB}", AutoTypeSequence = "{PASSWORD}" }, new(123), 0, input: input);
        Assert.Equal("{TAB}", new string(input.Characters.ToArray())); Assert.Empty(input.Keys);
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
}
