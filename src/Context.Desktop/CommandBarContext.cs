namespace Context;

/// <summary>The answer to one command bar line: whether it worked, and the one-line text to show.</summary>
internal sealed class CommandBarResult
{
    public CommandBarResult(bool ok, string text)
    {
        Ok = ok;
        Text = text;
    }

    public bool Ok { get; }

    public string Text { get; }

    public static CommandBarResult Done(string text) => new CommandBarResult(true, text);

    public static CommandBarResult Error(string text) => new CommandBarResult(false, text);

    public override string ToString() => nameof(CommandBarResult);
}

/// <summary>
/// The hook through which the command bar runs Context's own lines (job 282). A singleton: <c>Main</c> registers
/// its runner when it starts and clears it when it is disposed, so the bar never reaches a page that is gone.
/// </summary>
internal sealed class CommandBarContext
{
    public const string NotReadyText = "Context isn't ready yet.";

    private readonly object gate = new object();
    private Func<string, Task<CommandBarResult>>? runner;

    /// <summary>Sets the runner; a later call replaces it.</summary>
    public void Register(Func<string, Task<CommandBarResult>> run)
    {
        lock (gate)
        {
            runner = run;
        }
    }

    /// <summary>Removes the runner, only when it is still <paramref name="run"/>.</summary>
    public void Unregister(Func<string, Task<CommandBarResult>> run)
    {
        lock (gate)
        {
            if (ReferenceEquals(runner, run))
            {
                runner = null;
            }
        }
    }

    /// <summary>Runs the line through the registered runner, or answers <see cref="NotReadyText"/>; never throws.</summary>
    public async Task<CommandBarResult> Run(string line)
    {
        Func<string, Task<CommandBarResult>>? run;
        lock (gate)
        {
            run = runner;
        }

        if (run is null)
        {
            return CommandBarResult.Error(NotReadyText);
        }

        try
        {
            return await run(line).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return CommandBarResult.Error(NotReadyText);
        }
    }
}
