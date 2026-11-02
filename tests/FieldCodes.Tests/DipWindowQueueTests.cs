namespace FieldCodes.Tests;

/// <summary>
/// The Dip Builder window never touches the drawing itself: each click queues work that runs as
/// its own FTFDIPACT command. While a command was in progress -- most often one of the window's
/// own picks ("Select structure point...", "Connects to...", placing a label) still waiting in the
/// drawing -- every other click used to be refused with "Finish or cancel the command in progress
/// (FTFDIPACT) first". The window then kept showing what was typed (a diameter of 48, a pipe
/// marked outside the survey) although nothing was saved. Now the change waits and is saved, in
/// order, once the drawing is free; and the window says what the drawing is waiting for, with a
/// button that cancels that prompt.
///
/// The AutoCAD side cannot run here, so these pin the code that does it.
/// </summary>
public sealed class DipWindowQueueTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Repo() }.Concat(parts).ToArray()));

    [Fact]
    public void AChangeMadeWhileTheDrawingIsBusyIsKeptNotRefused()
    {
        var session = Read("src", "FieldCodes.Cad", "UtilityCommands.cs");
        Assert.DoesNotContain("Finish or cancel the command in progress", session);

        // Queued first, then either sent now or deferred -- never dropped.
        var post = session.IndexOf("public static bool Post(string name, DipWork work, bool fromOwnCommand)", StringComparison.Ordinal);
        Assert.True(post > 0);
        var enqueue = session.IndexOf("Pending.Enqueue(", post, StringComparison.Ordinal);
        var busy = session.IndexOf("!string.IsNullOrEmpty(doc.CommandInProgress)", post, StringComparison.Ordinal);
        Assert.True(enqueue > post && enqueue < busy, "the change is queued before the busy check");
        Assert.Contains("_deferred++;", session);
        Assert.Contains("AcadApp.Idle += OnIdle;", session);
    }

    [Fact]
    public void DeferredChangesRunOnlyWhenTheirDrawingIsFree()
    {
        var session = Read("src", "FieldCodes.Cad", "UtilityCommands.cs");
        // Typing FTFDIPACT into another command's prompt is what the old refusal prevented.
        Assert.Contains("if (doc == null || doc != _deferredDoc || !string.IsNullOrEmpty(doc.CommandInProgress)) return;", session);
        Assert.Contains("for (var i = 0; i < count; i++) doc.SendStringToExecute(\"FTFDIPACT \", true, false, true);", session);
    }

    [Fact]
    public void TheWindowSaysWhatTheDrawingIsWaitingForAndCanCancelIt()
    {
        var session = Read("src", "FieldCodes.Cad", "UtilityCommands.cs");
        Assert.Contains("Active = name;", session);
        Assert.Contains("finally { Active = null; }", session);

        var form = Read("src", "FieldCodes.Cad", "Ui", "DipBuilderForm.cs");
        Assert.Contains("public void ShowDrawingState()", form);
        Assert.Contains("_cancelPrompt = Btn(\"Cancel prompt\", (s, e) => DipSession.CancelActivePrompt());", form);
        // "Connects to..." waits for a pick in the drawing; it now says so.
        Assert.Contains("Pick the structure point this pipe runs to in the drawing", form);
    }
}
