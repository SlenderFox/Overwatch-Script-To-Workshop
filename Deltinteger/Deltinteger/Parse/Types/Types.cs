using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.Compiler;
using Deltin.Deltinteger.Compiler.SyntaxTree;
using Deltin.Deltinteger.Parse.Lambda;
using CompletionItem = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItem;
using CompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StringOrMarkupContent = OmniSharp.Extensions.LanguageServer.Protocol.Models.StringOrMarkupContent;

namespace Deltin.Deltinteger.Parse
{
    public abstract class CodeType : IExpression, ICallable, IWorkshopInit
    {
        public string Name { get; }
        public CodeType[] Generics { get; protected set; }
        public Constructor[] Constructors { get; protected set; } = new Constructor[0];
        public CodeType Extends { get; private set; }
        public string Description { get; protected set; }
        public IInvokeInfo InvokeInfo { get; protected set; }
        public Debugger.IDebugVariableResolver DebugVariableResolver { get; protected set; } = new Debugger.DefaultResolver();
        protected string Kind = "class";
        protected TokenType TokenType { get; set; } = TokenType.Type;
        protected List<TokenModifier> TokenModifiers { get; set; } = new List<TokenModifier>();

        /// <summary>Determines if the class can be deleted with the delete keyword.</summary>
        public bool CanBeDeleted { get; protected set; } = false;

        /// <summary>Determines if other classes can inherit this class.</summary>
        public bool CanBeExtended { get; protected set; } = false;

        public TypeOperation[] Operations { get; protected set; }

        public CodeType(string name)
        {
            Name = name;
        }

        protected void Inherit(CodeType extend, FileDiagnostics diagnostics, DocRange range)
        {
            if (extend == null) throw new ArgumentNullException(nameof(extend));

            string errorMessage = null;

            if (!extend.CanBeExtended)
                errorMessage = "Type '" + extend.Name + "' cannot be inherited.";
            
            else if (extend == this)
                errorMessage = "Cannot extend self.";
            
            else if (extend.Implements(this))
                errorMessage = $"The class {extend.Name} extends this class.";

            if (errorMessage != null)
            {
                if (diagnostics == null || range == null) throw new Exception(errorMessage);
                else
                {
                    diagnostics.Error(errorMessage, range);
                    return;
                }
            }

            Extends = extend;
        }

        public virtual bool Implements(CodeType type)
        {
            if (type == null) return false;

            // Iterate through all extended classes.
            CodeType checkType = this;
            while (checkType != null)
            {
                if (type.Is(checkType)) return true;
                checkType = checkType.Extends;
            }

            return false;
        }

        public virtual bool Is(CodeType type) => this == type;

        // Static
        public virtual Scope ReturningScope() => null;
        // Object
        public virtual Scope GetObjectScope() => null;

        public CodeType Type() => null;
        public IWorkshopTree Parse(ActionSet actionSet) => null;

        /// <summary>Determines if variables with this type can have their value changed.</summary>
        public virtual bool IsConstant() => false;

        /// <summary>The returning value when `new TypeName` is called.</summary>
        /// <param name="actionSet">The actionset to use.</param>
        /// <param name="constructor">The constuctor that was called.</param>
        /// <param name="constructorValues">The parameter values of the constructor.</param>
        /// <param name="additionalParameterData">Additional parameter data.</param>
        public virtual IWorkshopTree New(ActionSet actionSet, Constructor constructor, IWorkshopTree[] constructorValues, object[] additionalParameterData)
        {
            // Classes that can't be created shouldn't have constructors.
            throw new NotImplementedException();
        }
        
        /// <summary>Sets up an object reference when a new object is created. Is also called when a new object of a class extending this type is created.</summary>
        /// <param name="actionSet">The actionset to use.</param>
        /// <param name="reference">The reference of the object.</param>
        public virtual void BaseSetup(ActionSet actionSet, Element reference) => throw new NotImplementedException();

        public virtual void ResolveElements() {}

        /// <summary>Assigns workshop elements so the class can function. Implementers should check if `wasCalled` is true.</summary>
        public virtual void WorkshopInit(DeltinScript translateInfo) {}

        /// <summary>Adds the class objects to the index assigner.</summary>
        /// <param name="source">The source of the type.</param>
        /// <param name="assigner">The assigner that the object variables will be added to.</param>
        public virtual void AddObjectVariablesToAssigner(IWorkshopTree reference, VarIndexAssigner assigner) {}

        /// <summary>Deletes a variable from memory.</summary>
        /// <param name="actionSet">The action set to add the actions to.</param>
        /// <param name="reference">The object reference.</param>
        public virtual void Delete(ActionSet actionSet, Element reference) {}

        /// <summary>
        /// Gets an operation.
        /// </summary>
        /// <param name="op">The operation's operator type.</param>
        /// <param name="right">The right object's type.</param>
        /// <returns>A TypeOperation if the operation is found. Null if it is not found.</returns>
        public TypeOperation GetOperation(TypeOperator op, CodeType right)
        {
            CodeType current = this;
            while (current != null)
            {
                if (current.Operations != null)
                    foreach (TypeOperation operation in current.Operations)
                        if (operation.Operator == op && right != null && right.Implements(operation.Right))
                            return operation;
                
                current = current.Extends;
            }
            return null;
        }

        /// <summary>Calls a type from the specified document range.</summary>
        /// <param name="parseInfo">The script that the type was called from.</param>
        /// <param name="callRange">The range of the call.</param>
        public virtual void Call(ParseInfo parseInfo, DocRange callRange)
        {
            parseInfo.TranslateInfo.AddWorkshopInit(this);
            parseInfo.Script.AddHover(callRange, HoverHandler.Sectioned(Kind + " " + Name, Description));
            parseInfo.Script.AddToken(callRange, TokenType, TokenModifiers.ToArray());
        }

        /// <summary>Gets the completion that will show up for the language server.</summary>
        public abstract CompletionItem GetCompletion();

        /// <summary>Gets the full name of the type.</summary>
        public virtual string GetName() => Name;

        public virtual void AddLink(Location location) {}

        public virtual void GetRealType(ParseInfo parseInfo, Action<CodeType> callback) => callback(this);

        static List<CodeType> _defaultTypes;
        public static List<CodeType> DefaultTypes {
            get {
                if (_defaultTypes == null) GetDefaultTypes();
                return _defaultTypes;
            }
        }
        private static void GetDefaultTypes()
        {
            _defaultTypes = new List<CodeType>();
            _defaultTypes.AddRange(ValueGroupType.EnumTypes);

            // Add custom classes here.
            _defaultTypes.Add(new Models.AssetClass());
            _defaultTypes.Add(ObjectType.Instance);
            _defaultTypes.Add(NumberType.Instance);
            _defaultTypes.Add(BooleanType.Instance);
            _defaultTypes.Add(TeamType.Instance);
            _defaultTypes.Add(VectorType.Instance);
            _defaultTypes.Add(StringType.Instance);
            _defaultTypes.Add(Positionable.Instance);
            _defaultTypes.Add(Pathfinder.SegmentsStruct.Instance);
            ObjectType.Instance.InitOperations();
            NumberType.Instance.InitOperations();
            VectorType.Instance.InitOperations();
            StringType.Instance.InitOperations();
        }
    }
}