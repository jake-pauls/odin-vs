using OdinVS.Core;

namespace OdinVS.Core.Tests;

// Timing tests: the suite runs methods in parallel, and sleeping neighbours starve the thread pool the tracker waits on.
[TestClass]
[DoNotParallelize]
public sealed class OdinCollectionTrackerTests
{
    private static readonly TimeSpan s_Quiet = TimeSpan.FromMilliseconds(300);

    private static OdinCollectionTracker NewTracker(out List<string> log)
    {
        List<string> messages = [];
        log = messages;
        return new OdinCollectionTracker(s_Quiet, TimeSpan.FromSeconds(2), message => messages.Add(message));
    }

    // Runs `act`, waits until `expected` events arrived (at most 10 s, the machine may be busy), then a few quiet periods more so an
    // unexpected extra event is counted too.
    private static int CountEvents(OdinCollectionTracker tracker, Action act, int expected, bool problems = false)
    {
        int count = 0;
        if (problems)
        {
            tracker.ProblemsChanged += () => Interlocked.Increment(ref count);
        }
        else
        {
            tracker.CollectionsChanged += () => Interlocked.Increment(ref count);
        }

        act();
        SpinWait.SpinUntil(() => Volatile.Read(ref count) >= expected, TimeSpan.FromSeconds(10));
        Thread.Sleep(s_Quiet * 3);
        return Volatile.Read(ref count);
    }

    [TestMethod]
    public void ManyChangesInAQuickBurstRaiseOneChange()
    {
        OdinCollectionTracker tracker = NewTracker(out _);
        int count = CountEvents(tracker, () =>
        {
            for (int i = 0; i < 6; i++)
            {
                tracker.Set(@"C:\s\P" + i + ".odinproj", "P" + i, @"C:\s\P" + i);
                Thread.Sleep(10);
            }
        }, expected: 1);
        Assert.AreEqual(1, count);
        Assert.HasCount(6, tracker.Current().Collections);
    }

    [TestMethod]
    public void ASetThatMatchesWhatWasHandedOutRaisesNothing()
    {
        OdinCollectionTracker tracker = NewTracker(out _);
        tracker.Set(@"C:\s\A.odinproj", "A", @"C:\s\A");
        tracker.Current();
        Assert.AreEqual(0, CountEvents(tracker, () => tracker.Set(@"C:\s\A.odinproj", "A", @"C:\s\A"), expected: 0));
    }

    [TestMethod]
    public void RemovingAProjectRaisesAChangeWithoutIt()
    {
        OdinCollectionTracker tracker = NewTracker(out _);
        tracker.Set(@"C:\s\A.odinproj", "A", @"C:\s\A");
        tracker.Set(@"C:\s\B.odinproj", "B", @"C:\s\B");
        tracker.Current();
        Assert.AreEqual(1, CountEvents(tracker, () => tracker.Remove(@"C:\s\B.odinproj"), expected: 1));
        CollectionAssert.AreEqual(new[] { "A" }, tracker.Current().Collections.Select(c => c.Key).ToArray());
    }

    [TestMethod]
    public void ACollisionRaisesProblemsChangedAndClearsWhenFixed()
    {
        OdinCollectionTracker tracker = NewTracker(out _);
        tracker.Set(@"C:\s\A.odinproj", "A", @"C:\s\A");
        tracker.Current();
        Assert.AreEqual(1, CountEvents(tracker, () => tracker.Set(@"C:\s\B.odinproj", "A", @"C:\s\B"), expected: 1, problems: true));
        Assert.AreEqual(OdinDiagnosticCodes.DuplicateCollectionName, tracker.Problems.Single().Code);

        Assert.AreEqual(1, CountEvents(tracker, () => tracker.Set(@"C:\s\B.odinproj", "B", @"C:\s\B"), expected: 1, problems: true));
        Assert.IsEmpty(tracker.Problems);
    }

    [TestMethod]
    public void ContinuousChangesStillPublishAfterTheCap()
    {
        List<string> messages = [];
        OdinCollectionTracker tracker = new(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(600), messages.Add);
        int count = 0;
        tracker.CollectionsChanged += () => Interlocked.Increment(ref count);
        for (int i = 0; i < 15; i++)
        {
            tracker.Set(@"C:\s\P" + i + ".odinproj", "P" + i, @"C:\s\P" + i);
            Thread.Sleep(100);
        }

        SpinWait.SpinUntil(() => Volatile.Read(ref count) >= 1, TimeSpan.FromSeconds(10));
        Assert.IsGreaterThanOrEqualTo(1, Volatile.Read(ref count), "a change arrived every 100 ms for 1.5 s and the quiet period is 1 s, so only the cap (600 ms) can have published");
    }
}
