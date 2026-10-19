using MoMController;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var directory = Path.Combine(Path.GetTempPath(), "mom-completion-exit-" + Guid.NewGuid());
SdlDualSenseReportTests.Run();
Directory.CreateDirectory(directory);
try
{
    var gate = new CompletionExit();
    var exits = 0;
    var path = Path.Combine(directory, "event.json");
    Action<string> quit = id => {
        Require(id == "clear", "Wrong event caused exit.");
        Require(File.ReadAllText(path) == "published", "Exit preceded publication.");
        exits++;
        gate.Dispatch(_ => throw new Exception("Reentrant quit."));
    };
    gate.Dispatch(quit);
    Require(exits == 0, "Empty queue caused exit.");
    gate.Publish("failed", false, () => File.WriteAllText(path, "failed"));
    gate.Dispatch(quit);
    Require(exits == 0, "Noneligible result caused exit.");

    // A real I/O failure, followed by a successful retry of the same event.
    try
    {
        gate.Publish("clear", true, () => File.WriteAllText(directory, "published"));
        throw new Exception("Expected the write to fail.");
    }
    catch (UnauthorizedAccessException) { }
    gate.Dispatch(quit);
    Require(exits == 0, "Failed publication caused exit.");
    gate.Publish("clear", true, () => File.WriteAllText(path, "published"));
    Require(exits == 0, "Publication quit synchronously.");
    gate.Dispatch(quit);
    Require(exits == 1, "Published clear did not exit.");
    gate.Publish("clear", true, () => File.WriteAllText(path, "published"));
    gate.Dispatch(quit);
    Require(exits == 1, "Duplicate publication caused another exit.");

    var throwingGate = new CompletionExit();
    throwingGate.Publish("clear", true, () => { });
    try { throwingGate.Dispatch(_ => throw new InvalidOperationException("Native quit error")); }
    catch (InvalidOperationException) { }
    throwingGate.Dispatch(_ => throw new Exception("Retried an uncertain native quit."));
    Console.WriteLine("PASS: publication ordering, noneligible event, I/O failure/retry, deferred exit, reentrancy, duplicate event, and quit error.");
}
finally
{
    File.Delete(Path.Combine(directory, "event.json"));
    Directory.Delete(directory);
}
