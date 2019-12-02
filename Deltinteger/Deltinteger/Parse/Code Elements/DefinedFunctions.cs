using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.WorkshopWiki;

namespace Deltin.Deltinteger.Parse
{
    public abstract class DefinedFunction : IMethod
    {
        public string ScopeableType { get; } = "method";
        public string Name { get; }
        public CodeType ReturnType { get; protected set; }
        public Parameter[] Parameters { get; private set; }
        public AccessLevel AccessLevel { get; protected set; }
        public Location DefinedAt { get; }

        private DeltinScript translateInfo { get; }
        protected Scope methodScope { get; }

        public WikiMethod Wiki { get; }
        
        public DefinedFunction(DeltinScript translateInfo, string name, Location definedAt)
        {
            Name = name;
            DefinedAt = definedAt;
            this.translateInfo = translateInfo;
            methodScope = translateInfo.GlobalScope.Child();
        }

        protected static CodeType GetCodeType(ScriptFile script, DeltinScript translateInfo, string name, DocRange range)
        {
            if (name == null)
                return null;
            else
                return translateInfo.GetCodeType(name, script.Diagnostics, range);
        }

        protected void SetupParameters(ScriptFile script, DeltinScriptParser.SetParametersContext context)
        {
            if (context == null) 
            {
                Parameters = new Parameter[0];
                return;
            }

            Parameters = new Parameter[context.define().Length];
            for (int i = 0; i < context.define().Length; i++)
            {
                var newVar = Var.CreateVarFromContext(VariableDefineType.Parameter, script, translateInfo, context.define(i));
                newVar.Finalize(methodScope);
                Parameters[i] = new Parameter(context.define(i).name.Text, newVar.CodeType);
            }
        }
    }

    public class DefinedMethod : DefinedFunction
    {
        public bool IsRecursive { get; private set; }
        private BlockAction block { get; set; }

        public DefinedMethod(ScriptFile script, DeltinScript translateInfo, DeltinScriptParser.Define_methodContext context)
            : base(translateInfo, context.name.Text, new Location(script.File, DocRange.GetRange(context)))
        {
            IsRecursive = context.RECURSIVE() != null;
            ReturnType = GetCodeType(script, translateInfo, context.type.Text, DocRange.GetRange(context.type));
            AccessLevel = context.accessor().GetAccessLevel();
            SetupParameters(script, context.setParameters());
            block = new BlockAction(script, translateInfo, methodScope, context.block());
        }
    }

    public class DefinedMacro : DefinedFunction
    {
        public IExpression Expression { get; private set; }

        public DefinedMacro(ScriptFile script, DeltinScript translateInfo, DeltinScriptParser.Define_macroContext context)
            : base(translateInfo, context.name.Text, new Location(script.File, DocRange.GetRange(context)))
        {
            AccessLevel = context.accessor().GetAccessLevel();
            SetupParameters(script, context.setParameters());

            if (context.expr() == null)
                script.Diagnostics.Error("Expected expression.", DocRange.GetRange(context.TERNARY_ELSE()));
            else
            {
                Expression = CodeAction.GetExpression(script, translateInfo, methodScope, context.expr());
                if (Expression != null)
                    ReturnType = Expression.Type();
            }
        }
    }
}