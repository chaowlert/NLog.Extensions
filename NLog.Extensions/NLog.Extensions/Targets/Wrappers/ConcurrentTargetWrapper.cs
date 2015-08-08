using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using NLog.Common;

namespace NLog.Targets.Wrappers
{
    [Target("AsyncWrapper", IsWrapper = true)]
    public class ConcurrentTargetWrapper : WrapperTargetBase
    {
        Timer timer;
        readonly ConcurrentQueue<AsyncLogEventInfo> eventLogQueue = new ConcurrentQueue<AsyncLogEventInfo>();
        readonly ConcurrentQueue<AsyncContinuation> continuationQueue = new ConcurrentQueue<AsyncContinuation>();

        [DefaultValue(100)]
        public int BatchSize { get; set; }

        [DefaultValue(50)]
        public int TimeToSleepBetweenBatches { get; set; }

        [DefaultValue("Discard")]
        public AsyncTargetWrapperOverflowAction OverflowAction { get; set; }

        [DefaultValue(10000)]
        public int QueueLimit { get; set; }

        public bool ParallelWrite { get; set; }

        public ConcurrentTargetWrapper() : this(null) { }
        public ConcurrentTargetWrapper(Target wrappedTarget)
            : this(wrappedTarget, 10000, AsyncTargetWrapperOverflowAction.Discard) { }
        public ConcurrentTargetWrapper(Target wrappedTarget, int queueLimit, AsyncTargetWrapperOverflowAction overflowAction)
        {
            this.WrappedTarget = wrappedTarget;
            this.BatchSize = 100;
            this.TimeToSleepBetweenBatches = 50;
            this.QueueLimit = queueLimit;
            this.OverflowAction = overflowAction;
        }

        protected override void CloseTarget()
        {
            timer?.Dispose();
            ProcessQueue(null);

            base.CloseTarget();
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            continuationQueue.Enqueue(asyncContinuation);
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            timer = new Timer(ProcessQueue, null, this.TimeToSleepBetweenBatches, this.TimeToSleepBetweenBatches);
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            this.MergeEventProperties(logEvent.LogEvent);
            this.PrecalculateVolatileLayouts(logEvent.LogEvent);
            if (eventLogQueue.Count >= this.QueueLimit)
            {
                switch (this.OverflowAction)
                {
                    case AsyncTargetWrapperOverflowAction.Grow:
                        eventLogQueue.Enqueue(logEvent);
                        break;
                    case AsyncTargetWrapperOverflowAction.Discard:
                        break;
                    case AsyncTargetWrapperOverflowAction.Block:
                        using (var waitHandler = new ManualResetEventSlim())
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            continuationQueue.Enqueue(ex => waitHandler.Set());
                            waitHandler.Wait();
                        }
                        eventLogQueue.Enqueue(logEvent);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                eventLogQueue.Enqueue(logEvent);
            }
        }

        int processingFlag;
        int processingCount;
        private void SequentialProcessQueue()
        {
            Interlocked.Increment(ref processingCount);
            if (Interlocked.CompareExchange(ref processingFlag, 1, 0) == 1)
            {
                return;
            }

            do
            {
                ProcessQueueOnce();
            } while (Interlocked.Decrement(ref processingCount) > 0);

            Interlocked.Exchange(ref processingFlag, 0);
        }

        private void ProcessQueue(object state)
        {
            if (this.ParallelWrite)
            {
                ProcessQueueOnce();
            }
            else
            {
                SequentialProcessQueue();
            }
        }

        private void ProcessQueueOnce()
        {
            try
            {
                var asyncContinuation = GetAsyncContinuation();

                if (eventLogQueue.Count == 0)
                {
                    asyncContinuation?.Invoke(null);
                }
                else
                {
                    var logEvents = BatchDequeue(eventLogQueue, this.BatchSize).ToArray();
                    if (asyncContinuation != null)
                    {
                        var countDown = logEvents.Length;
                        logEvents = logEvents.Select(logEvent =>
                        {
                            var continuation = logEvent.Continuation;
                            return logEvent.LogEvent.WithContinuation(ex =>
                            {
                                continuation(ex);
                                if (Interlocked.Decrement(ref countDown) == 0)
                                {
                                    asyncContinuation(null);
                                }
                            });
                        }).ToArray();
                    }
                    this.WrappedTarget.WriteAsyncLogEvents(logEvents);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Error in ConcurrentTargetWrapper.ProcessQueue: {0}", ex);
            }
        }

        private AsyncContinuation GetAsyncContinuation()
        {
            int count = continuationQueue.Count;
            if (count == 0)
            {
                return null;
            }

            var continuations = BatchDequeue(continuationQueue, count);
            return continuations.Aggregate(default(AsyncContinuation), (a, b) => a + SafeWrap(b));
        }

        private static AsyncContinuation SafeWrap(AsyncContinuation continuation)
        {
            return ex =>
            {
                try
                {
                    continuation(ex);
                }
                catch (Exception ex2)
                {
                    InternalLogger.Error("Error when process continuation: {0}", ex2);
                }
            };
        }

        private static IEnumerable<T> BatchDequeue<T>(ConcurrentQueue<T> queue, int dequeueCount)
        {
            T item;
            for (int i = dequeueCount; i > 0 && queue.TryDequeue(out item); i--)
            {
                yield return item;
            }
        }  
    }
}
