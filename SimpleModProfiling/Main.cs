using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.Text;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;


namespace SimpleModProfiling
{
    public class Main : Mod
    {
        public static Main instance;
        public static Thread mainThread;
        Harmony harmony;

        public void Start()
        {
            mainThread = Thread.CurrentThread;
            instance = this;
            harmony = new Harmony("com.aidanamite.SimpleModProfiling");
            Patch();
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            Patch_Updates.logging = false;
            harmony.UnpatchAll(harmony.Id);
            Log("Mod has been unloaded!");
        }

        void Patch()
        {
            var h = new HashSet<MethodBase>(harmony.GetPatchedMethods());
            foreach (var m in Patch_Updates.TargetMethods())
                if (!h.Contains(m))
                    try
                    {
                        harmony.Patch(m, new HarmonyMethod(typeof(Patch_Updates), "Prefix"), finalizer: new HarmonyMethod(typeof(Patch_Updates), "Finalizer"));
                    }
                    catch { }
        }

        [ConsoleCommand(name: "ReinitializeProfiling", docs: "Reapplies the mod's profiling systems. Should be used if mods were loaded after the Simple Mod Profiling mod")]
        public static string ReinitializeProfilingCommand(string[] args)
        {
            instance.Patch();
            return "Patches Checked";
        }

        [ConsoleCommand(name: "ProfileMods", docs: "Syntax: 'ProfileMods <seconds>' Starts profiling for the specified amount of time and displays the results")]
        public static string ProfileModsCommand(string[] args)
        {
            if (Patch_Updates.logging)
                return "A profiling operation is already running. Use the command CancelProfile to stop it before starting a new one";
            if (args == null || args.Length < 1)
                return "Not enough arguments";
            if (!double.TryParse(args[0], out var v))
                return "Could not parse " + args[0] + " as a number";
            instance.StartCoroutine(GetLog(v));
            return "Started";
        }
        [ConsoleCommand(name: "CancelProfile", docs: "Cancels a currently running profiling displaying the gathered results")]
        public static string CancelProfileCommand(string[] args)
        {
            if (!Patch_Updates.logging)
                return "Not currently profiling. Cannot cancel";
            Patch_Updates.logging = false;
            return "Profiling canceled";
        }
        static IEnumerator GetLog(double time)
        {
            var s = DateTime.UtcNow.AddSeconds(time);
            Patch_Updates.logging = true;
            while (DateTime.UtcNow < s && Patch_Updates.logging)
                yield return new WaitForEndOfFrame();
            Patch_Updates.logging = false;
            var sum = 0L;
            var sums = new Dictionary<ArrayKey<Mod>, Su>();
            foreach (var i in Patch_Updates.log)
            {
                var mo = new ArrayKey<Mod>(i.Key.Keys.Select(x => Patch_Updates.associate[x.DeclaringType]).ToArray());
                if (!sums.TryGetValue(mo, out var v))
                    sums[mo] = v = new Su();
                sum += i.Value;
                v.sum += i.Value;
                v.items.Add((i.Value, i.Key));
            }
            var l = sums.ToList();
            l.Sort((x, y) => y.Value.sum.CompareTo(x.Value.sum));
            var t = new StringBuilder("Profiling Complete!\nTotal: ");
            t.Append(new TimeSpan(sum).TotalMilliseconds);
            t.Append("ms");
            foreach (var p in l)
            {
                t.Append("\n ├─[");
                for (int i = 0; i < p.Key.Keys.Length; i++)
                {
                    if (i > 0)
                        t.Append(" > ");
                    t.Append(p.Key.Keys[i].name);
                }
                t.Append("] Total: ");
                t.Append(new TimeSpan(p.Value.sum).TotalMilliseconds);
                t.Append("ms");
                p.Value.items.Sort((x, y) => y.Item1.CompareTo(x.Item1));
                foreach (var i in p.Value.items)
                {
                    t.Append("\n ├───[");
                    for (int j = 0; j < p.Key.Keys.Length; j++)
                    {
                        if (j > 0)
                            t.Append(" > ");
                        t.Append(i.Item2.Keys[j].DeclaringType);
                        t.Append("::");
                        t.Append(i.Item2.Keys[j]);
                    }
                    t.Append("] = ");
                    t.Append(new TimeSpan(i.Item1).TotalMilliseconds);
                    t.Append("ms");
                }
            }
            Debug.Log(t.ToString());
            Patch_Updates.log.Clear();
            yield break;
        }

        class Su
        {
            public long sum = 0;
            public List<(long, ArrayKey<MethodBase>)> items = new List<(long, ArrayKey<MethodBase>)>();
        }
    }

    public class HashableStackTrace
    {
        public readonly StackTrace trace = new StackTrace(2);
        public HashableStackTrace() { }
        public override bool Equals(object obj)
        {
            var o = obj is HashableStackTrace h ? h.trace : obj is StackTrace t ? t : null;
            if (o != null)
                return Equals(trace, o);
            return base.Equals(obj);
        }
        public override int GetHashCode() => GetHashCode(trace);
        public MethodBase GetTop() => trace.GetFrame(0)?.GetMethod();
        public static int GetHashCode(StackFrame obj) => (obj.GetMethod()?.GetHashCode() ?? 0) ^ obj.GetNativeOffset();
        public static bool Equals(StackFrame a, StackFrame b)
        {
            var aM = a.GetMethod();
            var bM = b.GetMethod();
            if (aM != bM)
                return false;
            if (a.GetNativeOffset() != b.GetNativeOffset())
                return false;
            return true;
        }
        public static int GetHashCode(StackTrace obj)
        {
            var r = 0;
            foreach (var f in obj.GetFrames())
                r ^= GetHashCode(f);
            return r;
        }
        public static bool Equals(StackTrace a, StackTrace b)
        {
            var c = a.FrameCount;
            if (c != b.FrameCount)
                return false;
            for (int i = 0; i < c; i++)
                if (!Equals(a.GetFrame(i), b.GetFrame(i)))
                    return false;
            return true;
        }
        public static bool operator ==(HashableStackTrace a, HashableStackTrace b) => a.Equals(b);
        public static bool operator !=(HashableStackTrace a, HashableStackTrace b) => !(a == b);
        public override string ToString() => trace.ToString();
    }

    public class ArrayKey<T>
    {
        public readonly T[] Keys;
        readonly int hash;
        public ArrayKey(T[] keys)
        {
            Keys = keys;
            hash = 0;
            foreach (var m in keys)
                hash ^= m.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            if (obj is ArrayKey<T> k)
                return k.Keys.EqualsOtherArray(Keys);
            return base.Equals(obj);
        }
        public override int GetHashCode() => hash;
        public static bool operator ==(ArrayKey<T> a, ArrayKey<T> b) => a.Equals(b);
        public static bool operator !=(ArrayKey<T> a, ArrayKey<T> b) => !(a == b);
    }

    static class Patch_Updates
    {
        public static Dictionary<Type, Mod> associate = new Dictionary<Type, Mod>();

        public static bool logging = false;
        public static Dictionary<ArrayKey<MethodBase>, long> log = new Dictionary<ArrayKey<MethodBase>, long>();
        public static IEnumerable<MethodBase> TargetMethods()
        {
            associate.Clear();
            var l = new List<MethodBase>();
            foreach (var mod in ModManagerPage.activeModInstances)
                if (mod && mod != Main.instance)
                    try
                    {
                        foreach (var j in mod.GetType().Assembly.GetTypes())
                        {
                            associate[j] = mod;
                            if (!j.ContainsGenericParameters)
                                foreach (var m in j.GetMethods(~BindingFlags.Default))
                                    if (m.HasMethodBody() && !m.ContainsGenericParameters)
                                        l.Add(m);
                        }
                    }
                    catch { }
            return l;
        }
        static List<(MethodBase,long)> start = new List<(MethodBase, long)>();
        public static void Prefix(out bool __state, MethodBase __originalMethod)
        {
            if (__state = (logging && Main.mainThread == Thread.CurrentThread && (start.Count == 0 || associate[start[start.Count - 1].Item1.DeclaringType] != associate[__originalMethod.DeclaringType])))
                start.Add((__originalMethod, DateTime.UtcNow.Ticks));
        }
        public static void Finalizer(bool __state)
        {
            if (__state && start.Count > 0)
            {
                var v = start[start.Count - 1];
                (var m, var t) = (v.Item1,DateTime.UtcNow.Ticks - v.Item2);
                var k = new ArrayKey<MethodBase>(start.Select(x => x.Item1).ToArray());
                log.TryGetValue(k, out var s);
                log[k] = s + t;
                start.RemoveAt(start.Count - 1);
                for (int i = 0; i < start.Count; i++)
                {
                    var j = start[i];
                    j.Item2 += t;
                    start[i] = j;
                }
            }
        }
    }
}