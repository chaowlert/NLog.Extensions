NLog.Extensions
===============

Extends ExceptionLayoutRenderer AsyncTargetWrapper and provides ExceptionUnwrapper

####Extends ExceptionLayoutRenderer
Normally when `ExceptionLayoutRenderer` renders `Method`, we will get method from top of stack. This extension will search for application method and line number. It is useful for grouping exceptions.

#####usage
All implementations are exactly the same as `ExceptionLayoutRenderer`. Adding `findApplicationMethod=true` to make `Method` searching for application method.
```
${exception:format=Method:findApplicationMethod=true}
```
#####example
This main will always generate ArgumentNullException
```
string.Join(",", null);
```
if we do not set `findApplicationMethod=true`, we will get
```
System.String Join(System.String, System.String[])
```
But if we set `findApplicationMethod=true`, we will get
```
Program.TestMethod-line27
```
####Extends AsyncTargetWrapper
Current `AsyncTargetWrapper` is using normal queue, and handle concurrency by locking. This extension change to ConcurrentQueue, therefore it is non-blocking.

#####usage
All implementation is the same as `AsyncTargetWrapper`, but internal queue will use ConcurrentQueue instead of normal queue. And you can add `parallelWrite="true"` in case log target support concurrent write (such as log to html).
```
<target name="test" xsi:type="AsyncWrapper">
    <target xsi:type="Console" layout="${Message}" />
</target>
```
####ExceptionUnwrapper
This is to unwrap certain kinds of exceptions such as `AggregateException` or your custom exceptions. It is useful for grouping exception type and you would like to see real exception type.
#####usage
By default, this will always unwrap `AggregateException` and you can specify your own exceptions to unwrap by `unwrapExceptions="..."`. Value is comma separated.
```
<target name="test" xsi:type="ExceptionUnwrapper" unwrapExceptions="UnhandledException,HttpUnhandleException">
    <target xsi:type="Console" layout="${exception:format=ShortType}" />
</target>

```
