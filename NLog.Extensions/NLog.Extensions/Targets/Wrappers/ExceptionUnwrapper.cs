using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog.Common;

namespace NLog.Targets.Wrappers
{
    [Target("ExceptionUnwrapper", IsWrapper = true)]
    public class ExceptionUnwrapperTarget : WrapperTargetBase
    {
        HashSet<string> unwrapExceptions = new HashSet<string>();
        public string UnwrapExceptions
        {
            get { return string.Join(",", unwrapExceptions); }
            set { unwrapExceptions = new HashSet<string>(value.Split(',').Select(s => s.Trim())); }
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            ProcessLogEvent(logEvent, this.WriteAsyncLogEvent, this.WriteAsyncLogEvents);
        }

        protected override void Write(AsyncLogEventInfo[] logEvents)
        {
            var list = new List<AsyncLogEventInfo>(logEvents.Length);
            foreach (var logEvent in logEvents)
            {
                ProcessLogEvent(logEvent, list.Add, list.AddRange);
            }
            this.WrappedTarget.WriteAsyncLogEvents(list.ToArray());
        }

        private void ProcessLogEvent(AsyncLogEventInfo logEvent, Action<AsyncLogEventInfo> oneItemAction, Action<AsyncLogEventInfo[]> multipleLogEventAction)
        {
            if (logEvent.LogEvent.Exception == null)
            {
                oneItemAction(logEvent);
                return;
            }

            var aggregateException = logEvent.LogEvent.Exception as AggregateException;
            if (aggregateException != null)
            {
                UnwrapAggregateLogEvent(logEvent, aggregateException, oneItemAction, multipleLogEventAction);
                return;
            }

            UnwrapLogEvent(logEvent, oneItemAction);
        }

        private void UnwrapAggregateLogEvent(AsyncLogEventInfo logEvent, AggregateException exception, Action<AsyncLogEventInfo> oneItemAction, Action<AsyncLogEventInfo[]> multipleLogEventAction)
        {
            var innerExceptions = exception.Flatten().InnerExceptions.Select(UnwrapException).ToList();
            var continuation = logEvent.Continuation;
            var countDown = innerExceptions.Count;
            if (countDown <= 1)
            {
                logEvent = CloneEventInfo(logEvent.LogEvent, innerExceptions.FirstOrDefault()).WithContinuation(continuation);
                oneItemAction(logEvent);
            }
            else
            {
                var logEvents = innerExceptions.Select(ex =>
                    CloneEventInfo(logEvent.LogEvent, ex).WithContinuation(ex2 =>
                    {
                        if (Interlocked.Decrement(ref countDown) == 0)
                        {
                            continuation(null);
                        }
                    })).ToArray();
                multipleLogEventAction(logEvents);
            }
        }

        private void UnwrapLogEvent(AsyncLogEventInfo logEvent, Action<AsyncLogEventInfo> oneItemAction)
        {
            var unwrapped = UnwrapException(logEvent.LogEvent.Exception);
            if (!object.ReferenceEquals(unwrapped, logEvent.LogEvent.Exception))
            {
                var continuation = logEvent.Continuation;
                logEvent = CloneEventInfo(logEvent.LogEvent, unwrapped).WithContinuation(continuation);
            }
            oneItemAction(logEvent);            
        }

        private Exception UnwrapException(Exception exception)
        {
            while (unwrapExceptions.Contains(exception.GetType().Name))
            {
                exception = exception.InnerException;
            }
            return exception;
        }

        private static LogEventInfo CloneEventInfo(LogEventInfo original, Exception exception)
        {
            var eventInfo = LogEventInfo.Create(original.Level, original.LoggerName, original.Message, exception);
            foreach (var item in original.Properties)
            {
                eventInfo.Properties.Add(item);
            }
            return eventInfo;
        }
    }
}
