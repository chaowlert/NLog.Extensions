using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using NLog.Common;
using NLog.Config;

namespace NLog.LayoutRenderers
{
    [LayoutRenderer("event-properties")]
    public class EventPropertiesExLayoutRenderer : LayoutRenderer
    {
        [RequiredParameter]
        [DefaultParameter]
        public string Item { get; set; }

        string property;
        readonly List<string> segments = new List<string>(); 
        protected override void InitializeLayoutRenderer()
        {
            var input = Item;
            bool success = false;
            while (!success)
            {
                var match = Regex.Match(input, "^(?<main>\\.?\\w+|\\['(''|[^'])*']|\\[\\d+])(?<more>.*)$", RegexOptions.Compiled);
                if (!match.Success)
                {
                    break;
                }
                var main = match.Groups["main"].Value;
                if (property == null)
                {
                    property = main;
                }
                else
                {
                    if (main[0] != '.' && main[0] != '[')
                    {
                        break;
                    }
                    segments.Add(main);
                }
                input = match.Groups["more"].Value;
                success = string.IsNullOrEmpty(input);
            }
            if (!success)
            {
                InternalLogger.Warn("EventPropertiesLayoutRenderer cannot parse {0}", this.Item);
                property = this.Item;
            }
            else
            {
                dict = new ConcurrentDictionary<int, Tuple<int, Func<object, object>>>();
            }
        }

        ConcurrentDictionary<int, Tuple<int, Func<object, object>>> dict; 
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            object value;

            if (!logEvent.Properties.TryGetValue(property, out value))
            {
                return;
            }

            try
            {
                int index = 0;
                while (index < segments.Count && value != null)
                {
                    var tuple = dict.GetOrAdd(index, i => Compile(i, value.GetType()));
                    index = tuple.Item1;
                    value = tuple.Item2(value);
                }

                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("EventPropertiesLayoutRenderer fail evaluating {0}: {1}", this.Item, ex);
            }
        }

        Tuple<int, Func<object, object>> Compile(int start, Type type)
        {
            var p = Expression.Parameter(typeof(object));
            Expression exp = Expression.Convert(p, type);

            //access each segment
            int i;
            for (i = start; i < segments.Count; i++)
            {
                var next = Access(exp, segments[i]);
                if (next == null)
                {
                    break;
                }
                exp = next;
            }

            if (i == start)
            {
                throw new InvalidOperationException("Cannot evaluate " + this.Item);
            }
            exp = Expression.Convert(exp, typeof(object));
            var func = Expression.Lambda<Func<object, object>>(exp, p).Compile();
            return Tuple.Create(i, func);
        }

        private static Expression Access(Expression exp, string segment)
        {
            //property access
            if (segment.StartsWith("."))
            {
                segment = segment.Substring(1);
                var propInfo = exp.Type.GetProperty(segment);
                if (propInfo == null)
                {
                    return null;
                }
                return Expression.Property(exp, propInfo);
            }

            //indexer access
            if (segment.StartsWith("[")) 
            {
                segment = segment.Substring(1, segment.Length - 2);
                if (exp.Type.IsArray)
                {
                    var index = int.Parse(segment);
                    return Expression.ArrayIndex(exp, Expression.Constant(index));
                }
                else
                {
                    bool isInt = true;
                    if (segment.StartsWith("'"))
                    {
                        segment = segment.Substring(1, segment.Length - 2);
                        segment = segment.Replace("''", "'");
                        isInt = false;
                    }
                    var propInfo = exp.Type.GetProperty("Item", new[] {isInt ? typeof(int) : typeof(string)});
                    if (propInfo == null)
                    {
                        return null;
                    }
                    return Expression.Property(exp, propInfo, Expression.Constant(isInt ? (object)int.Parse(segment) : segment));
                }
            }

            return null;
        }
    }
}
