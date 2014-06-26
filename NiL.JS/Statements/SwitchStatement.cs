﻿using System;
using System.Collections.Generic;
using System.Linq;
using NiL.JS.Core;

namespace NiL.JS.Statements
{
    [Serializable]
    public sealed class SwitchCase
    {
        internal int index;
        internal CodeNode statement;

        public int Index { get { return index; } }
        public CodeNode Statement { get { return statement; } }
    }

    [Serializable]
    public sealed class SwitchStatement : CodeNode
    {
        private FunctionStatement[] functions;
        private int length;
        private readonly CodeNode[] body;
        private SwitchCase[] cases;
        private CodeNode image;

        public FunctionStatement[] Functions { get { return functions; } }
        public CodeNode[] Body { get { return body; } }
        public SwitchCase[] Cases { get { return cases; } }
        public CodeNode Image { get { return image; } }

        internal SwitchStatement(CodeNode[] body)
        {
            this.body = body;
            length = body.Length - 1;
        }

        internal static ParseResult Parse(ParsingState state, ref int index)
        {
            string code = state.Code;
            int i = index;
            if (!Parser.Validate(code, "switch (", ref i) && !Parser.Validate(code, "switch(", ref i))
                return new ParseResult();
            while (char.IsWhiteSpace(code[i])) i++;
            var image = ExpressionStatement.Parse(state, ref i).Statement;
            if (code[i] != ')')
                throw new JSException(TypeProxy.Proxy(new Core.BaseTypes.SyntaxError("Expected \")\" at + " + Tools.PositionToTextcord(code, i))));
            do i++; while (char.IsWhiteSpace(code[i]));
            if (code[i] != '{')
                throw new JSException(TypeProxy.Proxy(new Core.BaseTypes.SyntaxError("Expected \"{\" at + " + Tools.PositionToTextcord(code, i))));
            do i++; while (char.IsWhiteSpace(code[i]));
            var body = new List<CodeNode>();
            var funcs = new List<FunctionStatement>();
            var cases = new List<SwitchCase>();
            cases.Add(null);
            state.AllowBreak++;
            while (code[i] != '}')
            {
                do
                {
                    if (Parser.Validate(code, "case", i) && Parser.isIdentificatorTerminator(code[i + 4]))
                    {
                        i += 4;
                        while (char.IsWhiteSpace(code[i])) i++;
                        var sample = ExpressionStatement.Parse(state, ref i).Statement;
                        if (code[i] != ':')
                            throw new JSException(TypeProxy.Proxy(new Core.BaseTypes.SyntaxError("Expected \":\" at + " + Tools.PositionToTextcord(code, i))));
                        i++;
                        cases.Add(new SwitchCase() { index = body.Count, statement = sample });
                    }
                    else if (Parser.Validate(code, "default", i) && Parser.isIdentificatorTerminator(code[i + 7]))
                    {
                        if (cases[0] != null)
                            throw new JSException(TypeProxy.Proxy(new Core.BaseTypes.SyntaxError("Duplicate default case in switch at " + Tools.PositionToTextcord(code, i))));
                        i += 7;
                        if (code[i] != ':')
                            throw new JSException(TypeProxy.Proxy(new Core.BaseTypes.SyntaxError("Expected \":\" at + " + Tools.PositionToTextcord(code, i))));
                        i++;
                        cases[0] = new SwitchCase() { index = body.Count, statement = null };
                    }
                    else break;
                    while (char.IsWhiteSpace(code[i]) || (code[i] == ';')) i++;
                } while (true);
                if (cases.Count == 1 && cases[0] == null)
                    throw new JSException(TypeProxy.Proxy(new Core.BaseTypes.SyntaxError("Switch statement must be contain cases. " + Tools.PositionToTextcord(code, index))));
                var t = Parser.Parse(state, ref i, 0);
                if (t == null)
                    continue;
                if (t is FunctionStatement)
                {
                    if (state.strict.Peek())
                        throw new JSException(TypeProxy.Proxy(new NiL.JS.Core.BaseTypes.SyntaxError("In strict mode code, functions can only be declared at top level or immediately within another function.")));
                    funcs.Add(t as FunctionStatement);
                }
                else
                    body.Add(t);
                while (char.IsWhiteSpace(code[i]) || (code[i] == ';')) i++;
            }
            state.AllowBreak--;
            i++;
            var pos = index;
            index = i;
            return new ParseResult()
            {
                IsParsed = true,
                Statement = new SwitchStatement(body.ToArray())
                {
                    functions = funcs.ToArray(),
                    cases = cases.ToArray(),
                    image = image,
                    Position = pos,
                    Length = index - pos
                }
            };
        }

        internal override JSObject Invoke(Context context)
        {
            if (functions != null)
                throw new InvalidOperationException();
            int i = cases[0] != null ? cases[0].index : length + 1;
            var imageVal = image.Invoke(context);
            for (int j = 1; j < cases.Length; j++)
            {
#if DEV
                if (context.debugging)
                    context.raiseDebugger(cases[j].statement);
#endif
                if (Expressions.StrictEqual.Check(imageVal, cases[j].statement, context))
                {
                    i = cases[j].index;
                    break;
                }
            }
            while (i-- > 0)
            {
                body[i].Invoke(context);
                if (context.abort != AbortType.None)
                {
                    if (context.abort == AbortType.Break)
                        context.abort = AbortType.None;
                    return context.abortInfo;
                }
            }
            return JSObject.undefined;
        }

        internal override bool Optimize(ref CodeNode _this, int depth, int fdepth, Dictionary<string, VariableDescriptor> variables, bool strict)
        {
            if (depth < 1)
                throw new InvalidOperationException();
            Parser.Optimize(ref image, 2, fdepth, variables, strict);
            for (int i = 0; i < body.Length; i++)
                Parser.Optimize(ref body[i], 1, fdepth, variables, strict);
            for (int i = 0; i < functions.Length; i++)
            {
                CodeNode stat = functions[i];
                Parser.Optimize(ref stat, 1, fdepth, variables, strict);
                variables[functions[i].Name] = new VariableDescriptor(functions[i].Reference, true, fdepth);
            }
            functions = null;
            for (int i = 1; i < cases.Length; i++)
                Parser.Optimize(ref cases[i].statement, 2, fdepth, variables, strict);
            for (int i = 0; i < body.Length / 2; i++)
            {
                var t = body[i];
                body[i] = body[body.Length - 1 - i];
                body[body.Length - 1 - i] = t;
            }
            for (int i = cases[0] != null ? 0 : 1; i < cases.Length; i++)
                cases[i].index = body.Length - cases[i].index;
            return false;
        }

        protected override CodeNode[] getChildsImpl()
        {
            var res = new List<CodeNode>()
            {
                image
            };
            res.AddRange(body);
            res.AddRange(functions);
            res.AddRange(from c in cases select c.statement);
            res.RemoveAll(x => x == null);
            return res.ToArray();
        }

        public override string ToString()
        {
            string res = "switch (" + image + ") {" + Environment.NewLine;
            var replp = Environment.NewLine;
            var replt = Environment.NewLine + "  ";
            for (int i = body.Length; i-- > 0; )
            {
                for (int j = 0; j < cases.Length; j++)
                {
                    if (cases[j] != null && cases[j].index == i)
                    {
                        res += "case " + (cases[j].statement as Expressions.StrictEqual).SecondOperand + ":" + Environment.NewLine;
                    }
                }
                string lc = body[i].ToString().Replace(replp, replt);
                res += "  " + lc + (lc[lc.Length - 1] != '}' ? ";" + Environment.NewLine : Environment.NewLine);
            }
            if (functions != null)
                for (var i = 0; i < functions.Length; i++)
                {
                    var func = functions[i].ToString().Replace(replp, replt);
                    res += "  " + func + Environment.NewLine;
                }
            return res + "}";
        }
    }
}