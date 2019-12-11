using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.LanguageServer;

namespace Deltin.Deltinteger.Parse
{
    public class CallMethodAction : IExpression, IStatement
    {
        public IMethod CallingMethod { get; }
        private DeltinScript translateInfo { get; }
        private IExpression[] ParameterValues { get; }

        private DocRange NameRange { get; }

        public CallMethodAction(ScriptFile script, DeltinScript translateInfo, Scope scope, DeltinScriptParser.MethodContext methodContext)
        {
            this.translateInfo = translateInfo;
            string methodName = methodContext.PART().GetText();
            NameRange = DocRange.GetRange(methodContext.PART());
            
            // TODO: Move the signature matching code to a seperate method so that constructors can use it.

            if (methodContext.picky_parameters() == null)
            {
                // Get the parameter values
                DeltinScriptParser.ExprContext[] parameterContexts;

                // Set parameterValues and parameterContexts.
                if (methodContext.call_parameters() == null)
                {
                    // If call_parameters is null, set both as empty.
                    ParameterValues = new IExpression[0];
                    parameterContexts = new DeltinScriptParser.ExprContext[0];
                }
                else
                {
                    // Get the parameter values.
                    parameterContexts = methodContext.call_parameters().expr();
                    ParameterValues = new IExpression[parameterContexts.Length];
                    for (int i = 0; i < ParameterValues.Length; i++)
                        ParameterValues[i] = DeltinScript.GetExpression(script, translateInfo, scope, parameterContexts[i]);
                }

                // Get the best overload via types.
                var overloads = scope.GetMethodsByName(methodName)
                    // Order the list by the number of parameters in each method.
                    .OrderBy(m => m.Parameters.Length)
                    .ToList();
                
                CallingMethod = overloads.OrderBy(m => Math.Abs(ParameterValues.Length - m.Parameters.Length)).FirstOrDefault();

                // Syntax error if there are no methods with the name.
                if (overloads.Count == 0)
                    script.Diagnostics.Error(string.Format("No method by the name of {0} exists in the current context.", methodName), NameRange);
                else
                {
                    // Remove the methods that have less parameters than the number of parameters of the method being called.
                    overloads = overloads.Where(m => m.Parameters.Length >= ParameterValues.Length)
                        .ToList();
                    
                    if (overloads.Count == 0)
                        script.Diagnostics.Error(
                            string.Format("No overloads for the method {0} has {1} parameters.", methodName, ParameterValues.Length),
                            NameRange
                        );
                    else
                    {
                        var methodDiagnostics = new Dictionary<IMethod, List<Diagnostic>>();
                        // Fill methodDiagnostics.
                        foreach (var method in overloads) methodDiagnostics.Add(method, new List<Diagnostic>());

                        // Match by value types and parameter types.
                        for (int i = 0; i < ParameterValues.Length; i++)
                        {
                            // Get the type of the parameter value.
                            var valueType = ParameterValues[i].Type();

                            // Check each method to make sure the parameter matches.
                            foreach (var method in overloads)
                            if (!CodeType.TypeMatches(method.Parameters[i].Type, valueType))
                            {
                                // The parameter type does not match.
                                string msg = string.Format("Expected a value of type {0}.", method.Parameters[i].Type.Name);
                                methodDiagnostics[method].Add(new Diagnostic(msg, DocRange.GetRange(parameterContexts[i]), Diagnostic.Error));
                            }
                        }

                        // If there are any methods with no errors, set that as the chosen method.
                        var methodWithNoErrors = methodDiagnostics.FirstOrDefault(m => m.Value.Count > 0).Key;
                        if (methodWithNoErrors != null) CallingMethod = methodWithNoErrors;

                        // Add the diagnostics of the chosen method.
                        script.Diagnostics.AddDiagnostics(methodDiagnostics[CallingMethod].ToArray());

                        // Get the missing parameters.
                        for (int i = ParameterValues.Length; i < CallingMethod.Parameters.Length; i++)
                        {
                            // TODO: check if there is a default value.
                            // Syntax error if there is no default value.
                            script.Diagnostics.Error(
                                string.Format("Missing the parameter \"{0}\" for method \"{1}\".", CallingMethod.Parameters[i].ToString(), CallingMethod.Name),
                                DocRange.GetRange(methodContext.PART())
                            );
                        }

                        // Get the signature.
                        // (Get overloads instead of just the chosen method)
                        // ParameterRange[] parameterRanges = new ParameterRange[ParameterValues.Length];
                        // for (int i = 0; i < parameterRanges.Length; i++)
                        //     parameterRanges[i] = new ParameterRange(CallingMethod.Parameters[i], i, DocRange.GetRange(parameterContexts[i]));
                        
                        // script.AddSignatureRange(new SignatureRange(CallingMethod, DocRange.GetRange(methodContext), parameterRanges));
                    }
                }
            }
            else
            {
                // todo: this
                throw new NotImplementedException();
            }
        }

        public Scope ReturningScope()
        {
            if (CallingMethod == null) return null;

            if (CallingMethod.ReturnType == null)
                return translateInfo.PlayerVariableScope;
            else
                return CallingMethod.ReturnType.GetObjectScope();
        }

        public CodeType Type() => CallingMethod?.ReturnType;
    
        // IStatement
        public void Translate(ActionSet actionSet)
        {
            CallingMethod.Parse(actionSet.New(NameRange), GetParameterValuesAsWorkshop(actionSet));
        }

        // IExpression
        public IWorkshopTree Parse(ActionSet actionSet)
        {
            return CallingMethod.Parse(actionSet.New(NameRange), GetParameterValuesAsWorkshop(actionSet));
        }

        private IWorkshopTree[] GetParameterValuesAsWorkshop(ActionSet actionSet)
        {
            IWorkshopTree[] parameterValues = new IWorkshopTree[ParameterValues.Length];
            for (int i = 0; i < ParameterValues.Length; i++)
                parameterValues[i] = ParameterValues[i].Parse(actionSet);
            return parameterValues;
        }
    }
}