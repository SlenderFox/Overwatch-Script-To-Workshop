using System;
using System.Collections.Generic;
using Deltin.Deltinteger.Parse.FunctionBuilder;
using Deltin.Deltinteger.Parse.Lambda;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.Compiler;
using Deltin.Deltinteger.Elements;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

namespace Deltin.Deltinteger.Parse
{
    public class MethodGroup : IVariableInstance, IVariable
    {
        public string Name { get; }
        public MarkupBuilder Documentation { get; }
        public bool WholeContext => true;
        public Location DefinedAt => null; // Doesn't matter.
        public AccessLevel AccessLevel => AccessLevel.Public; // Doesn't matter.
        public ICodeTypeSolver CodeType => null;
        public List<IMethod> Functions { get; } = new List<IMethod>();
        IVariable IVariableInstance.Provider => this;
        VariableType IVariable.VariableType => VariableType.ElementReference;

        public MethodGroup(string name)
        {
            Name = name;
        }

        public bool MethodIsValid(IMethod method) => method.Name == Name;
        public void AddMethod(IMethod method) => Functions.Add(method);

        public CompletionItem GetCompletion(DeltinScript deltinScript) => new CompletionItem()
        {
            Label = Name,
            Kind = CompletionItemKind.Function,
            Documentation = new MarkupBuilder()
                .StartCodeLine()
                .Add(
                    Functions[0].GetLabel(deltinScript, LabelInfo.SignatureOverload) + (Functions.Count == 1 ? "" : " (+" + (Functions.Count - 1) + " overloads)")
                ).EndCodeLine().ToMarkup()
        };

        public IGettableAssigner GetAssigner() => throw new NotImplementedException();

        ICallVariable IVariableInstance.GetExpression(ParseInfo parseInfo, DocRange callRange, IExpression[] index, CodeType[] typeArgs)
            => new CallMethodGroup(parseInfo, callRange, this, typeArgs);

        IVariableInstance IVariable.GetInstance(InstanceAnonymousTypeLinker genericsLinker) => this;
        IVariableInstance IVariable.GetDefaultInstance() => this;
        IScopeable IElementProvider.AddInstance(IScopeAppender scopeHandler, InstanceAnonymousTypeLinker genericsLinker) => throw new NotImplementedException();
        void IElementProvider.AddDefaultInstance(IScopeAppender scopeAppender) => throw new NotImplementedException();
    }

    public class CallMethodGroup : ICallVariable, IExpression, ILambdaApplier, ILambdaInvocable, IWorkshopTree
    {
        public MethodGroup Group { get; }
        public CodeType[] TypeArgs { get; }
        private readonly ParseInfo _parseInfo;
        private readonly DocRange _range;
        private PortableLambdaType _type = new PortableLambdaType(LambdaKind.Anonymous);
        private IMethod _chosenFunction;
        private int _identifier;
        private IMethodGroupInvoker _functionInvoker;
        public CallInfo CallInfo => (_chosenFunction as IApplyBlock)?.CallInfo;
        public IRecursiveCallHandler RecursiveCallHandler => CallInfo?.Function;
        public bool ResolvedSource => _chosenFunction != null;
        public IBridgeInvocable[] InvokedState { get; private set; }

        public CallMethodGroup(ParseInfo parseInfo, DocRange range, MethodGroup group, CodeType[] typeArgs)
        {
            _parseInfo = parseInfo;
            _range = range;
            Group = group;
            TypeArgs = typeArgs;
        }

        public void Accept()
        {
            _parseInfo.Script.AddToken(_range, SemanticTokenType.Function);

            if (_parseInfo.ResolveInvokeInfo != null)
                _parseInfo.ResolveInvokeInfo.Resolve(new MethodGroupInvokeInfo());
            else
                new CheckLambdaContext(
                    _parseInfo,
                    this,
                    "Cannot determine lambda in the current context",
                    _range,
                    ParameterState.Unknown
                ).Check();
        }

        public void GetLambdaStatement(PortableLambdaType expecting)
        {
            bool found = false;
            _type = expecting;
            foreach (var func in Group.Functions)
            {
                if (func.Parameters.Length == expecting.Parameters.Length)
                {
                    // Make sure the method implements the target lambda.
                    for (int i = 0; i < func.Parameters.Length; i++)
                    {
                        var parameterType = func.Parameters[i].GetCodeType(_parseInfo.TranslateInfo);
                        
                        if (parameterType != null && parameterType.Implements(expecting.Parameters[i]))
                            continue;
                    }

                    _chosenFunction = func;
                    found = true;
                    break;
                }
            }

            // If a compatible function was found, get the handler.
            if (found)
            {
                _functionInvoker = GetLambdaHandler(_chosenFunction);
                if (!_type.IsConstant())
                    _identifier = _functionInvoker.GetIdentifier(_parseInfo);

                // Get the variable's invoke info from the parameters.
                InvokedState = new IBridgeInvocable[_functionInvoker.ParameterCount()];
                for (int i = 0; i < _functionInvoker.ParameterCount(); i++)
                    if (_functionInvoker.GetParameterVar(i) is Var var)
                        InvokedState[i] = var.BridgeInvocable;
            }
            else
                _parseInfo.Script.Diagnostics.Error("No overload for '" + Group.Name + "' implements " + expecting.GetName(), _range);
        }

        public void GetLambdaStatement() => _parseInfo.Script.Diagnostics.Error("Cannot determine method group in the current context. Did you intend to invoke the method?", _range);

        private static IMethodGroupInvoker GetLambdaHandler(IMethod function)
        {
            // If the chosen function is a DefinedMethod, use the DefinedFunctionHandler.
            if (function is DefinedMethodInstance definedMethod)
                return new FunctionMethodGroupInvoker(new DefinedFunctionHandler(definedMethod, false));
            
            // If the chosen function is a macro.
            if (function is DefinedMacroInstance definedMacro)
                return new MacroMethodGroupInvoker(definedMacro);

            // Otherwise, use the generic function handler.
            return new FunctionMethodGroupInvoker(new GenericMethodHandler(function));
        }

        public IWorkshopTree Parse(ActionSet actionSet)
        {
            if (_type.IsConstant())
                return this;
            return Element.CreateArray(Element.Num(_identifier), actionSet.This ?? Element.Null());
        }

        public IWorkshopTree Invoke(ActionSet actionSet, params IWorkshopTree[] parameterValues) => _functionInvoker.Invoke(actionSet, parameterValues);

        public MarkupBuilder GetLabel(DeltinScript deltinScript, LabelInfo labelInfo) => _chosenFunction.GetLabel(deltinScript, labelInfo);
        public Scope ReturningScope() => null;
        public CodeType Type() => _type;
        
        public void ToWorkshop(WorkshopBuilder builder, ToWorkshopContext context) => throw new NotImplementedException();
        public bool EqualTo(IWorkshopTree other) => throw new NotImplementedException();
    }

    interface IMethodGroupInvoker
    {
        int ParameterCount();
        IVariable GetParameterVar(int index);
        int GetIdentifier(ParseInfo parseInfo) => -1;
        IWorkshopTree Invoke(ActionSet actionSet, params IWorkshopTree[] parameterValues);
    }

    class FunctionMethodGroupInvoker : IMethodGroupInvoker
    {
        private readonly IFunctionHandler _functionHandler;

        public FunctionMethodGroupInvoker(IFunctionHandler functionHandler)
        {
            _functionHandler = functionHandler;
        }

        public int GetIdentifier(ParseInfo parseInfo) => parseInfo.TranslateInfo.GetComponent<LambdaGroup>().Add(_functionHandler);
        public IVariable GetParameterVar(int index) => _functionHandler.GetParameterVar(index).Provider;
        public int ParameterCount() => _functionHandler.ParameterCount();

        public IWorkshopTree Invoke(ActionSet actionSet, params IWorkshopTree[] parameterValues)
        {
            var buildController = new FunctionBuildController(actionSet, new CallHandler(parameterValues), new DefaultGroupDeterminer(new IFunctionHandler[] { _functionHandler }));
            return buildController.Build();
        }
    }

    class MacroMethodGroupInvoker : IMethodGroupInvoker
    {
        private readonly DefinedMacroInstance _macro;

        public MacroMethodGroupInvoker(DefinedMacroInstance macro)
        {
            _macro = macro;
        }

        public IVariable GetParameterVar(int index) => _macro.ParameterVars[index].Provider;
        public int ParameterCount() => _macro.Parameters.Length;
        public IWorkshopTree Invoke(ActionSet actionSet, params IWorkshopTree[] parameterValues) => _macro.Parse(actionSet, new MethodCall(parameterValues));
    }

    class GenericMethodHandler : IFunctionHandler
    {
        public CodeType ContainingType => null;
        private readonly IMethod _method;
        private readonly IVariableInstance[] _parameterSavers;

        public GenericMethodHandler(IMethod method)
        {
            _method = method;

            _parameterSavers = new IVariableInstance[_method.Parameters.Length];
            for (int i = 0; i < _parameterSavers.Length; i++)
                _parameterSavers[i] = new InternalVar(_method.Parameters[i].Name);
        }

        public string GetName() => _method.Name;
        public bool DoesReturnValue() => _method.DoesReturnValue;
        public IVariableInstance GetParameterVar(int index) => _parameterSavers[index];
        public SubroutineInfo GetSubroutineInfo() => throw new NotImplementedException();
        public bool IsObject() => false;
        public bool IsRecursive() => false;
        public bool IsSubroutine() => false;
        public bool MultiplePaths() => false;
        public int ParameterCount() => _method.Parameters.Length;

        public void ParseInner(ActionSet actionSet)
        {
            var parameterValues = new IWorkshopTree[_parameterSavers.Length];
            for (int i = 0; i < _parameterSavers.Length; i++)   
                parameterValues[i] = actionSet.IndexAssigner[_parameterSavers[i].Provider].GetVariable();

            var result = _method.Parse(actionSet, new MethodCall(parameterValues, new object[parameterValues.Length]));
            if (_method.DoesReturnValue)
                actionSet.ReturnHandler.ReturnValue(result);
        }
        public object UniqueIdentifier() => _method;
    }
}