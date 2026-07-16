using Syltr.Engine.Spike;

namespace Syltr.Tests.Engine.Spike;

public class ProfileIsolationProbeResultTests
{
    [Fact]
    public void ConfirmsDistinctCurrentAndPreviousValues()
    {
        var result = ProfileIsolationProbeResult.Evaluate(
        [
            new("a", "a:old", "a:new", "a:new"),
            new("b", "b:old", "b:new", "b:new"),
            new("c", "c:old", "c:new", "c:new")
        ]);

        Assert.True(result.CurrentRunIsIsolated);
        Assert.True(result.PersistenceWasDetected);
    }

    [Fact]
    public void FirstRunConfirmsIsolationButNotPersistence()
    {
        var result = ProfileIsolationProbeResult.Evaluate(
        [
            new("a", null, "a:new", "a:new"),
            new("b", null, "b:new", "b:new")
        ]);

        Assert.True(result.CurrentRunIsIsolated);
        Assert.False(result.PersistenceWasDetected);
    }

    [Fact]
    public void SharedReadBackValueFailsIsolation()
    {
        var result = ProfileIsolationProbeResult.Evaluate(
        [
            new("a", null, "a:new", "b:new"),
            new("b", null, "b:new", "b:new")
        ]);

        Assert.False(result.CurrentRunIsIsolated);
    }

    [Fact]
    public void SharedPreviousValueDoesNotProvePersistenceIsolation()
    {
        var result = ProfileIsolationProbeResult.Evaluate(
        [
            new("a", "shared", "a:new", "a:new"),
            new("b", "shared", "b:new", "b:new")
        ]);

        Assert.True(result.CurrentRunIsIsolated);
        Assert.False(result.PersistenceWasDetected);
    }
}
