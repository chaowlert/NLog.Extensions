using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            var label = Expression.Label();
            var vars = new List<ParameterExpression>();
            var result = Expression.Variable(typeof(object));
            vars.Add(result);
            var blocks = new List<Expression>
            {
                Expression.Assign(result, Expression.Constant(null, typeof(object))),
            };

            var v = Expression.Variable(type);
            vars.Add(v);
            blocks.Add(Expression.Assign(v, Expression.Convert(p, type)));

            //access each segment
            int i;
            for (i = start; i < segments.Count; i++)
            {
                var block = Access(v, segments[i], label);
                if (block == null)
                {
                    break;
                }
                v = block.Variables.Single();
                vars.Add(v);
                blocks.AddRange(block.Expressions);
                if (IsNullable(v.Type))
                {
                    blocks.Add(
                        Expression.IfThen(
                            Expression.Equal(v, Expression.Constant(null, v.Type)),
                            Expression.Goto(label)));
                }
            }

            if (i == start)
            {
                throw new InvalidOperationException("Cannot evaluate " + this.Item);
            }

            blocks.Add(Expression.Assign(result, Expression.Convert(v, typeof(object))));
            blocks.Add(Expression.Label(label));
            blocks.Add(result);
            var body = Expression.Block(vars, blocks);
            var func = Expression.Lambda<Func<object, object>>(body, p).Compile();
            return Tuple.Create(i, func);
        }

        private static BlockExpression AccessProperty(Expression exp, string segment)
        {
            var propInfo = exp.Type.GetProperty(segment);
            if (propInfo == null)
            {
                return null;
            }
            var v = Expression.Variable(propInfo.PropertyType);
            return Expression.Block(
                new[] { v }, 
                Expression.Assign(v, Expression.Property(exp, propInfo)));            
        }

        private static bool IsNullable(Type type)
        {
            if (!type.IsValueType)
            {
                return true;
            }
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        private static bool IsList(Type type)
        {
            if (!type.IsInterface)
            {
                return false;
            }
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>);
        }

        private static bool IsDictionary(Type type)
        {
            if (!type.IsInterface)
            {
                return false;
            }
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>);
        }

        private static BlockExpression AccessArray(Expression exp, int index, LabelTarget label)
        {
            var c = Expression.Constant(index);
            var value = Expression.ArrayIndex(exp, c);
            var v = Expression.Variable(value.Type);
            var lines = new List<Expression>
            {
                Expression.IfThen(
                    Expression.GreaterThanOrEqual(c, Expression.ArrayLength(exp)),
                    Expression.Goto(label)),
                Expression.Assign(v, value),
            };
            return Expression.Block(new[] { v }, lines);            
        }

        private static BlockExpression AccessList(Expression exp, int index, LabelTarget label)
        {
            var c = Expression.Constant(index);
            var item = exp.Type.GetProperty("Item");
            var value = Expression.Property(exp, item, c);
            var count = exp.Type.GetProperty("Count");
            var v = Expression.Variable(value.Type);

            if (count == null || item == null)
            {
                return null;
            }

            return Expression.Block(
                new[] { v },
                Expression.IfThen(
                    Expression.GreaterThanOrEqual(c, Expression.Property(exp, count)),
                    Expression.Goto(label)),
                Expression.Assign(v, value));
        }

        private static BlockExpression AccessDictionary<T>(Expression exp, T key, LabelTarget label)
        {
            var c = Expression.Constant(key);
            var containsKey = exp.Type.GetMethod("ContainsKey");
            var item = exp.Type.GetProperty("Item");
            var value = Expression.Property(exp, item, c);
            var v = Expression.Variable(value.Type);

            if (containsKey == null || item == null)
            {
                return null;
            }

            return Expression.Block(
                new[] { v },
                Expression.IfThen(
                    Expression.Not(Expression.Call(exp, containsKey, c)),
                    Expression.Goto(label)),
                Expression.Assign(v, value));
        }

        private static BlockExpression Access(Expression exp, string segment, LabelTarget label)
        {
            //property access
            if (segment.StartsWith("."))
            {
                segment = segment.Substring(1);
                return AccessProperty(exp, segment);
            }

            //indexer access
            if (segment.StartsWith("["))
            {
                segment = segment.Substring(1, segment.Length - 2);

                //array
                if (exp.Type.IsArray)
                {
                    var index = int.Parse(segment);
                    return AccessArray(exp, index, label);
                }

                //list
                if (exp.Type.GetInterfaces().Any(IsList))
                {
                    var index = int.Parse(segment);
                    return AccessList(exp, index, label);
                }
                
                //dictionary
                if (exp.Type.GetInterfaces().Any(IsDictionary))
                {
                    bool isInt = true;
                    if (segment.StartsWith("'"))
                    {
                        segment = segment.Substring(1, segment.Length - 2);
                        segment = segment.Replace("''", "'");
                        isInt = false;
                    }
                    return isInt
                        ? AccessDictionary(exp, int.Parse(segment), label)
                        : AccessDictionary(exp, segment, label);
                }
            }

            return null;
        }
    }
}
