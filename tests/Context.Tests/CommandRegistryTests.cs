using Spectre.Console.Testing;

namespace Context.Tests;

public class CommandRegistryTests
{
    private static CommandResult Nothing(string argument) => CommandResult.None;

    // AC-9
    [Fact]
    public void Register_SameNameTwice_Throws()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/note", "First", Nothing);

        Assert.Throws<ArgumentException>(() => registry.Register("/note", "Second", Nothing));
    }

    // AC-9
    [Fact]
    public void Register_SameNameDifferentCase_Throws()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/note", "First", Nothing);

        Assert.Throws<ArgumentException>(() => registry.Register("/NOTE", "Second", Nothing));
    }

    // AC-9
    [Fact]
    public void Default_HasHelp_DuplicateHelpThrows()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();

        Assert.Throws<ArgumentException>(() => registry.Register("/Help", "Again", Nothing));
    }

    [Theory]
    [InlineData("note")]
    [InlineData("/")]
    [InlineData("/two words")]
    public void Register_InvalidName_Throws(string name)
    {
        CommandRegistry registry = new CommandRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(name, "Bad", Nothing));
    }

    // AC-9
    [Fact]
    public void TryGet_IgnoresCase()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();

        Assert.True(registry.TryGet("/Help", out Command command));
        Assert.Equal("/help", command.Name);
    }

    // AC-9
    [Fact]
    public void TryGet_Unknown_ReturnsFalse()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();

        Assert.False(registry.TryGet("/nope", out _));
    }

    [Fact]
    public void Commands_AreOrderedByName()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/zeta", "Z", Nothing);
        registry.Register("/alpha", "A", Nothing);

        Assert.Equal(["/alpha", "/zeta"], registry.Commands.Select(c => c.Name));
    }

    [Fact]
    public void RenderHelp_ListsEveryCommandWithDescription()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        TestConsole console = new TestConsole();

        console.Write(registry.RenderHelp());

        Assert.Contains("/help", console.Output);
        Assert.Contains("List all commands", console.Output);
        Assert.Contains("/quit", console.Output);
        Assert.Contains("Exit Context", console.Output);
    }
}
