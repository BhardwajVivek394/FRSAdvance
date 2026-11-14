using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeDerived -- formulas for derived card conditions (new in 2.0).
    //
    // CHANGE HERE to add or fix how a derived value is computed from live data.
    //
    // A formula is text such as "IBALST(mA) = ITC FEED END(mA) - ITC RELAY END(mA)" (the card's own
    // "formula" field) or one from judge-settings.json Live.Formulas. The part before "=" is the
    // result name and is ignored; the right side is parsed: operands are attribute names (unit in
    // brackets is dropped) or numbers; + - * / and ( ). Each operand is a step series fetched from
    // live data, and the formula is evaluated as a step series too: at every instant where any
    // operand changes, the result changes.
    // ============================================================================================
    internal static class JudgeDerived
    {
        internal abstract class Node
        {
            public abstract double? Eval(Func<string, double?> operand);

            public abstract void Names(List<string> into);
        }

        private sealed class NumNode : Node
        {
            public double Value;

            public override double? Eval(Func<string, double?> operand)
            {
                return Value;
            }

            public override void Names(List<string> into)
            {
            }
        }

        private sealed class NameNode : Node
        {
            public string Name;

            public override double? Eval(Func<string, double?> operand)
            {
                return operand(Name);
            }

            public override void Names(List<string> into)
            {
                if (!into.Contains(Name, StringComparer.OrdinalIgnoreCase))
                {
                    into.Add(Name);
                }
            }
        }

        private sealed class BinNode : Node
        {
            public char Op;
            public Node Left;
            public Node Right;

            public override double? Eval(Func<string, double?> operand)
            {
                double? l = Left.Eval(operand);
                double? r = Right.Eval(operand);
                if (l == null || r == null)
                {
                    return null;
                }

                switch (Op)
                {
                    case '+':
                        return l + r;
                    case '-':
                        return l - r;
                    case '*':
                        return l * r;
                    case '/':
                        if (Math.Abs(r.Value) < 1e-12)
                        {
                            return null;
                        }

                        return l / r;
                    default:
                        return null;
                }
            }

            public override void Names(List<string> into)
            {
                Left.Names(into);
                Right.Names(into);
            }
        }

        private sealed class NegNode : Node
        {
            public Node Inner;

            public override double? Eval(Func<string, double?> operand)
            {
                double? v = Inner.Eval(operand);
                return v == null ? (double?)null : -v;
            }

            public override void Names(List<string> into)
            {
                Inner.Names(into);
            }
        }

        /// <summary>Parses "X = A - B" or "A - B". Returns null (with a reason) when the text is not a formula.</summary>
        internal static Node Parse(string formula, out string error)
        {
            error = null;
            string f = (formula ?? "").Trim();
            if (f.Length == 0)
            {
                error = "empty formula";
                return null;
            }

            int eq = f.IndexOf('=');
            if (eq >= 0)
            {
                f = f.Substring(eq + 1).Trim();
            }

            try
            {
                Parser p = new Parser(f);
                Node n = p.Expr();
                p.SkipWs();
                if (!p.End)
                {
                    error = "unexpected '" + p.Rest() + "'";
                    return null;
                }

                return n;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        internal static List<string> OperandNames(Node n)
        {
            List<string> names = new List<string>();
            n.Names(names);
            return names;
        }

        /// <summary>
        /// Evaluates the formula as a step series over [from, to]: the carried-forward state at
        /// `from`, then a new value at every operand transition inside the window.
        /// </summary>
        internal static JudgeStepSeries EvalSeries(Node n, Dictionary<string, JudgeStepSeries> operands, DateTimeOffset from, DateTimeOffset to, string name)
        {
            JudgeStepSeries result = new JudgeStepSeries();
            result.Attribute = name;
            result.StateSource = "formula";
            Func<DateTimeOffset, double?> at = t => n.Eval(op =>
            {
                JudgeStepSeries s;
                if (!operands.TryGetValue(op, out s) || s == null)
                {
                    return null;
                }

                return s.At(t);
            });

            double? v0 = at(from);
            if (v0 != null)
            {
                result.StateAtStart = v0;
                result.StateAtStartTs = from;
            }

            SortedSet<DateTimeOffset> times = new SortedSet<DateTimeOffset>();
            foreach (JudgeStepSeries s in operands.Values)
            {
                if (s == null)
                {
                    continue;
                }

                foreach (JudgeSample t in s.Transitions)
                {
                    if (t.Ts > from && t.Ts <= to)
                    {
                        times.Add(t.Ts);
                    }
                }
            }

            foreach (DateTimeOffset t in times)
            {
                double? v = at(t);
                if (v != null)
                {
                    result.Transitions.Add(new JudgeSample(t, v.Value));
                }
            }

            return result;
        }

        // ------------------------------------------------------------------------------------------
        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text)
            {
                s = text;
            }

            public bool End
            {
                get
                {
                    return i >= s.Length;
                }
            }

            public string Rest()
            {
                return s.Substring(Math.Min(i, s.Length));
            }

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    i++;
                }
            }

            public Node Expr()
            {
                Node left = Term();
                while (true)
                {
                    SkipWs();
                    if (i < s.Length && (s[i] == '+' || s[i] == '-' || s[i] == '−'))
                    {
                        char op = s[i] == '−' ? '-' : s[i];
                        i++;
                        Node right = Term();
                        BinNode b = new BinNode();
                        b.Op = op;
                        b.Left = left;
                        b.Right = right;
                        left = b;
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private Node Term()
            {
                Node left = Factor();
                while (true)
                {
                    SkipWs();
                    if (i < s.Length && (s[i] == '*' || s[i] == '/' || s[i] == '×' || s[i] == '·'))
                    {
                        char op = s[i] == '/' ? '/' : '*';
                        i++;
                        Node right = Factor();
                        BinNode b = new BinNode();
                        b.Op = op;
                        b.Left = left;
                        b.Right = right;
                        left = b;
                    }
                    else
                    {
                        return left;
                    }
                }
            }

            private Node Factor()
            {
                SkipWs();
                if (i >= s.Length)
                {
                    throw new FormatException("operand expected");
                }

                if (s[i] == '(')
                {
                    i++;
                    Node inner = Expr();
                    SkipWs();
                    if (i >= s.Length || s[i] != ')')
                    {
                        throw new FormatException("')' expected");
                    }

                    i++;
                    return inner;
                }

                if (s[i] == '-' || s[i] == '−')
                {
                    i++;
                    NegNode neg = new NegNode();
                    neg.Inner = Factor();
                    return neg;
                }

                int start = i;
                if (char.IsDigit(s[i]) || s[i] == '.')
                {
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
                    {
                        i++;
                    }

                    NumNode num = new NumNode();
                    num.Value = double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
                    return num;
                }

                // a name: letters, digits, spaces, / and a trailing "(unit)"
                while (i < s.Length)
                {
                    char ch = s[i];
                    if (ch == '+' || ch == '-' || ch == '−' || ch == '*' || ch == '/' || ch == '×' || ch == '·' || ch == ')' || ch == '=')
                    {
                        // '/' inside a name such as "TFC I/P" -- allowed when the next char is a letter and the previous is a letter
                        if (ch == '/' && i > start && i + 1 < s.Length && char.IsLetter(s[i - 1]) && char.IsLetter(s[i + 1]))
                        {
                            i++;
                            continue;
                        }

                        break;
                    }

                    if (ch == '(')
                    {
                        int close = s.IndexOf(')', i);
                        if (close < 0)
                        {
                            throw new FormatException("')' expected after unit");
                        }

                        i = close + 1;
                        break;
                    }

                    i++;
                }

                string name = JudgeUtil.StripUnit(s.Substring(start, i - start)).Trim();
                if (name.Length == 0)
                {
                    throw new FormatException("operand expected at " + start);
                }

                NameNode nn = new NameNode();
                nn.Name = name;
                return nn;
            }
        }
    }
}
