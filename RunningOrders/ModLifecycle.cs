// ModLifecycle.cs - shared by Tetz95's 0.6 DLL mods (master copy: projects/Railroader/ModLifecycle.cs; each mod carries
// a copy, kept in sync by hand). The loading rules of MOD-CODE-CHECKLIST.md (rules 3-6), from Ashenmare's audit of
// Locomotive Traction 2.10.0:
//   3. unloading really unloads: Stop() takes the patches off, Resume() puts them back (the mod re-adds its own UI);
//   4. one broken patch costs one feature: PatchAll() patches class by class;
//   5. no throwing static initializers: Reflect() looks a game member up once, logs if it's gone and returns null;
//   6. LogOnce() for errors in paths that run every frame.
// Usage, in the mod's entry point:
//   OnModLoad:   if (started) { Main.Resume(context); return; }   (first time: Main.Start)
//   OnModUnload: Main.Stop();
//   Main.Start:  ModLifecycle.Init(id, msg => log.Info(msg), (msg, ex) => log.Error(ex, msg)); ModLifecycle.Patch(); ...
//   Main.Stop:   remove settings section / UI, Active = false, ModLifecycle.Unpatch();
//   Main.Resume: ModLifecycle.Patch(); re-add settings section / UI, Active = true; reset run-once latches.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

internal static class ModLifecycle
{
	private static Harmony _harmony;
	private static Action<string> _log;
	private static Action<string, Exception> _error;
	private static readonly HashSet<string> Logged = new HashSet<string>();

	// False between OnModUnload and the next OnModLoad: code the patches don't cover (event handlers, MonoBehaviours,
	// console commands, key bindings) checks it and does nothing.
	internal static bool Active { get; private set; }

	internal static string Id => _harmony?.Id;

	internal static void Init(string harmonyId, Action<string> log, Action<string, Exception> error)
	{
		_harmony ??= new Harmony(harmonyId);
		_log = log;
		_error = error;
	}

	// Class by class: PatchAll stops at the first class that fails and leaves the rest unpatched. The number that failed.
	internal static int Patch(Assembly assembly = null)
	{
		int failed = 0;
		foreach (Type t in AccessTools.GetTypesFromAssembly(assembly ?? Assembly.GetExecutingAssembly()))
		{
			if (!t.IsDefined(typeof(HarmonyPatch), false))
			{
				continue;
			}
			try
			{
				_harmony.CreateClassProcessor(t).Patch();
			}
			catch (Exception ex)
			{
				failed++;
				_error?.Invoke($"Patch {t.Name} not applied (a game update?); its feature is off:", ex);
			}
		}
		if (failed > 0)
		{
			_log?.Invoke($"{failed} patch class(es) not applied: see the errors above.");
		}
		Active = true;
		return failed;
	}

	// A patch applied by hand (another mod's method, a target found at run time): logged instead of thrown.
	internal static bool PatchMethod(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null,
		HarmonyMethod transpiler = null, HarmonyMethod finalizer = null)
	{
		if (target == null)
		{
			return false;
		}
		try
		{
			_harmony.Patch(target, prefix, postfix, transpiler, finalizer);
			return true;
		}
		catch (Exception ex)
		{
			_error?.Invoke($"Patch on {target.DeclaringType?.Name}.{target.Name} not applied:", ex);
			return false;
		}
	}

	internal static Harmony Harmony => _harmony;

	// OnModUnload: every patch of this mod's Harmony id comes off (an unticked mod leaves the game as it is without it).
	internal static void Unpatch()
	{
		Active = false;
		try
		{
			_harmony?.UnpatchAll(_harmony.Id);
		}
		catch (Exception ex)
		{
			_error?.Invoke("Unpatching:", ex);
		}
	}

	// A game member looked up by name, once: null (logged) if a game update renamed it, so only the feature that needs it
	// stands down. A throwing static initializer would instead break every use of its class.
	internal static T Reflect<T>(string what, Func<T> lookup) where T : class
	{
		try
		{
			return lookup() ?? throw new MissingMemberException(what);
		}
		catch (Exception ex)
		{
			LogOnce(what, $"{what} not found (a game update?); the features that need it are off:", ex);
			return null;
		}
	}

	// An error logged the first time only (per key): for paths that run every frame or physics step.
	internal static void LogOnce(string key, string message, Exception ex)
	{
		lock (Logged)
		{
			if (!Logged.Add(key))
			{
				return;
			}
		}
		if (_error != null)
		{
			_error(message, ex);
		}
		else
		{
			UnityEngine.Debug.LogWarning(message + " " + ex);   // before Init (a static field initializer ran early)
		}
	}
}
