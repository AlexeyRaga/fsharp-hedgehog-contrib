module Hedgehog.Stateful.Tests.ParallelCounterFixtureClean

(*
    PARALLEL TESTING: Testing Linearizability of Concurrent Operations

    This example demonstrates how parallel state machine testing works.

    WHAT DOES IT TEST?
    -------------------
    Parallel testing verifies that a concurrent data structure (like our ThreadSafeCounter)
    is "linearizable" - meaning that despite operations running in parallel, the observed
    results match what could happen if those operations executed in SOME sequential order.

    HOW DOES IT WORK?
    -----------------
    1. SEQUENTIAL PREFIX: First, a sequence of operations runs sequentially to set up
       some initial state (e.g., counter might be at value 5).

    2. PARALLEL BRANCHES: Then, TWO branches of operations run IN PARALLEL on the SAME SUT.
       For example:
         Thread 1: Increment, Get, Increment
         Thread 2: Decrement, Get
       These run concurrently - they WILL interfere with each other!

    3. LINEARIZABILITY CHECK: After parallel execution, the framework checks if there exists
       ANY valid sequential ordering of all operations that would produce the same results.

       If Branch 1 observed: [6, 6, 7] and Branch 2 observed: [5, 5], the framework checks:
       - Could Increment, Decrement, Get, Increment, Get produce these results?
       - Could Decrement, Increment, Get, Get, Increment produce these results?
       - ... checks all possible interleavings ...

       If ANY interleaving matches, the test passes (operations are linearizable).
       If NO interleaving matches, the test FAILS (there's a concurrency bug).

    WHY DOES THIS WORK?
    -------------------
    The key insight: The same SUT is INTENTIONALLY shared between parallel branches.
    This is not a bug - it's the whole point! We WANT operations to interfere, because
    we're testing whether the SUT can handle concurrent access correctly.

    If the SUT is correctly thread-safe (like ThreadSafeCounter with Interlocked operations),
    all observed results will be linearizable.

    If the SUT has concurrency bugs (e.g., unsynchronized read-modify-write), some test
    runs will observe results that can't be explained by ANY sequential ordering, and the
    test will fail.
*)

open System.Threading.Tasks
open Hedgehog.FSharp
open Hedgehog.Linq
open Hedgehog.Stateful
open Xunit

/// Thread-safe counter for parallel testing
/// This tests whether concurrent operations are linearizable
type ThreadSafeCounter() =
    let mutable value = 0

    member _.Increment() =
        System.Threading.Interlocked.Increment(&value)

    member _.Decrement() =
        System.Threading.Interlocked.Decrement(&value)

    member _.Get() =
        System.Threading.Interlocked.CompareExchange(&value, 0, 0)

type ParallelCounterState = {
    CurrentCount: Var<int>  // Symbolic reference to current count
}

/// Increment command - returns the new value after incrementing
type ParallelIncrementCommand() =
    inherit Command<ThreadSafeCounter, ParallelCounterState, unit, int>()

    override _.Name = "Increment"
    override _.Precondition _ = true
    override _.Execute(counter, _, _, _) = Task.FromResult(counter.Increment())
    override _.Generate _ = Gen.constant ()
    override _.Update(_, _, outputVar) = { CurrentCount = outputVar }

    // Ensure checks the output matches the model prediction.
    // The linearizability checker tries all sequential interleavings
    // and verifies that Ensure holds for at least one of them.
    override _.Ensure(env, oldState, _, _, result) =
        let oldCount = oldState.CurrentCount.Resolve(env)
        result = oldCount + 1

/// Decrement command - returns the new value after decrementing
type ParallelDecrementCommand() =
    inherit Command<ThreadSafeCounter, ParallelCounterState, unit, int>()

    override _.Name = "Decrement"
    override _.Precondition _ = true
    override _.Execute(counter, _, _, _) = Task.FromResult(counter.Decrement())
    override _.Generate _ = Gen.constant ()
    override _.Update(_, _, outputVar) = { CurrentCount = outputVar }

    override _.Ensure(env, oldState, _, _, result) =
        let oldCount = oldState.CurrentCount.Resolve(env)
        result = oldCount - 1

/// Get command - reads the current value
type ParallelGetCommand() =
    inherit Command<ThreadSafeCounter, ParallelCounterState, unit, int>()

    override _.Name = "Get"
    override _.Precondition _ = true
    override _.Execute(counter, _, _, _) = Task.FromResult(counter.Get())
    override _.Generate _ = Gen.constant ()
    override _.Update(_, _, outputVar) = { CurrentCount = outputVar }

    override _.Ensure(env, oldState, _, _, result) =
        let count = oldState.CurrentCount.Resolve(env)
        result = count

/// ParallelSpecification for testing linearizability of ThreadSafeCounter
type ParallelCounterSpec() =
    inherit ParallelSpecification<ThreadSafeCounter, ParallelCounterState>()

    // Initial model state - counter starts at 0
    override _.InitialState = { CurrentCount = Var.symbolic 0 }

    // Generate prefix sequences of 0-3 actions (setup some initial state)
    override _.PrefixRange = Range.linear 0 3

    // Generate branch sequences of 1-5 actions per thread
    override _.BranchRange = Range.linear 1 5

    // Commands that can run in parallel
    override _.Commands = [|
        ParallelIncrementCommand()
        ParallelDecrementCommand()
        ParallelGetCommand()
    |]

[<Fact>]
let ``Parallel counter test - verifies linearizability of concurrent operations``() =
    // Use ToPropertyWith to create a fresh SUT for each test iteration
    // This ensures the SUT state matches the model's initial state
    ParallelCounterSpec().ToPropertyWith(fun () -> ThreadSafeCounter()).Check()
