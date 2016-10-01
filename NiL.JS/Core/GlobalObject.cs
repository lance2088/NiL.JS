﻿using System;
using System.Collections.Generic;
using NiL.JS.BaseLibrary;

namespace NiL.JS.Core
{
#if !(PORTABLE || NETCORE)
    [Serializable]
#endif
    internal sealed class GlobalObject : JSObject
    {
        private Context _context;

        internal override JSObject GetDefaultPrototype()
        {
            throw new InvalidOperationException();
        }

        public GlobalObject(Context context)
            : base()
        {
            _attributes = JSValueAttributesInternal.SystemObject;
            _context = context;
            _fields = context._variables;
            _valueType = JSValueType.Object;
            _oValue = this;
            __prototype = context.GlobalContext._GlobalPrototype;
        }

        internal protected override JSValue GetProperty(JSValue key, bool forWrite, PropertyScope memberScope)
        {
            if (memberScope < PropertyScope.Super && key._valueType != JSValueType.Symbol)
            {
                var nameStr = key.ToString();
                var res = _context.GetVariable(nameStr, forWrite);
                return res;
            }

            return base.GetProperty(key, forWrite, memberScope);
        }

        protected internal override IEnumerator<KeyValuePair<string, JSValue>> GetEnumerator(bool hideNonEnumerable, EnumerationMode enumerationMode)
        {
            foreach (var i in _context._variables)
                if (i.Value.Exists && (!hideNonEnumerable || (i.Value._attributes & JSValueAttributesInternal.DoNotEnumerate) == 0))
                    yield return i;

            foreach (var i in _context.GlobalContext._variables)
                if (i.Value.Exists && (!hideNonEnumerable || (i.Value._attributes & JSValueAttributesInternal.DoNotEnumerate) == 0))
                    yield return i;
        }

        public override string ToString()
        {
            return "[object global]";
        }
    }
}
