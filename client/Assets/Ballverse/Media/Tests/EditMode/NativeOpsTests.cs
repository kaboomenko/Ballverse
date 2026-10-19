using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ballverse.Media.Tests
{
    /// <remarks>
    /// Unity Test Framework rule these tests live by: an Error / Assert / Exception log received while a test runs — from any
    /// thread — fails that test unless <see cref="LogAssert.Expect(LogType, Regex)"/> matches it. So a test that takes an
    /// intentional error path expects its exact logs, and no test may leave a timer or continuation behind that could log
    /// after it ended (it would fail whichever test runs then): TearDown checks that no cancel fallback timer is still armed
    /// and restores the static timeout.
    /// </remarks>
    public sealed class NativeOpsTests
    {
        MainThreadPump _pump;
        NativeOps _ops;
        List<string> _log;

        [SetUp]
        public void SetUp()
        {
            _pump = new MainThreadPump();
            _ops = new NativeOps();
            _log = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            NativeOps.NativeCancelTimeoutMs = NativeOps.DefaultNativeCancelTimeoutMs;
            _pump.Dispose();
            Assert.AreEqual(0, _ops.ArmedCancelTimerCount, "a cancel fallback timer outlived the test");
        }

        Task<string> Start(CancellationToken ct)
        {
            return _ops.Run(null, ct, id => _log.Add("start " + id), id => _log.Add("cancel " + id), e => new MediaException(MediaErrorCode.Unknown, e.Message, e));
        }

        // QA finding 5: a pre-cancelled token used to call cancelNative(id) before the native start of that id.
        [Test]
        public void PreCancelledToken_DoesNotStartNative()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Task<string> task = Start(cts.Token);
            Assert.IsTrue(task.IsCanceled);
            CollectionAssert.IsEmpty(_log);
            Assert.AreEqual(0, _ops.PendingCount);
        }

        [Test]
        public void CancelAfterStart_CancelsTask_ThenNative()
        {
            var cts = new CancellationTokenSource();
            Task<string> task = Start(cts.Token);
            cts.Cancel();
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            CollectionAssert.AreEqual(new[] { "start 1", "cancel 1" }, _log);
            _ops.Complete(1, NativeStatus.Ok, "{}"); // late native completion is ignored
            _pump.Drain(20);
            Assert.IsTrue(task.IsCanceled);
        }

        [Test]
        public void CancelFromAnotherThread_RunsNativeCancelAfterStart()
        {
            var cts = new CancellationTokenSource();
            Task<string> task = Start(cts.Token);
            var t = new Thread(() => cts.Cancel());
            t.Start();
            t.Join();
            _pump.RunUntil(() => task.IsCompleted);
            CollectionAssert.AreEqual(new[] { "start 1", "cancel 1" }, _log);
        }

        [Test]
        public void TokenCancelledInsideStart_CancelsAfterStartReturns()
        {
            var cts = new CancellationTokenSource();
            Task<string> task = _ops.Run(null, cts.Token, id =>
            {
                _log.Add("start " + id);
                cts.Cancel();
                _log.Add("started " + id);
            }, id => _log.Add("cancel " + id), null);
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            CollectionAssert.AreEqual(new[] { "start 1", "started 1", "cancel 1" }, _log);
        }

        [Test]
        public void NativeCompletion_CompletesOnMainThread()
        {
            Task<string> task = Start(CancellationToken.None);
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var t = new Thread(() => _ops.Complete(1, NativeStatus.Ok, "{\"x\":1}"));
            t.Start();
            t.Join();
            Assert.IsFalse(task.IsCompleted, "completion must be marshalled to the main thread");
            Assert.AreEqual("{\"x\":1}", _pump.Wait(task));
            Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId);
        }

        [Test]
        public void NativeError_FaultsWithMediaException()
        {
            Task<string> task = Start(CancellationToken.None);
            _ops.Complete(1, NativeStatus.Error, "{\"code\":\"DRM_PROTECTED\",\"message\":\"nope\"}");
            _pump.RunUntil(() => task.IsCompleted);
            var e = Assert.Throws<MediaException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual(MediaErrorCode.DrmProtected, e.Code);
        }

        [Test]
        public void StartThrows_FaultsTask_WithoutNativeCancel()
        {
            var cts = new CancellationTokenSource();
            Task<string> task = _ops.Run(null, cts.Token, id => throw new InvalidOperationException("boom"), id => _log.Add("cancel " + id),
                e => new MediaException(MediaErrorCode.EncoderFailed, e.Message, e));
            Assert.IsTrue(task.IsFaulted);
            cts.Cancel();
            _pump.Drain(20);
            CollectionAssert.IsEmpty(_log);
            Assert.AreEqual(MediaErrorCode.EncoderFailed, ((MediaException)task.Exception.InnerException).Code);
        }

        // Writer releases must survive bridge disposal: their completion means "the native writer has stopped".
        [Test]
        public void CancelAll_KeepsKeepOnCancelAllOperations()
        {
            Task<string> normal = Start(CancellationToken.None);
            Task<string> release = _ops.Run(null, CancellationToken.None, id => _log.Add("release " + id), null, null, true);
            _ops.CancelAll();
            Assert.IsTrue(normal.IsCanceled);
            Assert.IsFalse(release.IsCompleted);
            Assert.AreEqual(1, _ops.PendingCount);
            _ops.Complete(2, NativeStatus.Ok, "{}");
            Assert.AreEqual("{}", _pump.Wait(release));
        }

        // QA round 2 (minor): a cancelled segment join returned before the native join stopped, so a retry for the same
        // output could start beside it. Joins now use cancelWaitsForNative: the task completes when native reports.
        Task<string> StartWaiting(CancellationToken ct)
        {
            return _ops.Run(null, ct, id => _log.Add("start " + id), id => _log.Add("cancel " + id),
                e => new MediaException(MediaErrorCode.Unknown, e.Message, e), false, true);
        }

        [Test]
        public void CancelWaitsForNative_PendingUntilNativeReports()
        {
            var cts = new CancellationTokenSource();
            Task<string> task = StartWaiting(cts.Token);
            cts.Cancel();
            _pump.Drain(50);
            CollectionAssert.AreEqual(new[] { "start 1", "cancel 1" }, _log);
            Assert.IsFalse(task.IsCompleted, "completed before the native side stopped");
            Assert.AreEqual(1, _ops.PendingCount);
            Assert.AreEqual(1, _ops.ArmedCancelTimerCount);
            cts.Cancel(); // a second cancel does not forward again
            _pump.Drain(20);
            Assert.AreEqual(2, _log.Count);
            Assert.AreEqual(1, _ops.ArmedCancelTimerCount, "a second cancel must not arm a second timer");
            _ops.Complete(1, NativeStatus.Cancelled, "");
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, _ops.PendingCount);
            Assert.AreEqual(0, _ops.ArmedCancelTimerCount, "the fallback timer must stop when native reports");
        }

        [Test]
        public void CancelWaitsForNative_LateSuccessAfterCancel_IsStillCancelled()
        {
            var cts = new CancellationTokenSource();
            Task<string> task = StartWaiting(cts.Token);
            cts.Cancel();
            _pump.Drain(20);
            _ops.Complete(1, NativeStatus.Ok, "{\"frames\":3}");
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled, "a result reported after the cancel must not be used");
            Assert.AreEqual(0, _ops.ArmedCancelTimerCount);
        }

        [Test]
        public void CancelWaitsForNative_WithoutCancel_CompletesNormally()
        {
            Task<string> task = StartWaiting(new CancellationTokenSource().Token);
            _ops.Complete(1, NativeStatus.Ok, "{}");
            Assert.AreEqual("{}", _pump.Wait(task));
            CollectionAssert.AreEqual(new[] { "start 1" }, _log);
        }

        [Test]
        public void CancelWaitsForNative_CancelAll_ForwardsAndWaits()
        {
            Task<string> task = StartWaiting(CancellationToken.None);
            _ops.CancelAll();
            CollectionAssert.AreEqual(new[] { "start 1", "cancel 1" }, _log);
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(1, _ops.ArmedCancelTimerCount);
            _ops.Complete(1, NativeStatus.Cancelled, "");
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, _ops.ArmedCancelTimerCount);
        }

        // The one test that takes the timeout path on purpose: exactly one error log, expected (UTF fails the test on any
        // unexpected Error log). The timeout is restored in TearDown too.
        [Test]
        public void CancelWaitsForNative_NoNativeReport_GivesUpAfterTimeout()
        {
            NativeOps.NativeCancelTimeoutMs = 100;
            LogAssert.Expect(LogType.Error, new Regex(@"^\[Media\] native operation 1 did not stop within 100 ms of its cancel; continuing$"));
            var cts = new CancellationTokenSource();
            Task<string> task = StartWaiting(cts.Token);
            cts.Cancel();
            _pump.RunUntil(() => task.IsCompleted, 3000);
            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, _ops.PendingCount);
            Assert.AreEqual(0, _ops.ArmedCancelTimerCount);
            _ops.Complete(1, NativeStatus.Cancelled, ""); // a very late report is ignored
            _pump.Drain(20);
        }

        // CI failure (UiShellTests, ~20 s after this fixture): the fallback timer of an operation whose native side reported
        // in time was never stopped; it fired after the test, when no main-thread dispatcher existed any more, and logged an
        // error during an unrelated test. Now the report stops the timer, and nothing of the finished test runs or logs
        // after its dispatcher is gone. The timeout leaves a wide margin (the report arrives within a few ms of the cancel; only
        // a main-thread stall of more than ~1 s, e.g. a GC pause on a loaded CI runner, would let the fallback fire first and
        // log), while the sleep after the report still reaches well past it.
        [Test]
        public void CancelWaitsForNative_ReportInTime_TimerStopped_NothingLogsAfterTeardown()
        {
            NativeOps.NativeCancelTimeoutMs = 1000;
            var cts = new CancellationTokenSource();
            Task<string> task = StartWaiting(cts.Token);
            cts.Cancel();
            _pump.Drain(20);
            Assert.AreEqual(1, _ops.ArmedCancelTimerCount);
            _ops.Complete(1, NativeStatus.Cancelled, "");
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, _ops.ArmedCancelTimerCount, "the fallback timer outlived its operation");
            _pump.Dispose(); // as after the end of a test: no main-thread dispatcher (idempotent; TearDown disposes again)
            Thread.Sleep(1500); // well past the 1000 ms timeout
            LogAssert.NoUnexpectedReceived();
        }

        // A callback that arrives after its dispatcher went away is dropped with a warning, never an error (an error would
        // fail whatever test is running at that moment).
        [Test]
        public void Post_WithoutMainThreadContext_DropsWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Media\] MainThread\.Post without a captured main-thread context"));
            bool ran = false;
            using (MainThread.OverrideForTests(null))
            {
                Assert.IsFalse(MainThread.Post(() => ran = true));
            }
            _pump.Drain(20);
            Assert.IsFalse(ran);
        }
    }
}
