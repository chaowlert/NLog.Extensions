using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog.Common;

namespace NLog.Targets.Wrappers
{
    [Target("AsyncWrapper", IsWrapper = true)]
    public class ConcurrentTargetWrapper : WrapperTargetBase
    {
        Timer timer;
        readonly ConcurrentQueue<AsyncLogEventInfo> queue = new ConcurrentQueue<AsyncLogEventInfo>();

        [DefaultValue(100)]
        public int BatchSize { get; set; }

        [DefaultValue(50)]
        public int TimeToSleepBetweenBatches { get; set; }

        [DefaultValue("Discard")]
        public AsyncTargetWrapperOverflowAction OverflowAction { get; set; }

        [DefaultValue(10000)]
        public int QueueLimit { get; set; }

        protected override void CloseTarget()
        {
            if (timer != null)
            {
                timer.Dispose();
            }
            ProcessQueue(null);

            base.CloseTarget();
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            Task.Run(() => ProcessQueue(asyncContinuation));
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
            if (queue.Count >= this.QueueLimit)
            {
                switch (this.OverflowAction)
                {
                    case AsyncTargetWrapperOverflowAction.Grow:
                        queue.Enqueue(logEvent);
                        break;
                    case AsyncTargetWrapperOverflowAction.Discard:
                        break;
                    case AsyncTargetWrapperOverflowAction.Block:
                        ProcessQueue(null);
                        queue.Enqueue(logEvent);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                queue.Enqueue(logEvent);
            }
        }

        private void ProcessQueue(object state)
        {
            var asyncContinuation = state as AsyncContinuation;

            try
            {
                var logEvents = BatchDequeue().ToArray();

                if (logEvents.Length == 0)
                {
                    if (asyncContinuation != null)
                    {
                        asyncContinuation(null);
                    }
                }
                else 
                {
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

        private IEnumerable<AsyncLogEventInfo> BatchDequeue()
        {
            AsyncLogEventInfo logEvent;
            for (int i = this.QueueLimit; i > 0 && queue.TryDequeue(out logEvent); i--)
            {
                yield return logEvent;
            }
        }  
    }
}
