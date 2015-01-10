using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NLog.LayoutRenderers
{
    [LayoutRenderer("exception")]
    public class ExceptionExLayoutRenderer : ExceptionLayoutRenderer
    {
        protected override void AppendMethod(StringBuilder sb, Exception ex)
        {
            var match = Regex.Match(ex.StackTrace ?? string.Empty, @"^\s*at (?<method>[^(]+).*line (?<linenum>\d+)\r?$", RegexOptions.Multiline);
            if (match.Success)
            {
                sb.Append(match.Groups["method"].Value)
                  .Append("-line").Append(match.Groups["linenum"].Value);
            }
            else
            {
                var target = ex.TargetSite;
                if (target != null)
                {
                    if (target.DeclaringType != null)
                    {
                        sb.Append(target.DeclaringType).Append(".");
                    }
                    sb.Append(target.Name);
                }
            }
        }
    }
}
