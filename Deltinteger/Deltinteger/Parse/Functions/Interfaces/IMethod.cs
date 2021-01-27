using System;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

namespace Deltin.Deltinteger.Parse
{
    public interface IMethod : IScopeable, IParameterCallable
    {
        MethodAttributes Attributes { get; }
        IMethodInfo MethodInfo { get; }
        IWorkshopTree Parse(ActionSet actionSet, MethodCall methodCall);
        bool DoesReturnValue => CodeType != null;
        CompletionItem IScopeable.GetCompletion(DeltinScript deltinScript) => GetFunctionCompletion(deltinScript, this);
        MarkupBuilder ILabeled.GetLabel(DeltinScript deltinScript, LabelInfo labelInfo) => DefaultLabel(deltinScript, labelInfo, this);

        public static MarkupBuilder DefaultLabel(DeltinScript deltinScript, LabelInfo labelInfo, IMethod function)
        {
            MarkupBuilder markup = new MarkupBuilder().StartCodeLine();
            
            // Add return type.
            if (labelInfo.IncludeReturnType)
                markup.Add(ICodeTypeSolver.GetNameOrVoid(deltinScript, function.CodeType)).Add(" ");
            
            // Add function name and parameters.
            markup.Add(function.Name + CodeParameter.GetLabels(deltinScript, function.Parameters))
                .EndCodeLine();
            
            // Add documentation.
            if (labelInfo.IncludeDocumentation && function.Documentation != null)
                markup.NewSection().Add(function.Documentation);
            
            return markup;
        }

        public static CompletionItem GetFunctionCompletion(DeltinScript deltinScript, IMethod function) => new CompletionItem()
        {
            Label = function.Name,
            Kind = CompletionItemKind.Method,
            Detail = ICodeTypeSolver.GetNameOrVoid(deltinScript, function.CodeType) + " " + function.Name + CodeParameter.GetLabels(deltinScript, function.Parameters),
            Documentation = Extras.GetMarkupContent(function.Documentation)
        };
    }

    public interface IMethodInfo
    {
        AnonymousType[] GenericTypes { get; }
        int TypeArgCount => GenericTypes == null ? 0 : GenericTypes.Length;
        void Override(IMethodProvider overridenBy) => throw new NotImplementedException();
        InstanceAnonymousTypeLinker GetInstanceInfo(CodeType[] typeArgs) => new InstanceAnonymousTypeLinker(GenericTypes, typeArgs);
    }

    public interface IMethodProvider : IMethodInfo
    {
        string Name { get; }
        IMethod GetDefaultInstance();
    }

    class MethodInfo : IMethodInfo
    {
        public AnonymousType[] GenericTypes { get; }

        public MethodInfo()
        {
            GenericTypes = new AnonymousType[0];
        }

        public MethodInfo(AnonymousType[] generics)
        {
            GenericTypes = generics;
        }
    }

    public class FunctionOverrideInfo
    {
        public string Name { get; }
        public CodeType[] ParameterTypes { get; }

        public FunctionOverrideInfo(string name, CodeType[] parameterTypes)
        {
            Name = name;
            ParameterTypes = parameterTypes;
        }
    }
}