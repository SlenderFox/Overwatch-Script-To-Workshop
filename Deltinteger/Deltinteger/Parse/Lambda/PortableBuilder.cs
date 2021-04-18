using System;
using System.Linq;
using System.Collections.Generic;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.Parse.Functions.Builder;
using Deltin.Deltinteger.Parse.Functions.Builder.User;

namespace Deltin.Deltinteger.Parse.Lambda
{
    // TODO: Either switch this to IWorkshopComponent or split it into 2.
    class LambdaGroup : IComponent, IWorkshopFunctionController
    {
        readonly List<LambdaAction> _lambdas = new List<LambdaAction>();
        readonly Dictionary<object, int> _identifiers = new Dictionary<object, int>();
        DeltinScript _deltinScript;
        int _parameterCount;
        int _functionIdentifier = 0;
        RecycleWorkshopVariableAssigner _recycler;

        public void Init(DeltinScript deltinScript)
        {
            _deltinScript = deltinScript;
            _recycler = new RecycleWorkshopVariableAssigner(_deltinScript.VarCollection);
        }

        public int Add(LambdaAction lambda)
        {
            if (_identifiers.TryGetValue(lambda, out int existingIdentifier))
                return existingIdentifier;

            _functionIdentifier++;
            _identifiers.Add(lambda, _functionIdentifier);

            // Update the parameter count.
            _parameterCount = Math.Max(_parameterCount, lambda.Parameters.Length);

            // Add the function handler.
            _lambdas.Add(lambda);
            return _functionIdentifier;
        }

        public IWorkshopTree Call(ActionSet actionSet, ICallInfo call)
        {
            return WorkshopFunctionBuilder.Call(actionSet, call, this);
        }

        WorkshopFunctionControllerAttributes IWorkshopFunctionController.Attributes { get; } = new WorkshopFunctionControllerAttributes() {
            IsInstance = true,
            IsRecursive = true,
            RecursiveRequiresObjectStack = true
        };

        ReturnHandler IWorkshopFunctionController.GetReturnHandler(ActionSet actionSet) => new ReturnHandler(actionSet, "func group", null, false);
        SubroutineCatalogItem IWorkshopFunctionController.GetSubroutine() => _deltinScript.GetComponent<SubroutineCatalog>().GetSubroutine(this, () =>
            new SubroutineBuilder(_deltinScript, new SubroutineContext() {
                Controller = this,
                ElementName = "func group", ObjectStackName = "func group", RuleName = "lambda",
                VariableGlobalDefault = true
            }).SetupSubroutine()
        );
        object IWorkshopFunctionController.StackIdentifier() => this;

        void IWorkshopFunctionController.Build(ActionSet actionSet)
        {
            // Create the switch that chooses the lambda.
            SwitchBuilder lambdaSwitch = new SwitchBuilder(actionSet);

            foreach (var option in _lambdas)
            {
                // The action set for the overload.
                ActionSet optionSet = actionSet.New(actionSet.IndexAssigner.CreateContained());

                // Go to next case
                lambdaSwitch.NextCase(Element.Num(_identifiers[option]));

                // Add the object variables of the selected method.
                var callerObject = ((Element)optionSet.CurrentObject)[1];

                // Add the class objects.
                option.This?.AddObjectVariablesToAssigner(optionSet.ToWorkshop, callerObject, optionSet.IndexAssigner);

                // then parse the block.
                option.Statement.Translate(optionSet.SetThis(callerObject).New(actionSet.CurrentObject));
                // Create a new contained action set.
                //     actionSet = actionSet.New(actionSet.IndexAssigner.CreateContained());

                //     var infoSaver = actionSet.VarCollection.Assign("funcSaver", true, false);
                //     actionSet.AddAction(infoSaver.SetVariable((Element)actionSet.CurrentObject));

                //     actionSet = actionSet.New(infoSaver.Get());

                //     // Add the contained variables.
                //     for (int i = 0; i < _lambda.CapturedVariables.Count; i++)
                //         actionSet.IndexAssigner.Add(_lambda.CapturedVariables[i], infoSaver.CreateChild(i + 2));

                //     if (_lambda.Expression != null)
                //         actionSet.ReturnHandler.ReturnValue(_lambda.Expression.Parse(actionSet));
                //     else
                //         _lambda.Statement.Translate(actionSet);
            }

            // Finish the switch.
            lambdaSwitch.Finish(((Element)actionSet.CurrentObject)[0]);
        }

        public IParameterHandler CreateParameterHandler(ActionSet actionSet) => new LambdaParameterHandler(_deltinScript.VarCollection, _recycler, _lambdas.ToArray());

        class LambdaParameterHandler : IParameterHandler
        {
            readonly RecycleWorkshopVariableAssigner _recycler;
            readonly LambdaAction[] _lambdas;
            readonly List<AssignedParameter> _assignedParameters = new List<AssignedParameter>();

            public LambdaParameterHandler(VarCollection varCollection, RecycleWorkshopVariableAssigner recycler, LambdaAction[] lambdas)
            {
                _recycler = recycler;
                _lambdas = lambdas;

                // Loop through each lambda.
                foreach (var lambda in lambdas)
                {
                    // Assign the variables.
                    foreach (var parameter in lambda.Parameters)
                    {
                        // Assign the parameter.
                        var gettable = parameter.CodeType
                            .GetGettableAssigner(parameter)
                            .GetValue(new GettableAssignerValueInfo(varCollection) {
                                IndexReferenceCreator = recycler,
                                SetInitialValue = false
                            });

                        _assignedParameters.Add(new AssignedParameter(parameter, gettable));
                    }

                    // Reset.
                    _recycler.Reset();
                }
            }

            public void AddParametersToAssigner(VarIndexAssigner assigner)
            {
                foreach (var assignedParameter in _assignedParameters)
                    assigner.Add(assignedParameter.Variable, assignedParameter.Gettable);
            }

            struct AssignedParameter
            {
                public IVariable Variable;
                public IGettable Gettable;

                public AssignedParameter(IVariable variable, IGettable gettable)
                {
                    Variable = variable;
                    Gettable = gettable;
                }
            }

            public void Set(ActionSet actionSet, IWorkshopTree[] parameterValues)
            {
                parameterValues = ExtractStructs(parameterValues);

                for (int i = 0; i < parameterValues.Length; i++)
                    _recycler.Created[i].Set(actionSet, (Element)parameterValues[i]);
            }

            public void Push(ActionSet actionSet, IWorkshopTree[] parameterValues)
            {
                parameterValues = ExtractStructs(parameterValues);
                throw new NotImplementedException();
            }

            static IWorkshopTree[] ExtractStructs(IWorkshopTree[] parameterValues)
            {
                var extracted = new List<IWorkshopTree>();

                // Extract all values from the parameters.
                foreach (var parameter in parameterValues)
                {
                    // Unfold struct.
                    if (parameter is IStructValue structValue)
                        extracted.AddRange(structValue.GetAllValues());
                    else // Normal
                        extracted.Add(parameter);
                }

                return extracted.ToArray();
            }

            public void Pop(ActionSet actionSet)
            {
                throw new NotImplementedException();
            }
        }
    }
}