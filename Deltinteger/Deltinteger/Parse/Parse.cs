﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.LanguageServer;
using Deltin.Deltinteger.Models;
using Antlr4.Runtime;

namespace Deltin.Deltinteger.Parse
{
    public class ParsingData
    {
        private static readonly Log Log = new Log("Parse");

        public static ParsingData GetParser(string file, string content)
        {
            return new ParsingData(file, content);
        }

        private ParsingData(string file, string content)
        {
            Rule initialGlobalValues = new Rule(Constants.INTERNAL_ELEMENT + "Initial Global Values");
            Rule initialPlayerValues = new Rule(Constants.INTERNAL_ELEMENT + "Initial Player Values", RuleEvent.OngoingPlayer, Team.All, PlayerSelector.All);
            TranslateRule globalTranslate = new TranslateRule(initialGlobalValues, Root, this);
            TranslateRule playerTranslate = new TranslateRule(initialPlayerValues, Root, this);

            GetObjects(content, file, globalTranslate, playerTranslate, true);

            foreach (var type in DefinedTypes)
                try
                {
                    type.RegisterParameters(this);
                }
                catch (SyntaxErrorException ex)
                {
                    Diagnostics.Error(ex);
                }
            foreach (var method in UserMethods)
                try
                {
                    method.RegisterParameters(this);
                }
                catch (SyntaxErrorException ex)
                {
                    Diagnostics.Error(ex);
                }
            
            if (!Diagnostics.ContainsErrors())
            {
                // Parse the rules.
                Rules = new List<Rule>();

                for (int i = 0; i < RuleNodes.Count; i++)
                {
                    try
                    {
                        var result = TranslateRule.GetRule(RuleNodes[i], Root, this);
                        Rules.Add(result);
                    }
                    catch (SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }
                }

                foreach (var definedVar in VarCollection.AllVars)
                    try
                    {
                        if (definedVar is IndexedVar && definedVar.IsDefinedVar && definedVar.Scope == Root)
                        {
                            Node value = ((IDefine)definedVar.Node).Value;
                            if (value != null)
                            {
                                if (((IndexedVar)definedVar).IsGlobal)
                                    globalTranslate.Actions.AddRange(((IndexedVar)definedVar).SetVariable(globalTranslate.ParseExpression(Root, Root, value)));
                                else
                                    playerTranslate.Actions.AddRange(((IndexedVar)definedVar).SetVariable(playerTranslate.ParseExpression(Root, Root, value)));

                            }
                        }
                    }
                    catch(SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }
                
                globalTranslate.Finish();
                playerTranslate.Finish();

                // Add the player initial values rule if it was used.
                if (initialPlayerValues.Actions.Length > 0)
                    Rules.Insert(0, initialPlayerValues);
                
                // Add the global initial values rule if it was used.
                if (initialGlobalValues.Actions.Length > 0)
                    Rules.Insert(0, initialGlobalValues);

                foreach (Rule rule in AdditionalRules)
                    if (rule.Actions.Length > 0)
                        Rules.Add(rule);
            }

            Success = !Diagnostics.ContainsErrors();
        }

        private RulesetNode GetRuleset(string file, string content)
        {
            AntlrInputStream inputStream = new AntlrInputStream(content);

            // Lexer
            DeltinScriptLexer lexer = new DeltinScriptLexer(inputStream);
            CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);

            // Parse
            DeltinScriptParser parser = new DeltinScriptParser(commonTokenStream);
            var errorListener = new ErrorListener(file, Diagnostics);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errorListener);

            DeltinScriptParser.RulesetContext ruleSetContext = parser.ruleset();

            Log.Write(LogLevel.Verbose, ruleSetContext.ToStringTree(parser));

            // Get the ruleset node.
            RulesetNode ruleset = null;
            if (!Diagnostics.ContainsErrors())
            {
                BuildAstVisitor bav = new BuildAstVisitor(file, Diagnostics);
                ruleset = (RulesetNode)bav.Visit(ruleSetContext);
            }

            AdditionalErrorChecking aec = new AdditionalErrorChecking(file, parser, Diagnostics);
            aec.Visit(ruleSetContext);

            return ruleset;
        }

        private void GetObjects(string document, string file, TranslateRule globalTranslate, TranslateRule playerTranslate, bool isRoot)
        {
            // If this file was already loaded, don't load it again.
            if (Imported.Contains(file)) return;
            Imported.Add(file);
            Diagnostics.AddFile(file);

            // Get the ruleset.
            RulesetNode ruleset = GetRuleset(file, document);
            Rulesets.Add(file, ruleset);

            if (ruleset != null && !Diagnostics.ContainsErrors())
            {
                if (isRoot)
                {
                    VarCollection = new VarCollection(ruleset.UseGlobalVar, ruleset.UsePlayerVar, ruleset.UseBuilderVar);
                    Root = new ScopeGroup(VarCollection);
                }

                // Get the defined types
                foreach (var definedType in ruleset.DefinedTypes)
                    try
                    {
                        if (DefinedTypes.Any(type => type.Name == definedType.Name))
                            throw SyntaxErrorException.NameAlreadyDefined(definedType.Location);
                        DefinedTypes.Add(DefinedType.GetDefinedType(definedType));
                    }
                    catch (SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }

                // Get the user methods.
                for (int i = 0; i < ruleset.UserMethods.Length; i++)
                    try
                    {
                        UserMethods.Add(new UserMethod(Root, ruleset.UserMethods[i]));
                    }
                    catch (SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }

                // Get the rules
                RuleNodes.AddRange(ruleset.Rules);

                List<string> importedFiles = new List<string>();

                foreach (ImportObjectNode importObject in ruleset.ObjectImports)
                    try
                    {
                        Importer importer = new Importer(Diagnostics, importedFiles, importObject.File, file, importObject.Location);
                        if (!importer.AlreadyImported)
                        {
                            importedFiles.Add(importer.ResultingPath);
                            string content = importer.GetFile();
                            switch (importer.FileType)
                            {
                                case ".obj":
                                    Model newModel = Model.ImportObj(content);
                                    new ModelVar(importObject.Name, Root, importObject, newModel);
                                    break;
                            }
                        }
                    }
                    catch (SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }

                // Check the imported files.
                foreach (ImportNode importNode in ruleset.Imports)
                    try
                    {
                        Importer importer = new Importer(Diagnostics, importedFiles, importNode.File, file, importNode.Location);
                        if (!importer.AlreadyImported)
                        {
                            string content = File.ReadAllText(importer.ResultingPath);
                            GetObjects(content, importer.ResultingPath, globalTranslate, playerTranslate, false);
                            importedFiles.Add(importer.ResultingPath);
                        }
                    }
                    catch (SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }

                // Get the variables
                foreach (var definedVar in ruleset.DefinedVars)
                    try
                    {
                        IndexedVar var;
                        if (definedVar.UseVar == null)
                            var = VarCollection.AssignVar(Root, definedVar.VariableName, definedVar.IsGlobal, definedVar);
                        else
                            var = VarCollection.AssignVar(
                                Root, 
                                definedVar.VariableName, 
                                definedVar.IsGlobal,
                                definedVar.UseVar.Variable, 
                                definedVar.UseVar.Index,
                                definedVar
                            );
                        if (definedVar.Type != null)
                            var.Type = GetDefinedType(definedVar.Type, definedVar.Location);
                    }
                    catch (SyntaxErrorException ex)
                    {
                        Diagnostics.Error(ex);
                    }
            }
        }

        public Diagnostics Diagnostics { get; private set; } = new Diagnostics();
        public List<Rule> Rules { get; private set; }
        private List<RuleNode> RuleNodes { get; set; } = new List<RuleNode>();
        public List<DefinedType> DefinedTypes { get; private set; } = new List<DefinedType>();
        public List<UserMethod> UserMethods { get; private set; } = new List<UserMethod>();
        public bool Success { get; private set; }
        public VarCollection VarCollection { get; private set; }
        public ScopeGroup Root { get; private set; }
        public Dictionary<string, RulesetNode> Rulesets { get; } = new Dictionary<string, RulesetNode>();
        public List<Rule> AdditionalRules { get; } = new List<Rule>();
        public List<VariableChaseData> Chasing { get; } = new List<VariableChaseData>();
        private List<string> Imported { get; } = new List<string>();

        public IMethod GetMethod(string name)
        {
            return (IMethod)UserMethods?.FirstOrDefault(um => um.Name == name) 
            ?? (IMethod)CustomMethodData.GetCustomMethod(name) 
            ?? (IMethod)Element.GetElement(name);
        }

        public DefinedType GetDefinedType(string name, Location location)
        {
            DefinedType type = DefinedTypes.FirstOrDefault(dt => dt.Name == name);
            if (type == null && location != null)
                throw SyntaxErrorException.NonexistentType(name, location);
            return type;
        }
    }
}