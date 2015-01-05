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
        HashSet<string> shortTypeExceptions = new HashSet<string>();

        public string ShortTypeExceptions
        {
            get { return string.Join(",", shortTypeExceptions); }
            set { shortTypeExceptions = new HashSet<string>(value.Split(',').Select(s => s.Trim())); }
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            if (logEvent.LogEvent.Exception == null)
            {
                this.WrappedTarget.WriteAsyncLogEvent(logEvent);
                return;
            }

            var aggregateException = logEvent.LogEvent.Exception as AggregateException;
            if (aggregateException != null)
            {
                var innerExceptions = aggregateException.Flatten().InnerExceptions.Select(Unwrap).ToList();
                var countDown = innerExceptions.Count;
                var continuation = logEvent.Continuation;
                var logEvents = innerExceptions.Select(ex =>
                    CloneEventInfo(logEvent.LogEvent, ex).WithContinuation(ex2 =>
                    {
                        if (Interlocked.Decrement(ref countDown) == 0)
                        {
                            continuation(null);
                        }
                    })).ToArray();
                this.WrappedTarget.WriteAsyncLogEvents(logEvents);
                return;
            }

            var unwrapped = Unwrap(logEvent.LogEvent.Exception);
            if (!object.ReferenceEquals(unwrapped, logEvent.LogEvent.Exception))
            {
                var continuation = logEvent.Continuation;
                logEvent = CloneEventInfo(logEvent.LogEvent, unwrapped).WithContinuation(continuation);
            }
            this.WrappedTarget.WriteAsyncLogEvent(logEvent);
        }

        private Exception Unwrap(Exception exception)
        {
            while (shortTypeExceptions.Contains(exception.GetType().Name))
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
