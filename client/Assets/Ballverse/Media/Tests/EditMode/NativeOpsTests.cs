using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;
using NUnit.Framework;

namespace Ballverse.Media.Tests
{
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
            _pump.Dispose();
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
    }
}
