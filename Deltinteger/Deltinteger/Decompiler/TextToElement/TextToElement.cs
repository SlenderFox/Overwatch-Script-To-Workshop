using System;
using System.Collections.Generic;
using System.Linq;
using Deltin.Deltinteger.Elements;
using Deltin.Deltinteger.Decompiler.ElementToCode;

namespace Deltin.Deltinteger.Decompiler.TextToElement
{
    class ConvertTextToElement
    {
        private readonly static char[] WHITESPACE = new char[] { '\r', '\n', '\t', ' ' };
        private readonly static string[] DEFAULT_VARIABLES = new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

        public string Content { get; }
        public int Position { get; private set; }
        public char Current => Content[Position];
        public bool ReachedEnd => Position >= Content.Length;

        private readonly Stack<TTEOperator> _operators = new Stack<TTEOperator>();
        private readonly Stack<ITTEExpression> _operands = new Stack<ITTEExpression>();

        private readonly ElementList[] _actions;
        private readonly ElementList[] _values;

        public List<WorkshopVariable> Variables { get; } = new List<WorkshopVariable>();
        public List<Subroutine> Subroutines { get; } = new List<Subroutine>();
        public List<TTERule> Rules { get; } = new List<TTERule>();

        public ConvertTextToElement(string content)
        {
            Content = content;
            _actions = ElementList.Elements.Where(e => !e.IsValue).OrderByDescending(e => e.WorkshopName.Length).ToArray();
            _values = ElementList.Elements.Where(e => e.IsValue).OrderByDescending(e => e.WorkshopName.Length).ToArray();
            _operators.Push(TTEOperator.Sentinel);
        }

        public Workshop Get()
        {
            // Match variables and subroutines.
            MatchVariables();
            MatchSubroutines();
            // Match rules
            while (Rule(out TTERule rule)) Rules.Add(rule);

            return new Workshop(Variables.ToArray(), Subroutines.ToArray(), Rules.ToArray());
        }

        // TODO: Translate the english keyword to the specified language's keyword.
        public string Kw(string value) => value;

        void Advance()
        {
            if (!ReachedEnd)
                Position += 1;
        }

        void Advance(int length)
        {
            Position = Math.Min(Content.Length, Position + length);
        }

        void SkipWhitespace()
        {
            while (!ReachedEnd && WHITESPACE.Contains(Current))
                Advance();
        }

        bool Is(char character) => !ReachedEnd && Current == character;
        bool Is(int position, char character) => Position + position < Content.Length && Content[Position + position] == character;
        bool IsInsensitive(int position, char character) => Position + position < Content.Length && Char.ToLower(Content[Position + position]) == Char.ToLower(character);
        bool IsSymbol(int position) => Position + position < Content.Length && char.IsSymbol(Content[Position + position]);
        bool IsAny(params char[] characters) => !ReachedEnd && characters.Contains(Current);
        bool IsAny(string characters) => IsAny(characters.ToCharArray());
        bool IsNumeric() => IsAny("0123456789");
        bool IsAlpha() => IsAny("_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");
        bool IsAlphaNumeric() => IsNumeric() || IsAlpha();

        bool Match(string str, bool caseSensitive = true)
        {
            for (int i = 0; i < str.Length; i++)
                if ((caseSensitive && !Is(i, str[i])) || !IsInsensitive(i, str[i]))
                    return false;
            
            if (IsSymbol(str.Length)) return false;

            Advance(str.Length);
            SkipWhitespace();
            return true;
        }

        // Commons
        // String
        bool MatchString(out string value)
        {
            if (!Match("\""))
            {
                value = null;
                return false;
            }

            value = "";
            bool escaped = false;
            do
            {
                value += Current;
                if (escaped) escaped = false;
                else if (Is(0, '\\')) escaped = true;
                Advance();
            }
            while (escaped || !Is(0, '"'));
            Advance();
            SkipWhitespace();

            return true;
        }

        // Identifier
        bool Identifier(out string identifier)
        {
            if (!IsAlpha())
            {
                identifier = null;
                return false;
            }

            identifier = "";
            while (IsAlphaNumeric())
            {
                identifier += Current;
                Advance();
            }
            SkipWhitespace();
            return true;
        }

        // Integer
        bool Integer(out int value)
        {
            string str = "";

            if (Match("-")) str += "-";

            while (IsNumeric())
            {
                str += Current;
                Advance();
            }

            if (str == "")
            {
                value = 0;
                return false;
            }
            value = int.Parse(str);
            SkipWhitespace();
            return true;
        }

        // Double
        bool Double(out double number)
        {
            string str = "";

            if (Match("-")) str += "-";

            while (IsNumeric())
            {
                str += Current;
                Advance();
            }

            if (str == "")
            {
                number = 0;
                return false;
            }

            if (Match("."))
            {
                str += ".";
                while (IsNumeric())
                {
                    str += Current;
                    Advance();
                }
            }

            number = double.Parse(str);
            SkipWhitespace();
            return true;
        }

        // Commons as ITTEExpression
        bool Number(out ITTEExpression numberExpression)
        {
            if (Double(out double value))
            {
                numberExpression = new NumberExpression(value);
                return true;
            }
            numberExpression = null;
            return false;
        }

        // Workshop Copy Structure
        // Variable list
        bool MatchVariables()
        {
            if (!Match("variables")) return false;
            Match("{");

            if (Match("global:")) MatchVariableList(true);
            if (Match("player:")) MatchVariableList(false);
            
            Match("}");
            return true;
        }

        void MatchVariableList(bool isGlobal)
        {
            while (Integer(out int index))
            {
                Match(":");
                Identifier(out string name);
                Variables.Add(new WorkshopVariable(isGlobal, index, name));
            }
        }

        void AddIfOmitted(string variableName, bool isGlobal)
        {
            // Get the default index of the variable.
            int index = Array.IndexOf(DEFAULT_VARIABLES, variableName);
            // If the index was found and the variable was not added, add it.
            if (index != -1 && !Variables.Any(v => v.IsGlobal == isGlobal && v.Name == variableName))
                Variables.Add(new WorkshopVariable(isGlobal, index, variableName));
        }

        // Subroutines
        bool MatchSubroutines()
        {
            if (!Match("subroutines")) return false;
            Match("{");

            // Subroutine list
            while (Integer(out int index))
            {
                Match(":");
                Identifier(out string name);
                Subroutines.Add(new Subroutine(index, name));
            } 

            Match("}");
            return true;
        }

        // Rules
        bool Rule(out TTERule rule)
        {
            bool disabled;
            if (Match("disabled"))
            {
                disabled = true;
                Match("rule");
            }
            else if (Match("rule"))
            {
                disabled = false;
            }
            else
            {
                rule = null;
                return false;
            }

            Match("(");
            MatchString(out string ruleName);
            Match(")");
            Match("{");

            // Event
            Match("event");
            Match("{");
            EventInfo eventInfo = MatchEvent();
            Match("}");

            // Conditions
            List<ITTEExpression> conditions = new List<ITTEExpression>();
            if (Match("conditions"))
            {
                Match("{");
                while (Expression(out ITTEExpression condition))
                {
                    Match(";");
                    conditions.Add(condition);
                }
                Match("}");
            }

            // Actions
            List<ITTEAction> actions = new List<ITTEAction>(); 
            if (Match("actions"))
            {
                Match("{");
                while (Action(out ITTEAction action)) actions.Add(action);
                Match("}");
            }

            Match("}");

            rule = new TTERule(ruleName, eventInfo, conditions.ToArray(), actions.ToArray(), disabled);
            return true;
        }

        EventInfo MatchEvent()
        {
            // Global
            if (Match("Ongoing - Global;"))
            {
                return new EventInfo();
            }
            // Subroutine
            else if (Match("Subroutine;"))
            {
                Identifier(out string subroutineName);
                Match(";");
                return new EventInfo(subroutineName);
            }
            // Player event
            else
            {
                // Get the event type.
                var ruleEvent = RuleEvent.OngoingGlobal;
                foreach (var eventNameInfo in EventInfo.PlayerEventNames)
                    if (Match(eventNameInfo.Item1 + ";"))
                    {
                        ruleEvent = eventNameInfo.Item2;
                        break;
                    }

                // Get the team.
                var team = Team.All;
                if (Match("All;")) {}
                else if (Match("Team 1;"))
                    team = Team.Team1;
                else if (Match("Team 2;"))
                    team = Team.Team2;
                
                // Get the player type.
                var player = PlayerSelector.All;
                foreach (var playerNameInfo in EventInfo.PlayerTypeNames)
                    if (Match(playerNameInfo.Item1 + ";"))
                    {
                        player = playerNameInfo.Item2;
                        break;
                    }

                return new EventInfo(ruleEvent, player, team);
            }
        }

        // Actions
        bool Action(out ITTEAction action)
        {
            action = null;

            // Comment
            MatchString(out string comment);
            bool isDisabled = Match(Kw("disabled"));

            // Subroutine
            if (Match(Kw("Call Subroutine")))
            {
                Match("(");
                Identifier(out string name);
                Match(")");
                action = new CallSubroutine(name, Parse.CallParallel.NoParallel);
            }
            // Function.
            else if (Function(true, out FunctionExpression func))
            {
                action = func;
            }
            // Set variable.
            else if (Expression(out ITTEExpression expr, getIndexer: false))
            {
                // Make sure the expression is a global or player variable.
                if (expr is ITTEVariable == false)
                    throw new Exception("Expression is not a variable.");
                
                // Get the index being set.
                VariableIndex(out var index);
                
                string op = null;
                string[] operators = new string[] { "=", "+=", "-=", "/=", "*=" };
                foreach (string it in operators)
                    if (Match(it))
                    {
                        op = it;
                        break;
                    }
                
                Expression(out ITTEExpression value);
                action = new SetVariableAction((ITTEVariable)expr, op, value, index);
            }
            // Unknown.
            else
            {
                return false;
            }

            action.Disabled = isDisabled;
            action.Comment = comment;
            Match(";");
            return true;
        }

        // Functions
        bool Function(bool actions, out FunctionExpression expr)
        {
            if (actions)
                // Actions
                foreach (var action in _actions)
                {
                    if (Function(action, out expr))
                        return true;
                }
            else
                // Values
                foreach (var value in _values)
                {
                    if (Function(value, out expr))
                        return true;
                }
            
            // Nope
            expr = null;
            return false;
        }

        bool Function(ElementList func, out FunctionExpression expr)
        {
            if (!Match(Kw(func.WorkshopName), false))
            {
                expr = null;
                return false;
            }

            // Get the parameter values.
            List<ITTEExpression> values = new List<ITTEExpression>();
            if (Match("("))
            {
                int currentParameter = 0;
                do
                {
                    // Normal parameter
                    if (currentParameter >= func.WorkshopParameters.Length || func.WorkshopParameters[currentParameter] is Parameter)
                    {
                        _operators.Push(TTEOperator.Sentinel);
                        if (Expression(out ITTEExpression value)) values.Add(value);
                        _operators.Pop();
                    }
                    // Enumerator
                    else if (func.WorkshopParameters[currentParameter] is EnumParameter enumParam)
                    {
                        // Match enum member
                        foreach (var member in enumParam.EnumData.Members.OrderByDescending(m => m.WorkshopName.Length))
                            if (Match(Kw(member.WorkshopName), false))
                            {
                                values.Add(new ConstantEnumeratorExpression(member));
                                break;
                            }
                    }
                    // Variable reference
                    else if (func.WorkshopParameters[currentParameter] is VarRefParameter varRefParameter)
                    {
                        // Match the variable parameter.
                        if (!Identifier(out string identifier))
                            throw new Exception("Failed to retrieve identifier of variable parameter.");
                        
                        AddIfOmitted(identifier, varRefParameter.IsGlobal);
                        values.Add(new AnonymousVariableExpression(identifier, varRefParameter.IsGlobal));
                    }

                    // Increment the current parameter.
                    currentParameter++;
                }
                while (Match(","));
                Match(")");
            }

            expr = new FunctionExpression(func, values.ToArray());
            return true;
        }

        // Expressions
        bool Expression(out ITTEExpression expr, bool root = true, bool getIndexer = true)
        {
            expr = null;

            // Group
            if (Match("("))
            {
                _operators.Push(TTEOperator.Sentinel);
                Expression(out expr);
                Match(")");
                _operators.Pop();
            }
            // Number
            else if (Number(out expr)) {}
            // String
            else if (WorkshopString(out expr)) {}
            // Enum value
            else if (EnumeratorValue(out expr)) {}
            // Variable
            else if (GlobalVariable(out expr)) {}
            // Function
            else if (Function(false, out FunctionExpression value))
            {
                expr = value;
            }
            // Unary operator
            else if (MatchUnary(out TTEOperator unaryOperator))
            {
                PushOperator(unaryOperator);
                Expression(out expr);
            }
            // No matches
            else
            {
                return false;    
            }
            
            // Player variable
            if (MatchPlayerVariable(expr, out ITTEExpression playerVariable))
                expr = playerVariable;
            
            // Array index
            if (getIndexer)
            {
                while (VariableIndex(out ITTEExpression index))
                    expr = new IndexerExpression(expr, index);
            }

            // Push the expression
            _operands.Push(expr);
            
            // Binary operator
            while (MatchOperator(out TTEOperator op))
            {
                PushOperator(op);
                Expression(out ITTEExpression right, false);
            }
            while (_operators.Peek().Precedence > 0)
                PopOperator();
            
            // If this is the root, return the top operand.
            if (root) expr = _operands.Pop();

            return true;
        }

        // Workshop string function
        bool WorkshopString(out ITTEExpression expr)
        {
            bool localized; // Determines if the string is localized.

            // Custom string
            if (Match(Kw("Custom String")))
                localized = false;
            // Localized string
            else if (Match(Kw("String")))
                localized = true;
            else
            {
                // Not a string
                expr = null;
                return false;
            }

            Match("(");

            // Get the actual string.
            MatchString(out string str);

            // Get the format parameters.
            List<ITTEExpression> formats = new List<ITTEExpression>();
            while (Match(","))
            {
                _operators.Push(TTEOperator.Sentinel);
                if (Expression(out ITTEExpression value)) formats.Add(value);
                _operators.Pop();
            }

            Match(")");

            expr = new StringExpression(str, formats.ToArray(), localized);
            return true;
        }

        // Enumerator Values
        bool EnumeratorValue(out ITTEExpression expr)
        {
            if (Match(Kw("All Teams")))
            {
                expr = new ConstantEnumeratorExpression(EnumData.GetEnumValue(Team.All));
                return true;
            }
            if (Match(Kw("Team 1")))
            {
                expr = new ConstantEnumeratorExpression(EnumData.GetEnumValue(Team.Team1));
                return true;
            }
            if (Match(Kw("Team 2")))
            {
                expr = new ConstantEnumeratorExpression(EnumData.GetEnumValue(Team.Team2));
                return true;
            }
            // TODO: Gamemode, map, button, etc

            expr = null;
            return false;
        }

        // Variables
        bool GlobalVariable(out ITTEExpression expr)
        {
            int c = Position; // Revert

            string name = null;
            bool result = Match("Global")
                && Match(".")
                && Identifier(out name);

            if (!result)
            {
                expr = null;
                Position = c;
                return false;
            }
            
            AddIfOmitted(name, true);
            expr = new GlobalVariableExpression(name);
            return true;
        }

        bool MatchPlayerVariable(ITTEExpression parent, out ITTEExpression playerVariable)
        {
            playerVariable = parent;
            bool matched = false;

            while (Match("."))
            {
                matched = true;
                Identifier(out string name);
                AddIfOmitted(name, false);
                playerVariable = new PlayerVariableExpression(name, playerVariable);
            }
            
            return matched;
        }

        bool VariableIndex(out ITTEExpression index)
        {
            if (!Match("["))
            {
                index = null;
                return false;
            }

            Expression(out index);
            Match("]");
            return true;
        }
    
        // Operators
        bool MatchOperator(out TTEOperator op)
        {
            if (Match("&&")) op = TTEOperator.And;
            else if (Match("||")) op = TTEOperator.Or;
            else if (Match("-")) op = TTEOperator.Subtract;
            else if (Match("+")) op = TTEOperator.Add;
            else if (Match("%")) op = TTEOperator.Modulo;
            else if (Match("/")) op = TTEOperator.Divide;
            else if (Match("*")) op = TTEOperator.Multiply;
            else if (Match("^")) op = TTEOperator.Power;
            else if (Match("==")) op = TTEOperator.Equal;
            else if (Match("!=")) op = TTEOperator.NotEqual;
            else if (Match(">=")) op = TTEOperator.GreaterThanOrEqual;
            else if (Match("<=")) op = TTEOperator.LessThanOrEqual;
            else if (Match(">")) op = TTEOperator.GreaterThan;
            else if (Match("<")) op = TTEOperator.LessThan;
            else if (Match("?")) op = TTEOperator.Ternary;
            else if (Match(":")) op = TTEOperator.RhsTernary;
            else
            {
                op = null;
                return false;
            }
            return true;
        }

        bool MatchUnary(out TTEOperator op)
        {
            if (Match("!")) op = TTEOperator.Not;
            else
            {
                op = null;
                return false;
            }
            return true;
        }

        void PushOperator(TTEOperator op)
        {
            // while (_operators.Peek().Precedence > op.Precedence)
            while (TTEOperator.Compare(_operators.Peek(), op))
                PopOperator();
            _operators.Push(op);
        }

        void PopOperator()
        {
            var op = _operators.Pop();
            if (op.Type == OperatorType.Binary)
            {
                // Binary
                var right = _operands.Pop();
                var left = _operands.Pop();
                _operands.Push(new BinaryOperatorExpression(left, right, op));
            }
            else if (op.Type == OperatorType.Unary)
            {
                // Unary
                var value = _operands.Pop();
                _operands.Push(new UnaryOperatorExpression(value, op));
            }
            else
            {
                // Ternary
                var op2 = _operators.Pop();
                var rhs = _operands.Pop();
                var middle = _operands.Pop();
                var lhs = _operands.Pop();
                _operands.Push(new TernaryExpression(lhs, middle, rhs));
            }
        }
    }

    public class Workshop
    {
        public WorkshopVariable[] Variables { get; }
        public Subroutine[] Subroutines { get; }
        public TTERule[] Rules { get; }

        public Workshop(WorkshopVariable[] variables, Subroutine[] subroutines, TTERule[] rules)
        {
            Variables = variables;
            Subroutines = subroutines;
            Rules = rules;
        }
    }

    public class EventInfo
    {
        public static readonly (string, RuleEvent)[] PlayerEventNames = new (string, RuleEvent)[] {
            ("Ongoing - Each Player", RuleEvent.OngoingPlayer),
            ("Player Earned Elimination", RuleEvent.OnElimination),
            ("Player Dealt Final Blow", RuleEvent.OnFinalBlow),
            ("Player Dealt Damage", RuleEvent.OnDamageDealt),
            ("Player Took Damage", RuleEvent.OnDamageTaken),
            ("Player Died", RuleEvent.OnDeath),
            ("Player Dealt Healing", RuleEvent.OnHealingDealt),
            ("Player Received Healing", RuleEvent.OnHealingTaken),
            ("Player Joined Match", RuleEvent.OnPlayerJoin),
            ("Player Left Match", RuleEvent.OnPlayerLeave),
            ("Player Dealt Knockback", RuleEvent.PlayerDealtKnockback),
            ("Player Received Knockback", RuleEvent.PlayerReceivedKnockback)
        };
        public static readonly (string, PlayerSelector)[] PlayerTypeNames = new (string, PlayerSelector)[] {
            ("All", PlayerSelector.All),
            ("Ana", PlayerSelector.Ana),
            ("Ashe", PlayerSelector.Ashe),
            ("Baptiste", PlayerSelector.Baptiste),
            ("Bastion", PlayerSelector.Bastion),
            ("Brigitte", PlayerSelector.Brigitte),
            ("Doomfist", PlayerSelector.Doomfist),
            ("D.va", PlayerSelector.Dva),
            ("Echo", PlayerSelector.Echo),
            ("Genji", PlayerSelector.Genji),
            ("Hanzo", PlayerSelector.Hanzo),
            ("Junkrat", PlayerSelector.Junkrat),
            ("Lúcio", PlayerSelector.Lucio),
            ("Mccree", PlayerSelector.Mccree),
            ("Mei", PlayerSelector.Mei),
            ("Mercy", PlayerSelector.Mercy),
            ("Moira", PlayerSelector.Moira),
            ("Orisa", PlayerSelector.Orisa),
            ("Pharah", PlayerSelector.Pharah),
            ("Reaper", PlayerSelector.Reaper),
            ("Reinhardt", PlayerSelector.Reinhardt),
            ("Roadhog", PlayerSelector.Roadhog),
            ("Sigma", PlayerSelector.Sigma),
            ("Slot 0", PlayerSelector.Slot0),
            ("Slot 1", PlayerSelector.Slot1),
            ("Slot 2", PlayerSelector.Slot2),
            ("Slot 3", PlayerSelector.Slot3),
            ("Slot 4", PlayerSelector.Slot4),
            ("Slot 5", PlayerSelector.Slot5),
            ("Slot 6", PlayerSelector.Slot6),
            ("Slot 7", PlayerSelector.Slot7),
            ("Slot 8", PlayerSelector.Slot8),
            ("Slot 9", PlayerSelector.Slot9),
            ("Slot 10", PlayerSelector.Slot10),
            ("Slot 11", PlayerSelector.Slot11),
            ("Soldier: 76", PlayerSelector.Soldier76),
            ("Sombra", PlayerSelector.Sombra),
            ("Symmetra", PlayerSelector.Symmetra),
            ("Torbjörn", PlayerSelector.Torbjorn),
            ("Tracer", PlayerSelector.Tracer),
            ("Widowmaker", PlayerSelector.Widowmaker),
            ("Winston", PlayerSelector.Winston),
            ("Wrecking Ball", PlayerSelector.WreckingBall),
            ("Zarya", PlayerSelector.Zarya),
            ("Zenyatta", PlayerSelector.Zenyatta)
        };
        public RuleEvent Event { get; }
        public PlayerSelector Player { get; }
        public Team Team { get; }
        public string SubroutineName { get; }

        public EventInfo()
        {
        }
        public EventInfo(string subroutineName)
        {
            Event = RuleEvent.Subroutine;
            SubroutineName = subroutineName;
        }
        public EventInfo(RuleEvent ruleEvent, PlayerSelector player, Team team)
        {
            Event = ruleEvent;
            Player = player;
            Team = team;
        }
    }

    public class TTEOperator
    {
        public static TTEOperator Sentinel { get; } = new TTEOperator(0, null);
        // Unary
        public static TTEOperator Not { get; } = new TTEOperator(16, "!", OperatorType.Unary);
        // Compare
        public static TTEOperator Ternary { get; } = new TTEOperator(1, "?", OperatorType.Ternary);
        public static TTEOperator RhsTernary { get; } = new TTEOperator(2, ":", OperatorType.Ternary);
        public static TTEOperator Equal { get; } = new TTEOperator(3, "==");
        public static TTEOperator NotEqual { get; } = new TTEOperator(4, "!=");
        public static TTEOperator GreaterThan { get; } = new TTEOperator(5, ">");
        public static TTEOperator LessThan { get; } = new TTEOperator(6, "<");
        public static TTEOperator GreaterThanOrEqual { get; } = new TTEOperator(7, ">=");
        public static TTEOperator LessThanOrEqual { get; } = new TTEOperator(8, "<=");
        // Boolean
        public static TTEOperator And { get; } = new TTEOperator(9, "&&");
        public static TTEOperator Or { get; } = new TTEOperator(10, "||");
        // Math
        public static TTEOperator Subtract { get; } = new TTEOperator(11, "-");
        public static TTEOperator Add { get; } = new TTEOperator(12, "+");
        public static TTEOperator Modulo { get; } = new TTEOperator(13, "%");
        public static TTEOperator Divide { get; } = new TTEOperator(14, "/");
        public static TTEOperator Multiply { get; } = new TTEOperator(15, "*");
        public static TTEOperator Power { get; } = new TTEOperator(16, "^");

        public int Precedence { get; }
        public string Operator { get; }
        public OperatorType Type { get; }

        public TTEOperator(int precedence, string op, OperatorType type = OperatorType.Binary)
        {
            Precedence = precedence;
            Operator = op;
            Type = type;
        }

        public static bool Compare(TTEOperator op1, TTEOperator op2)
        {
            if ((op1 == Ternary || op1 == RhsTernary) && (op2 == Ternary || op2 == RhsTernary))
                return op1 == RhsTernary && op2 == RhsTernary;
            
            if (op1 == Sentinel || op2 == Sentinel) return false;
            return op1.Precedence > op2.Precedence;
        }
    }

    public enum OperatorType
    {
        Unary,
        Binary,
        Ternary
    }

    public class TTERule
    {
        public string Name { get; }
        public EventInfo EventInfo { get; }
        public ITTEExpression[] Conditions { get; }
        public ITTEAction[] Actions { get; }
        public bool Disabled { get; }

        public TTERule(string name, EventInfo eventInfo, ITTEExpression[] conditions, ITTEAction[] actions, bool disabled)
        {
            Name = name;
            EventInfo = eventInfo;
            Conditions = conditions;
            Actions = actions;
            Disabled = disabled;
        }

        public override string ToString() => Name + " [" + Actions.Length + " actions]"; 
    }

    // Interfaces
    public interface ITTEExpression {
        void Decompile(DecompileRule decompiler);
    }
    public interface ITTEAction {
        string Comment { get; set; }
        bool Disabled { get; set; }
        void Decompile(DecompileRule decompiler);
    }
    // Expressions
    public class NumberExpression : ITTEExpression
    {
        public double Value { get; }

        public NumberExpression(double value)
        {
            Value = value;
        }

        public override string ToString() => Value.ToString();
        public void Decompile(DecompileRule decompiler) => decompiler.Append(Value.ToString());
    }
    public class StringExpression : ITTEExpression
    {
        public string Value { get; }
        public ITTEExpression[] Formats { get; }
        public bool IsLocalized { get; }

        public StringExpression(string str, ITTEExpression[] formats, bool isLocalized)
        {
            Value = str;
            Formats = formats;
            IsLocalized = isLocalized;
        }

        public override string ToString() => (IsLocalized ? "@" : "") + "\"" + Value + "\"";

        public void Decompile(DecompileRule decompiler)
        {
            string str = (IsLocalized ? "@\"" : "\"") + Value + "\"";
            if (Formats == null || Formats.Length == 0)
                decompiler.Append(str);
            else
            {
                decompiler.Append("<" + str + ", ");
                for (int i = 0; i < Formats.Length; i++)
                {
                    Formats[i].Decompile(decompiler);
                    if (i < Formats.Length - 1)
                        decompiler.Append(", ");
                }
                decompiler.Append(">");
            }
        }
    }
    public class BinaryOperatorExpression : ITTEExpression
    {
        public ITTEExpression Left { get; }
        public ITTEExpression Right { get; }
        public TTEOperator Operator { get; }

        public BinaryOperatorExpression(ITTEExpression left, ITTEExpression right, TTEOperator op)
        {
            Left = left;
            Right = right;
            Operator = op;
        }

        public override string ToString() => Left.ToString() + " " + Operator.Operator + " " + Right.ToString();

        public void Decompile(DecompileRule decompiler)
        {
            WriteSide(decompiler, Left);
            decompiler.Append(" " + Operator.Operator + " ");
            WriteSide(decompiler, Right);
        }

        private void WriteSide(DecompileRule decompiler, ITTEExpression expression)
        {
            if (expression is TernaryExpression || (expression is BinaryOperatorExpression bop && bop.Operator.Precedence < Operator.Precedence))
            {
                decompiler.Append("(");
                expression.Decompile(decompiler);
                decompiler.Append(")");
            }
            else
                expression.Decompile(decompiler);
        }
    }
    public class UnaryOperatorExpression : ITTEExpression
    {
        public ITTEExpression Value { get; }
        public TTEOperator Operator { get; }

        public UnaryOperatorExpression(ITTEExpression value, TTEOperator op)
        {
            Value = value;
            Operator = op;
        }

        public override string ToString() => Operator.Operator + Value.ToString();

        public void Decompile(DecompileRule decompiler)
        {
            decompiler.Append(Operator.Operator);

            if (Value is BinaryOperatorExpression || Value is TernaryExpression)
            {
                decompiler.Append("(");
                Value.Decompile(decompiler);
                decompiler.Append(")");
            }
            else
                Value.Decompile(decompiler);
        }
    }
    public class FunctionExpression : ITTEExpression, ITTEAction
    {
        public ElementList Function { get; }
        public ITTEExpression[] Values { get; }
        public string Comment { get; set; }
        public bool Disabled { get; set; }

        public FunctionExpression(ElementList function, ITTEExpression[] values)
        {
            Function = function;
            Values = values;
        }

        public override string ToString() => Function.Name + (Values.Length == 0 ? "" : "(" + string.Join(", ", Values.Select(v => v.ToString())) + ")");

        void ITTEExpression.Decompile(DecompileRule decompiler) => Decompile(decompiler, false);
        void ITTEAction.Decompile(DecompileRule decompiler)
        {
            decompiler.AddComment(this);
            Decompile(decompiler, true);
        }

        public void Decompile(DecompileRule decompiler, bool end)
        {
            if (Disabled)
                decompiler.Append("// ");

            if (WorkshopFunctionDecompileHook.Convert.TryGetValue(Function.WorkshopName, out var action))
                action.Invoke(decompiler, this);
            else
                Default(decompiler, end);
        }

        public void Default(DecompileRule decompiler, bool end)
        {
            decompiler.Append(Function.Name + "(");

            for (int i = 0; i < Values.Length; i++)
            {
                Values[i].Decompile(decompiler);
                if (i < Values.Length - 1)
                    decompiler.Append(", ");
            }

            decompiler.Append(")");

            // Finished
            if (end)
                decompiler.EndAction();
        }
    }
    public class IndexerExpression : ITTEExpression
    {
        public ITTEExpression Expression { get; }
        public ITTEExpression Index { get; }

        public IndexerExpression(ITTEExpression expression, ITTEExpression index)
        {
            Expression = expression;
            Index = index;
        }

        public override string ToString() => Expression.ToString() + "[" + Index.ToString() + "]";

        public void Decompile(DecompileRule decompiler)
        {
            Expression.Decompile(decompiler);
            decompiler.Append("[");
            Index.Decompile(decompiler);
            decompiler.Append("]");
        }
    }
    public class TernaryExpression : ITTEExpression
    {
        public ITTEExpression Condition { get; }
        public ITTEExpression Consequent { get; }
        public ITTEExpression Alternative { get; }

        public TernaryExpression(ITTEExpression condition, ITTEExpression consequent, ITTEExpression alternative)
        {
            Condition = condition;
            Consequent = consequent;
            Alternative = alternative;
        }

        public override string ToString() => "(" + Condition.ToString() + " ? " + Consequent.ToString() + " : " + Alternative.ToString() + ")";

        public void Decompile(DecompileRule decompiler)
        {
            Condition.Decompile(decompiler);
            decompiler.Append(" ? ");
            Consequent.Decompile(decompiler);
            decompiler.Append(" : ");
            Alternative.Decompile(decompiler);
        }
    }
    public class ConstantEnumeratorExpression : ITTEExpression
    {
        public EnumMember Member { get; }

        public ConstantEnumeratorExpression(EnumMember member)
        {
            Member = member;
        }

        public override string ToString() => Member.WorkshopName;

        public void Decompile(DecompileRule decompiler)
        {
            decompiler.Append(Member.Enum.CodeName + "." + Member.CodeName);
        }
    }
    // Actions
    public class SetVariableAction : ITTEAction
    {
        public ITTEVariable Variable { get; }
        public string Operator { get; }
        public ITTEExpression Value { get; }
        public ITTEExpression Index { get; }
        public string Comment { get; set; }
        public bool Disabled { get; set; }

        public SetVariableAction(ITTEVariable variable, string op, ITTEExpression value, ITTEExpression index)
        {
            Variable = variable;
            Operator = op;
            Value = value;
            Index = index;
        }

        public override string ToString() => Variable.ToString() + (Index == null ? " " : "[" + Index.ToString() + "] ") + Operator + " " + Value.ToString();

        public void Decompile(DecompileRule decompiler)
        {
            decompiler.AddComment(this);

            if (Disabled) decompiler.Append("// ");
            Variable.Decompile(decompiler);

            if (Index != null)
            {
                decompiler.Append("[");
                Index.Decompile(decompiler);
                decompiler.Append("]");
            }

            decompiler.Append(" " + Operator + " ");
            Value.Decompile(decompiler);
            decompiler.EndAction();
        }
    }
    public class CallSubroutine : ITTEAction
    {
        public string SubroutineName { get; }
        public Parse.CallParallel Parallel { get; }
        public string Comment { get; set; }
        public bool Disabled { get; set; }

        public CallSubroutine(string name, Parse.CallParallel parallel)
        {
            SubroutineName = name;
            Parallel = parallel;
        }

        public void Decompile(DecompileRule decompiler)
        {
            decompiler.AddComment(this);

            switch (Parallel)
            {
                case Parse.CallParallel.NoParallel:
                    decompiler.Append(SubroutineName + "()");
                    break;
                
                default: throw new NotImplementedException(Parallel.ToString());
            }
            decompiler.EndAction();
        }
    }
    // Variables
    public interface ITTEVariable
    {
        string Name { get; }
        void Decompile(DecompileRule decompiler);
    }
    public class GlobalVariableExpression : ITTEExpression, ITTEVariable
    {
        public string Name { get; }

        public GlobalVariableExpression(string name)
        {
            Name = name;
        }

        public override string ToString() => "Global." + Name;
        public void Decompile(DecompileRule decompiler) => decompiler.Append(decompiler.Decompiler.GetVariableName(Name, true));
    }
    public class AnonymousVariableExpression : ITTEExpression, ITTEVariable
    {
        public string Name { get; }
        public bool IsGlobal { get; }

        public AnonymousVariableExpression(string name, bool isGlobal)
        {
            Name = name;
            IsGlobal = isGlobal;
        }

        public override string ToString() => (IsGlobal ? "Global." : "Player.") + Name;
        public void Decompile(DecompileRule decompiler) => decompiler.Append(decompiler.Decompiler.GetVariableName(Name, IsGlobal));
    }
    public class PlayerVariableExpression : ITTEExpression, ITTEVariable
    {
        public string Name { get; }
        public ITTEExpression Player { get; }

        public PlayerVariableExpression(string name, ITTEExpression player)
        {
            Name = name;
            Player = player;
        }

        public override string ToString() => Player.ToString() + "." + Name;

        public void Decompile(DecompileRule decompiler)
        {
            if (Player is FunctionExpression func && func.Function.WorkshopName == "Event Player")
                decompiler.Append(decompiler.Decompiler.GetVariableName(Name, false));
            else
            {
                Player.Decompile(decompiler);
                decompiler.Append("." + decompiler.Decompiler.GetVariableName(Name, false));
            }
        }
    }
}