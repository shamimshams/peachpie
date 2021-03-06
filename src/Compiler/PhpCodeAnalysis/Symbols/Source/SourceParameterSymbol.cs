﻿using Pchp.Syntax.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Pchp.Syntax;
using System.Diagnostics;

namespace Pchp.CodeAnalysis.Symbols
{
    /// <summary>
    /// Represents a PHP function parameter.
    /// </summary>
    internal sealed class SourceParameterSymbol : ParameterSymbol
    {
        readonly SourceRoutineSymbol _routine;
        readonly FormalParam _syntax;
        readonly int _index;
        readonly PHPDocBlock.ParamTag _ptagOpt;

        TypeSymbol _lazyType;

        public SourceParameterSymbol(SourceRoutineSymbol routine, FormalParam syntax, int index, PHPDocBlock.ParamTag ptagOpt)
        {
            Contract.ThrowIfNull(routine);
            Contract.ThrowIfNull(syntax);
            Debug.Assert(index >= 0);

            _routine = routine;
            _syntax = syntax;
            _index = index;
            _ptagOpt = ptagOpt;
        }

        /// <summary>
        /// Containing routine.
        /// </summary>
        internal SourceRoutineSymbol Routine => _routine;

        public override Symbol ContainingSymbol => _routine;

        internal override PhpCompilation DeclaringCompilation => _routine.DeclaringCompilation;

        internal override IModuleSymbol ContainingModule => _routine.ContainingModule;

        public override NamedTypeSymbol ContainingType => _routine.ContainingType;

        public override string Name => _syntax.Name.Value;

        public override bool IsThis => false;

        public FormalParam Syntax => _syntax;

        internal sealed override TypeSymbol Type
        {
            get
            {
                if (_lazyType == null)
                {
                    _lazyType = ResolveType();
                }

                return _lazyType;
            }
        }

        TypeSymbol ResolveType()
        {
            if (IsThis)
            {
                // <this> parameter
                if (_routine is SourceGlobalMethodSymbol)
                {
                    // "AnyType" in case of $this in global scope
                    return DeclaringCompilation.CoreTypes.PhpValue;
                }

                return ContainingType;
            }

            //return DeclaringCompilation.GetTypeFromTypeRef(_routine, _routine.ControlFlowGraph.GetParamTypeMask(this));

            // determine parameter type from the signature:

            // aliased parameter:
            if (_syntax.IsOut || _syntax.PassedByRef)
            {
                return DeclaringCompilation.CoreTypes.PhpAlias;
            }

            // 1. specified type hint
            var typehint = new Utilities.TypeHintValue(_syntax.TypeHint);
            var result = typehint.AsTypeSymbol(DeclaringCompilation);

            // 2. optionally type specified in PHPDoc
            if (result == null && _ptagOpt != null && _ptagOpt.TypeNamesArray.Length != 0)
            {
                var typectx = _routine.TypeRefContext;
                var tmask = FlowAnalysis.PHPDoc.GetTypeMask(typectx, _ptagOpt.TypeNamesArray);
                if (!tmask.IsVoid && !tmask.IsAnyType)
                {
                    result = DeclaringCompilation.GetTypeFromTypeRef(typectx, tmask);
                }
            }

            // 3 default:
            if (result == null)
            {
                // TODO: use type from overriden method

                result = DeclaringCompilation.CoreTypes.PhpValue;
            }

            // variadic (result[])
            if (_syntax.IsVariadic)
            {
                // result = ArraySZSymbol.FromElement(result);
                throw new NotImplementedException();
            }

            //
            return result;
        }

        public override RefKind RefKind
        {
            get
            {
                //if (_syntax.IsOut)
                //    return RefKind.Out;

                return RefKind.None;
            }
        }

        public override bool IsParams => _syntax.IsVariadic;

        public override bool IsImplicitlyDeclared
        {
            get
            {
                return base.IsImplicitlyDeclared;
            }
        }

        public override int Ordinal => _index;

        public override ImmutableArray<Location> Locations
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override bool IsOptional => _syntax.InitValue != null;

        internal override ConstantValue ExplicitDefaultConstantValue
        {
            get
            {
                if (IsOptional)
                {
                    // NOTE: the constant does not have to have the exact same type as the parameter, it is up to the caller of the method to process DefaultValue and convert it if necessary

                    // TODO: Bind _syntax.InitValue to BoundExpression

                    var value = Semantics.SemanticsBinder.TryGetConstantValue(this.DeclaringCompilation, _syntax.InitValue);
                    if (value == null)
                    {
                        // TODO: non-literal default values (like array()) must be handled by creating a method overload calling this method:

                        // Template:
                        // foo($a = [], $b = [1, 2, 3]) =>
                        // + foo($a, $b){ /* this routine */ }
                        // + foo($a) => foo($a, [1, 2, 3])
                        // + foo() => foo([], [1, 2, 3)
                    }

                    //
                    Debug.Assert(value != null, $"Not implemented default value '{_syntax.InitValue.GetType().Name}'");
                    return value;
                }

                return null;
            }
        }
    }
}
