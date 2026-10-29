namespace Tests.Common;

/// <summary>
/// Holds each directory call until a set number of them are waiting together, then lets that group through.
///
/// It is how a test proves calls overlap without timing anything. Code that really runs <c>parties</c> calls at
/// once fills every group immediately; code that makes them one at a time never brings a second caller, so its
/// first call gives up after <c>patience</c> with a message saying how many arrived. Passing therefore does not
/// depend on how fast the machine is — only the failure path waits on a clock.
///
/// See TestGraphContext.HoldCallsUntilInFlight.
/// </summary>
public sealed class GraphCallGate(int parties, TimeSpan patience)
{
    private readonly object _sync = new();

    private TaskCompletionSource _group = NewGroup();

    // Callers waiting in the group that has not been released yet. A caller that leaves without being released
    // takes itself back out, so the count is only ever of calls that are in flight right now.
    private int _waiting;

    // How many had arrived when a group first ran out of patience; null until one does.
    private int? _brokenAt;

    private int _groupsReleased;

    /// <summary>
    /// Gets how many full groups have gone through.
    /// </summary>
    public int GroupsReleased
    {
        get
        {
            lock (_sync)
            {
                return _groupsReleased;
            }
        }
    }

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        Task group;
        lock (_sync)
        {
            // One group that never filled has already answered the question, so the rest fail at once rather
            // than each waiting out its own patience.
            if (_brokenAt is { } arrived)
            {
                throw NeverFilled(arrived);
            }

            group = _group.Task;
            if (++_waiting == parties)
            {
                _waiting = 0;
                _groupsReleased++;
                _group.SetResult();
                _group = NewGroup();
            }
        }

        try
        {
            await group.WaitAsync(patience, cancellationToken);
        }
        catch (TimeoutException)
        {
            lock (_sync)
            {
                // The group filled as this caller's patience ran out. It was released with the rest, so it goes
                // through: the calls did overlap, and failing here would report a slow machine as a broken pool.
                if (group.IsCompleted)
                {
                    return;
                }

                _brokenAt ??= _waiting;
                _waiting--;
                throw NeverFilled(_brokenAt.Value);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled caller is no longer in flight. Left in the count, callers that came and went one at a
            // time would add up to a "group" and be reported as having overlapped.
            lock (_sync)
            {
                if (!group.IsCompleted)
                {
                    _waiting--;
                }
            }

            throw;
        }
    }

    // Continuations run asynchronously so releasing a group never runs the callers' code under the lock.
    private static TaskCompletionSource NewGroup() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TimeoutException NeverFilled(int arrived) => new(
        $"Expected {parties} directory calls in flight together, but only {arrived} arrived within {patience.TotalSeconds:0}s.");
}