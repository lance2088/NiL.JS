using NiL.JS.Core;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace NiL.JS.Modules
{
    public sealed class TypeProxy : JSObject
    {
        private static object[] convertArgs(JSObject[] source)
        {
            if (source == null)
                return null;
            object[] res = new object[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                    continue;
                var obj = source[i];
                var v = obj.Value;
                if (v is Core.BaseTypes.Array)
                {
                    var arr = v as Core.BaseTypes.Array;
                    var arg = new object[arr.length];
                    for (var j = 0; j < arg.Length; j++)
                        arg[j] = arr[j].Value;
                    res[i] = arg;
                }
                else
                    res[i] = v;
            }
            return res;
        }

        private static object[] convertArgs(JSObject[] source, ParameterInfo[] targetTypes)
        {
            int targetCount = targetTypes.Length;
            object[] res = new object[targetCount];
            if (source != null)
                targetCount = System.Math.Min(targetCount, source.Length);
            for (int i = 0; i < targetCount; i++)
            {
                if (source == null || source[i] == null)
                    continue;
                var obj = source[i];
                if (targetTypes[i].ParameterType == typeof(JSObject))
                    res[i] = obj;
                else
                {
                    var v = obj.Value;
                    if (v is Core.BaseTypes.Array)
                    {
                        var arr = v as Core.BaseTypes.Array;
                        var arg = new object[arr.length];
                        for (var j = 0; j < arg.Length; j++)
                            arg[j] = arr[j].Value;
                        res[i] = arg;
                    }
                    else
                        res[i] = v;
                }
            }
            return res;
        }

        private class ProxyPrototype : JSObject
        {
            private MethodInfo getItem;
            private MethodInfo setItem;
            private Dictionary<string, JSObject> cache;

            public ProxyPrototype(Type type)
            {
                if (type == null)
                    throw new NullReferenceException();
                oValue = type;
                ValueType = ObjectValueType.Object;
                assignCallback = ErrorAssignCallback;
                cache = new Dictionary<string, JSObject>();
                getItem = type.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                setItem = type.GetMethod("set_Item", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            private JSObject convert(MethodInfo method)
            {
                var result = new CallableField((th, args) =>
                {
                    var res = method.Invoke(th.thisBind.oValue ?? th.thisBind.firstContainer ?? th.thisBind, convertArgs(args, method.GetParameters()));
                    if (res == null)
                        return null;
                    else if (res is JSObject)
                        return res as JSObject;
                    else if (res is int)
                        return (int)res;
                    else if (res is long)
                        return (long)res;
                    else if (res is double)
                        return (double)res;
                    else if (res is string)
                        return (string)res;
                    else if (res is bool)
                        return (bool)res;
                    else if (res is ContextStatement)
                        return (JSObject)(ContextStatement)res;
                    else return TypeProxy.Proxy(res);
                });
                return result;
            }

            public override JSObject GetField(string name, bool fast, bool own)
            {
                JSObject r = null;
                var hostedType = (Type)oValue;
                if (cache.TryGetValue(name, out r) && r.ValueType >= ObjectValueType.Undefined)
                    return r;
                var m = hostedType.GetMember(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                if (m.Length > 1)
                    throw new InvalidOperationException("Too many fields with name " + name);
                if (m.Length == 0 || m[0].GetCustomAttribute(typeof(HiddenAttribute)) != null)
                {
                    r = DefaultFieldGetter(name, fast, own);
                    if (r != undefined && r != Null && r.ValueType != ObjectValueType.NotExistInObject)
                        return r;
                    switch (name)
                    {
                        case "constructor":
                            {
                                TypeProxy res = null;
                                if (!constructors.TryGetValue(hostedType, out res))
                                    res = new TypeProxy(hostedType);
                                cache[name] = res;
                                return res;
                            }
                        case "valueOf":
                            {
                                JSObject res = new CallableField((th, args) =>
                                {
                                    return th.thisBind;
                                });
                                cache[name] = res;
                                return res;
                            }
                    }
                    var i = 0;
                    var d = 0;
                    var gprms = getItem != null ? getItem.GetParameters() : null;
                    var sprms = setItem != null ? setItem.GetParameters() : null;
                    if (((sprms ?? gprms) != null) &&
                        ((gprms ?? sprms)[0].ParameterType == typeof(string)
                        || (Parser.ParseNumber(name, ref i, false, out i) && (gprms ?? sprms)[0].ParameterType == typeof(int))
                        || (Parser.ParseNumber(name, ref i, false, out d) && (gprms ?? sprms)[0].ParameterType == typeof(double))
                        || ((gprms ?? sprms)[0].ParameterType == typeof(object))))
                    {
                        var ptype = (sprms ?? gprms)[0].ParameterType;
                        object index = ptype == typeof(double) ? (object)d : (object)(ptype == typeof(int) ? i : (object)name);
                        return new JSObject()
                        {
                            ValueType = ObjectValueType.Property,
                            oValue = new Statement[] 
                            {
                                new NiL.JS.Statements.ExternalFunction(new CallableField((_th, args) =>
                                {
                                    object[] a = null;
                                    if (sprms[1].ParameterType != typeof(JSObject))
                                    {
                                        args = new JSObject[] { null, args[0] };
                                        a = convertArgs(args, sprms);
                                        a[0] = sprms[0].ParameterType == typeof(string) ? name : (object)(sprms[0].ParameterType == typeof(int) ? i : (object)d);
                                    }
                                    else
                                        a = new object[] { index, args[0] };
                                    setItem.Invoke(oValue, a);
                                    return undefined;
                                })),
                                new NiL.JS.Statements.ExternalFunction(new CallableField((_th, args) =>
                                {
                                    var res = getItem.Invoke(oValue, new object[] { index });
                                    if (res is JSObject)
                                        return res as JSObject;
                                    else if (res is int)
                                        return (int)res;
                                    else if (res is double || res is long)
                                        return (double)res;
                                    else if (res is string)
                                        return (string)res;
                                    else if (res is bool)
                                        return (bool)res;
                                    else if (res is ContextStatement)
                                        return (JSObject)(ContextStatement)res;
                                    else return new TypeProxy(res.GetType())
                                    {
                                        oValue = res
                                    };
                                })) 
                            }
                        };
                    }
                    return r;
                }
                switch (m[0].MemberType)
                {
                    case MemberTypes.Constructor:
                        {
                            var method = (ConstructorInfo)m[0];
                            r = new CallableField((th, args) =>
                            {
                                var res = method.Invoke(args);
                                if (res is JSObject)
                                    return res as JSObject;
                                else if (res is int)
                                    return (int)res;
                                else if (res is double || res is long)
                                    return (double)res;
                                else if (res is string)
                                    return (string)res;
                                else if (res is bool)
                                    return (bool)res;
                                else if (res is ContextStatement)
                                    return (JSObject)(ContextStatement)res;
                                else
                                    return TypeProxy.Proxy(res);
                            });
                            break;
                        }
                    case MemberTypes.Method:
                        {
                            var method = (MethodInfo)m[0];
                            r = convert(method);
                            break;
                        }
                    case MemberTypes.Field:
                        {
                            var field = (m[0] as FieldInfo);
                            object res = field.GetValue(this);
                            if (res is JSObject)
                                r = res as JSObject;
                            else
                            {
                                if (res is int)
                                    r = (int)res;
                                else if (res is double || res is long)
                                    r = (double)res;
                                else if (res is string)
                                    r = (string)res;
                                else if (res is bool)
                                    r = (bool)res;
                                else if (res is ContextStatement)
                                    r = (JSObject)(ContextStatement)res;
                                else
                                    r = TypeProxy.Proxy(res);
                                r.assignCallback = null;
                            }
                            break;
                        }
                    case MemberTypes.Property:
                        {
                            var pinfo = (PropertyInfo)m[0];
                            r = new JSObject()
                            {
                                ValueType = ObjectValueType.Property,
                                oValue = new Statement[] { 
                                    pinfo.CanWrite ? convert(pinfo.SetMethod).oValue as Statement : null,
                                    pinfo.CanRead ? convert(pinfo.GetMethod).oValue as Statement : null 
                                }
                            };
                            break;
                        }
                    default: throw new NotImplementedException("Convertion from " + m[0].MemberType + " not implemented");
                }
                if (m[0].GetCustomAttributes(typeof(ProtectedAttribute), false).Length != 0)
                    r.Protect();
                cache[name] = r;
                r.attributes |= ObjectAttributes.DontDelete | ObjectAttributes.DontEnum;
                return r;
            }
        }

        private static readonly Dictionary<Type, TypeProxy> constructors = new Dictionary<Type, TypeProxy>();
        private static readonly Dictionary<Type, ProxyPrototype> prototypes = new Dictionary<Type, ProxyPrototype>();

        public static JSObject Proxy(object obj)
        {
            var type = obj.GetType();
            ProxyPrototype prot = null;
            if (!prototypes.TryGetValue(type, out prot))
            {
                new TypeProxy(type);
                prot = prototypes[type];
            }
            return new JSObject(false) { oValue = obj, ValueType = ObjectValueType.Object, prototype = prot };
        }

        private Type hostedType;
        private MethodInfo getItem;
        private MethodInfo setItem;
        private Dictionary<string, JSObject> cache;

        public TypeProxy(Type type)
        {
            ValueType = ObjectValueType.Statement;
            hostedType = type;
            JSObject proto = null;
            TypeProxy exconst = null;
            assignCallback = ErrorAssignCallback;
            cache = new Dictionary<string, JSObject>();
            getItem = type.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            setItem = type.GetMethod("set_Item", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (constructors.TryGetValue(type, out exconst))
            {
                oValue = exconst.oValue;
                DefaultFieldGetter("prototype", false, false).Assign(prototypes[type]);
            }
            else
            {
                oValue = new Statements.ExternalFunction((x, y) =>
                {
                    object[] args = null;
                    var constructor = type.GetConstructor(y != null ? new Type[] { y.GetType() } : Type.EmptyTypes);
                    if (constructor == null)
                    {
                        constructor = type.GetConstructor(Type.GetTypeArray(y));
                        if (constructor == null)
                        {
                            constructor = type.GetConstructor(new Type[] { typeof(object[]) });
                            if (constructor == null)
                            {
                                constructor = type.GetConstructor(Type.EmptyTypes);
                                args = null;
                            }
                            else
                                args = new object[] { args };
                        }
                        else
                            args = (object[])y;
                    }
                    else
                        args = y != null ? new object[] { y } : null;
                    var _this = x.thisBind;
                    bool bynew = _this != null && _this.prototype != null && _this.prototype.ValueType == ObjectValueType.Object && _this.prototype.oValue == type as object;
                    var obj = constructor.Invoke(args);
                    var res = obj is JSObject && !bynew ? obj as JSObject : new TypeProxy(type)
                    {
                        oValue = constructor.Invoke(args)
                    };
                    res.prototype = proto;
                    if (bynew)
                        _this.firstContainer = res;
                    return res;
                });
                constructors[type] = this;
                prototypes[type] = new ProxyPrototype(type);
                proto = DefaultFieldGetter("prototype", false, false);
                proto.Assign(prototypes[type]);
                proto.attributes |= ObjectAttributes.DontDelete | ObjectAttributes.DontEnum;
            }
        }

        private JSObject convert(MethodInfo method)
        {
            JSObject result = null;
            if (method.ReturnType == typeof(JSObject))
            {
                if ((method.GetParameters().Length == 1) && (method.GetParameters()[0].ParameterType == typeof(JSObject[])))
                {
                    var dinv = (Func<JSObject[], JSObject>)Delegate.CreateDelegate(typeof(Func<JSObject[], JSObject>), null, method);
                    result = new CallableField((th, args) =>
                    {
                        return dinv(args);
                    });
                    return result;
                }
                else if (method.GetParameters().Length == 0)
                {
                    var dinv = (Func<JSObject>)Delegate.CreateDelegate(typeof(Func<JSObject>), null, method);
                    result = new CallableField((th, args) =>
                    {
                        return dinv();
                    });
                    return result;
                }
            }
            result = new CallableField((th, args) =>
            {
                var res = method.Invoke(null, convertArgs(args, method.GetParameters()));
                if (res == null)
                    return null;
                else if (res is JSObject)
                    return res as JSObject;
                else if (res is int)
                    return (int)res;
                else if (res is long)
                    return (long)res;
                else if (res is double)
                    return (double)res;
                else if (res is string)
                    return (string)res;
                else if (res is bool)
                    return (bool)res;
                else if (res is ContextStatement)
                    return (JSObject)(ContextStatement)res;
                else return TypeProxy.Proxy(res);
            });
            return result;
        }

        public override JSObject GetField(string name, bool fast, bool own)
        {
            JSObject r = null;
            if (cache.TryGetValue(name, out r) && r.ValueType >= ObjectValueType.Undefined)
                return r;
            var m = hostedType.GetMember(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            if (m.Length > 1)
                throw new InvalidOperationException("Too many fields with name " + name);
            if (m.Length == 0 || m[0].GetCustomAttribute(typeof(HiddenAttribute)) != null)
            {
                r = DefaultFieldGetter(name, fast, own);
                if (r != undefined && r != Null && r.ValueType != ObjectValueType.NotExistInObject)
                    return r;
                var i = 0;
                var d = 0;
                var gprms = getItem != null ? getItem.GetParameters() : null;
                var sprms = setItem != null ? setItem.GetParameters() : null;
                if (((sprms ?? gprms) != null) &&
                    ((gprms ?? sprms)[0].ParameterType == typeof(string)
                    || (Parser.ParseNumber(name, ref i, false, out i) && (gprms ?? sprms)[0].ParameterType == typeof(int))
                    || (Parser.ParseNumber(name, ref i, false, out d) && (gprms ?? sprms)[0].ParameterType == typeof(double))
                    || ((gprms ?? sprms)[0].ParameterType == typeof(object))))
                {
                    var ptype = (sprms ?? gprms)[0].ParameterType;
                    object index = ptype == typeof(double) ? (object)d : (object)(ptype == typeof(int) ? i : (object)name);
                    return new JSObject()
                    {
                        ValueType = ObjectValueType.Property,
                        oValue = new Statement[] 
                            {
                                new NiL.JS.Statements.ExternalFunction(new CallableField((_th, args) =>
                                {
                                    object[] a = null;
                                    if (sprms[1].ParameterType != typeof(JSObject))
                                    {
                                        args = new JSObject[] { null, args[0] };
                                        a = convertArgs(args, sprms);
                                        a[0] = sprms[0].ParameterType == typeof(string) ? name : (object)(sprms[0].ParameterType == typeof(int) ? i : (object)d);
                                    }
                                    else
                                        a = new object[] { index, args[0] };
                                    setItem.Invoke(oValue, a);
                                    return undefined;
                                })),
                                new NiL.JS.Statements.ExternalFunction(new CallableField((_th, args) =>
                                {
                                    var res = getItem.Invoke(oValue, new object[] { index });
                                    if (res is JSObject)
                                        return res as JSObject;
                                    else if (res is int)
                                        return (int)res;
                                    else if (res is double || res is long)
                                        return (double)res;
                                    else if (res is string)
                                        return (string)res;
                                    else if (res is bool)
                                        return (bool)res;
                                    else if (res is ContextStatement)
                                        return (JSObject)(ContextStatement)res;
                                    else return new TypeProxy(res.GetType())
                                    {
                                        oValue = res
                                    };
                                })) 
                            }
                    };
                }
                return r;
            }
            switch (m[0].MemberType)
            {
                case MemberTypes.Constructor:
                    {
                        var method = (ConstructorInfo)m[0];
                        r = new CallableField((th, args) =>
                        {
                            var res = method.Invoke(args);
                            if (res is JSObject)
                                return res as JSObject;
                            else if (res is int)
                                return (int)res;
                            else if (res is double || res is long)
                                return (double)res;
                            else if (res is string)
                                return (string)res;
                            else if (res is bool)
                                return (bool)res;
                            else if (res is ContextStatement)
                                return (JSObject)(ContextStatement)res;
                            else
                                return TypeProxy.Proxy(res);
                        });
                        break;
                    }
                case MemberTypes.Method:
                    {
                        var method = (MethodInfo)m[0];
                        r = convert(method);
                        break;
                    }
                case MemberTypes.Field:
                    {
                        var field = (m[0] as FieldInfo);
                        object res = field.GetValue(this);
                        if (res is JSObject)
                            r = res as JSObject;
                        else
                        {
                            if (res is int)
                                r = (int)res;
                            else if (res is double || res is long)
                                r = (double)res;
                            else if (res is string)
                                r = (string)res;
                            else if (res is bool)
                                r = (bool)res;
                            else if (res is ContextStatement)
                                r = (JSObject)(ContextStatement)res;
                            else
                                r = TypeProxy.Proxy(res);
                            r.assignCallback = null;
                        }
                        break;
                    }
                case MemberTypes.Property:
                    {
                        var pinfo = (PropertyInfo)m[0];
                        r = new JSObject()
                        {
                            ValueType = ObjectValueType.Property,
                            oValue = new Statement[] { 
                                    pinfo.CanWrite ? convert(pinfo.SetMethod).oValue as Statement : null,
                                    pinfo.CanRead ? convert(pinfo.GetMethod).oValue as Statement : null 
                                }
                        };
                        break;
                    }
                default: throw new NotImplementedException("Convertion from " + m[0].MemberType + " not implemented");
            }
            if (m[0].GetCustomAttributes(typeof(ProtectedAttribute), false).Length != 0)
                r.Protect();
            cache[name] = r;
            r.attributes |= ObjectAttributes.DontDelete | ObjectAttributes.DontEnum;
            return r;
        }
    }
}