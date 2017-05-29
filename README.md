NLog.Extensions
===============

Extends ExceptionLayoutRenderer, EventPropertyLayoutRenderer, AsyncTargetWrapper and provides ExceptionUnwrapper

#### Extends ExceptionLayoutRenderer
Normally when `ExceptionLayoutRenderer` renders `Method`, we will get method from top of stack. This extension will search for application method and line number. It will give you better idea where to fix the problem, no need to look into stack trace.

##### usage
All implementations are exactly the same as normal `ExceptionLayoutRenderer`, but `Method` format will search for application method.
```
${exception:format=Method}
```
##### example
This expression will always generate ArgumentNullException if args is null.
```
string.Join(",", args);
```
Normal `ExceptionLayoutRenderer` will return `System.String Join(System.String, System.String[])`, and then you need to look into stack trace to identify the problem. But with extended `ExceptionLayoutRenderer` will return `Program.TestMethod-line27`. So it gives you better idea where to fix the problem.

#### Extends EventPropertyLayoutRenderer
Normal `EventPropertyLayoutRenderer` will only convert property to string, but this one will allow writing path to access member inside that property.

##### usage
All implementations are exactly the same as normal `EventPropertyLayoutRenderer`, but you can write path to access member.
```
${event-properties:Item=key.Prop1.Prop2['test'][0]}
```
- **property access**: for property access just write `.` (dot) follow by property name.
- **indexer access**: for indexer access just write `[` parameter and close by `]`. Currently only single string and integer are allowed as parameter. String identify by `'` (single quote).

You can chain property access and indexer access as long as you want. If one of properties is null, this will return null without create exception.

#### Extends AsyncTargetWrapper
Current `AsyncTargetWrapper` is using normal queue, and handling concurrency by locking. This extension change to ConcurrentQueue to make it non-blocking.

##### usage
All implementation is the same as normal `AsyncTargetWrapper`, but internal queue will use ConcurrentQueue instead of normal queue. And you can add `parallelWrite="true"` in case log target support concurrent write (such as log to html).
```
<target name="test" xsi:type="AsyncWrapper">
    <target xsi:type="Console" layout="${Message}" />
</target>
```
#### ExceptionUnwrapper
This is to unwrap certain kinds of exceptions such as `AggregateException` or your custom exceptions. It is useful if you would like to see real exception type.
##### usage
By default, this will always unwrap `AggregateException` and you can specify your own exceptions to unwrap by `unwrapExceptions="..."`. Value is comma separated.
```
<target name="test" xsi:type="ExceptionUnwrapper" unwrapExceptions="UnhandledException,HttpUnhandleException">
    <target xsi:type="Console" layout="${exception:format=ShortType}" />
</target>

```
