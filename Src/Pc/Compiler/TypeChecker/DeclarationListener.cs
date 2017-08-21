using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.Pc.Antlr;

namespace Microsoft.Pc.TypeChecker
{
    public class DeclarationListener : PParserBaseListener
    {
        /// <summary>
        ///     Functions can be nested via anonymous event handlers, so we do need to keep track.
        /// </summary>
        private readonly Stack<Function> functionStack = new Stack<Function>();
        /// <summary>
        /// Groups can be nested
        /// </summary>
        private readonly Stack<StateGroup> groupStack = new Stack<StateGroup>();

        private readonly ParseTreeProperty<IPDecl> nodesToDeclarations;
        private readonly ParseTreeProperty<DeclarationTable> programDeclarations;

        /// <summary>
        ///     Event sets cannot be nested, so we keep track only of the most recent one.
        /// </summary>
        private EventSet currentEventSet;

        /// <summary>
        ///     Function prototypes cannot be nested, so we keep track only of the most recent one.
        /// </summary>
        private FunctionProto currentFunctionProto;

        /// <summary>
        ///     Machines cannot be nested, so we keep track of only the most recent one.
        /// </summary>
        private Machine currentMachine;

        /// <summary>
        ///     This keeps track of the current declaration table. The "on every entry/exit" rules handle popping the
        ///     stack using its Parent pointer.
        /// </summary>
        private DeclarationTable table;

        public DeclarationListener(ParseTreeProperty<DeclarationTable> programDeclarations, ParseTreeProperty<IPDecl> nodesToDeclarations)
        {
            this.programDeclarations = programDeclarations;
            this.nodesToDeclarations = nodesToDeclarations;
        }

        private Function CurrentFunction => functionStack.Count > 0 ? functionStack.Peek() : null;

        #region Events
        public override void EnterEventDecl(PParser.EventDeclContext context)
        {
            var pEvent = (PEvent) nodesToDeclarations.Get(context);

            bool hasAssume = context.cardinality()?.ASSUME() != null;
            bool hasAssert = context.cardinality()?.ASSERT() != null;
            int cardinality = int.Parse(context.cardinality()?.IntLiteral().GetText() ?? "-1");
            pEvent.Assume = hasAssume ? cardinality : -1;
            pEvent.Assert = hasAssert ? cardinality : -1;

            pEvent.PayloadType = TypeResolver.ResolveType(context.type(), table);

            if (context.annotationSet() != null)
            {
                throw new NotImplementedException("Have not implemented event annotations");
            }
        }
        #endregion

        public override void EnterNonDefaultEventList(PParser.NonDefaultEventListContext context)
        {
            // TODO: implement handlers for other parents of these event lists.
            Debug.Assert(currentEventSet != null, $"Event set not prepared for {nameof(EnterNonDefaultEventList)}");
            foreach (IToken contextEvent in context._events)
            {
                string eventName = contextEvent.Text;
                if (!table.Lookup(eventName, out PEvent evt))
                {
                    throw new MissingEventException(currentEventSet, eventName);
                }

                currentEventSet?.Events.Add(evt);
            }
        }

        public override void EnterFunDecl(PParser.FunDeclContext context)
        {
            var fun = (Function) nodesToDeclarations.Get(context);
            fun.Signature.ReturnType = TypeResolver.ResolveType(context.type(), table);

            if (context.annotationSet() != null)
            {
                throw new NotImplementedException("Function annotations not implemented");
            }

            if (context.statementBlock() == null)
            {
                throw new NotImplementedException("Foreign functions not implemented");
            }

            currentMachine?.Methods.Add(fun);
            functionStack.Push(fun);
        }

        public override void EnterFunParam(PParser.FunParamContext context)
        {
            string name = context.name.Text;
            if (currentFunctionProto != null)
            {
                // If we're in a prototype, then we don't look up a variable, we just create a formal parameter
                currentFunctionProto.Signature.Parameters.Add(
                    new FormalParameter {Name = name, Type = TypeResolver.ResolveType(context.type(), table)});
            }
            else
            {
                // Otherwise, we're in a function of some sort, and we add the variable to its signature
                bool success = table.Get(name, out Variable variable);
                Debug.Assert(success);
                CurrentFunction.Signature.Parameters.Add(variable);
            }
        }

        public override void ExitFunDecl(PParser.FunDeclContext context)
        {
            functionStack.Pop();
        }

        public override void EnterVarDecl(PParser.VarDeclContext context)
        {
            foreach (ITerminalNode varName in context.idenList().Iden())
            {
                var variable = (Variable) nodesToDeclarations.Get(varName);
                variable.Type = TypeResolver.ResolveType(context.type(), table);

                if (CurrentFunction != null)
                {
                    // Either a local variable to the current function...
                    CurrentFunction.LocalVariables.Add(variable);
                }
                else
                {
                    // ...or a field to the current machine, if no function contains it.
                    currentMachine.Fields.Add(variable);
                }
            }
        }

        public override void EnterGroup(PParser.GroupContext context)
        {
            var group = (StateGroup) nodesToDeclarations.Get(context);
            if (groupStack.Count > 0)
            {
                groupStack.Peek().SubGroups.Add(group);
            }
            groupStack.Push(group);
        }

        public override void ExitGroup(PParser.GroupContext context)
        {
            groupStack.Pop();
        }

        public override void EnterStateDecl(PParser.StateDeclContext context)
        {
            var state = (State) nodesToDeclarations.Get(context);
            // Register current state with parents
            currentState = state;
            if (groupStack.Count > 0)
            {
                groupStack.Peek().States.Add(state);
            }
            else
            {
                currentMachine.States.Add(state);
            }

            // START?
            state.IsStart = context.START() != null;
            if (state.IsStart)
            {
                if (currentMachine.StartState != null)
                {
                    throw new DuplicateStartStateException(currentMachine, state);
                }
                currentMachine.StartState = state;
            }

            // temperature=(HOT | COLD)?
            state.Temperature = context.temperature == null
                ? StateTemperature.WARM
                : context.temperature.Text.Equals("HOT")
                    ? StateTemperature.HOT
                    : StateTemperature.COLD;

            // STATE name=Iden
            // annotationSet?
            if (context.annotationSet() != null)
            {
                throw new NotImplementedException("state annotations");
            }

            // LBRACE stateBodyItem* RBRACE
            // handled by StateEntry / StateExit / StateDefer / StateIgnore / OnEventDoAction / OnEventPushState / OnEventGotoState
        }

        public override void ExitStateDecl(PParser.StateDeclContext context)
        {
            currentState = null;
        }

        public override void EnterStateEntry(PParser.StateEntryContext context)
        {
            // TODO: state entry
        }

        public override void EnterOnEventDoAction(PParser.OnEventDoActionContext context)
        {
            // TODO: on event do action
        }

        public override void EnterStateExit(PParser.StateExitContext context)
        {
            // TODO: state exit
        }

        public override void EnterOnEventGotoState(PParser.OnEventGotoStateContext context)
        {
            // TODO: on event goto state
        }

        public override void EnterStateIgnore(PParser.StateIgnoreContext context)
        {
            // TODO: state ignore
        }

        public override void EnterStateDefer(PParser.StateDeferContext context)
        {
            // TODO: state defer
        }

        public override void EnterOnEventPushState(PParser.OnEventPushStateContext context)
        {
            // TODO: on event push state
        }

        public override void EnterPayloadVarDecl(PParser.PayloadVarDeclContext context)
        {
            var variable = (Variable) nodesToDeclarations.Get(context.funParam());
            variable.Type = TypeResolver.ResolveType(context.funParam().type(), table);
            CurrentFunction.LocalVariables.Add(variable);
        }

        public override void EnterImplMachineProtoDecl(PParser.ImplMachineProtoDeclContext context)
        {
            var proto = (MachineProto) nodesToDeclarations.Get(context);
            proto.PayloadType = TypeResolver.ResolveType(context.type(), table);
        }

        public override void EnterSpecMachineDecl(PParser.SpecMachineDeclContext context)
        {
            // TODO: implement
        }

        public override void EnterFunProtoDecl(PParser.FunProtoDeclContext context)
        {
            var proto = (FunctionProto) nodesToDeclarations.Get(context);
            proto.Signature.ReturnType = TypeResolver.ResolveType(context.type(), table);
            currentFunctionProto = proto;
        }

        public override void ExitFunProtoDecl(PParser.FunProtoDeclContext context)
        {
            currentFunctionProto = null;
        }

        public override void EnterEveryRule(ParserRuleContext ctx)
        {
            DeclarationTable thisTable = programDeclarations.Get(ctx);
            if (thisTable != null)
            {
                table = thisTable;
            }
        }

        public override void ExitEveryRule(ParserRuleContext context)
        {
            if (programDeclarations.Get(context) != null)
            {
                Debug.Assert(table != null);
                table = table.Parent;
            }
        }

        #region Event sets
        public override void EnterEventSetDecl(PParser.EventSetDeclContext context)
        {
            currentEventSet = (EventSet) nodesToDeclarations.Get(context);
        }

        public override void ExitEventSetDecl(PParser.EventSetDeclContext context)
        {
            currentEventSet = null;
        }
        #endregion

        #region Interfaces
        public override void EnterInterfaceDecl(PParser.InterfaceDeclContext context)
        {
            var mInterface = (Interface) nodesToDeclarations.Get(context);
            mInterface.PayloadType = TypeResolver.ResolveType(context.type(), table);
            if (context.eventSet != null)
            {
                // Either look up the event set and establish the link by name...
                if (!table.Lookup(context.eventSet.Text, out EventSet eventSet))
                {
                    throw new MissingDeclarationException(eventSet);
                }

                mInterface.ReceivableEvents = eventSet;
            }
            else
            {
                // ... or let the nonDefaultEventList handler fill in a newly created event set
                Debug.Assert(context.nonDefaultEventList() != null);
                currentEventSet = new EventSet($"{mInterface.Name}$eventset", null);
                mInterface.ReceivableEvents = currentEventSet;
            }
        }

        public override void ExitInterfaceDecl(PParser.InterfaceDeclContext context)
        {
            if (context.eventSet == null)
            {
                currentEventSet = null;
            }
        }
        #endregion

        #region Typedefs
        public override void EnterPTypeDef(PParser.PTypeDefContext context)
        {
            var typedef = (TypeDef) nodesToDeclarations.Get(context);
            typedef.Type = TypeResolver.ResolveType(context.type(), table);
        }

        public override void EnterForeignTypeDef(PParser.ForeignTypeDefContext context)
        {
            throw new NotImplementedException("TODO: foreign types");
        }
        #endregion

        #region Enums
        /// <summary>
        ///     Enum declarations can't be nested, so we simply store the most recently encountered
        ///     one in a variable for the listener actions for the elements to access.
        /// </summary>
        private PEnum currentEnum;

        private State currentState;

        public override void EnterEnumTypeDefDecl(PParser.EnumTypeDefDeclContext context)
        {
            currentEnum = (PEnum) nodesToDeclarations.Get(context);
        }

        public override void ExitEnumTypeDefDecl(PParser.EnumTypeDefDeclContext context)
        {
            // Check that there is a default element in the enum.
            if (currentEnum.Values.All(elem => elem.Value != 0))
            {
                throw new EnumMissingDefaultException(currentEnum);
            }
        }

        public override void EnterEnumElem(PParser.EnumElemContext context)
        {
            var elem = (EnumElem) nodesToDeclarations.Get(context);
            elem.Value = currentEnum.Count; // listener visits from left-to-right, so this will count upwards correctly.
            bool success = currentEnum.AddElement(elem);
            Debug.Assert(success);
        }

        public override void EnterNumberedEnumElem(PParser.NumberedEnumElemContext context)
        {
            var elem = (EnumElem) nodesToDeclarations.Get(context);
            elem.Value = int.Parse(context.value.Text);
            bool success = currentEnum.AddElement(elem);
            Debug.Assert(success);
        }
        #endregion

        #region Machines
        public override void EnterImplMachineDecl(PParser.ImplMachineDeclContext context)
        {
            // eventDecl : MACHINE name=Iden
            currentMachine = (Machine) nodesToDeclarations.Get(context);

            // cardinality?
            bool hasAssume = context.cardinality()?.ASSUME() != null;
            bool hasAssert = context.cardinality()?.ASSERT() != null;
            int cardinality = int.Parse(context.cardinality()?.IntLiteral().GetText() ?? "-1");
            currentMachine.Assume = hasAssume ? cardinality : -1;
            currentMachine.Assert = hasAssert ? cardinality : -1;

            // annotationSet?
            if (context.annotationSet() != null)
            {
                throw new NotImplementedException("Machine annotations not yet implemented");
            }

            // (COLON idenList)?
            if (context.idenList() != null)
            {
                IEnumerable<string> interfaces = context.idenList()._names.Select(name => name.Text);
                foreach (string pInterfaceName in interfaces)
                {
                    if (!table.Lookup(pInterfaceName, out Interface pInterface))
                    {
                        throw new MissingDeclarationException(pInterface);
                    }

                    currentMachine.Interfaces.Add(pInterface);
                }
            }

            // receivesSends*
            // handled by EnterReceivesSends

            // machineBody
            // handled by EnterVarDecl / EnterFunDecl / EnterGroup / EnterStateDecl
        }

        public override void ExitImplMachineDecl(PParser.ImplMachineDeclContext context)
        {
            currentMachine = null;
        }

        public override void EnterReceivesSends(PParser.ReceivesSendsContext context)
        {
            if (context.RECEIVES() != null)
            {
                if (currentMachine.Receives == null)
                {
                    currentMachine.Receives = new EventSet($"{currentMachine.Name}$receives", null);
                }
                currentEventSet = currentMachine.Receives;
            }
            else if (context.SENDS() != null)
            {
                if (currentMachine.Sends == null)
                {
                    currentMachine.Sends = new EventSet($"{currentMachine.Name}$sends", null);
                }
                currentEventSet = currentMachine.Sends;
            }
            else
            {
                Debug.Fail("A receives / sends spec had neither a receives nor sends.");
            }
        }

        public override void ExitReceivesSends(PParser.ReceivesSendsContext context)
        {
            currentEventSet = null;
        }
        #endregion
    }

    public class DuplicateStartStateException : Exception
    {
        public Machine CurrentMachine { get; }
        public State ConflictingStartState { get; }

        public DuplicateStartStateException(Machine currentMachine, State conflictingStartState)
        {
            this.CurrentMachine = currentMachine;
            this.ConflictingStartState = conflictingStartState;
        }
    }
}