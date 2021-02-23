﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Barotrauma
{
    public enum CoroutineStatus
    {
        Running, Success, Failure
    }

	public class CoroutineHandle
    {
        public readonly IEnumerator<object> Coroutine;
        public readonly string Name;

        public Exception Exception;
        public volatile bool AbortRequested;

        public Thread Thread;

        public CoroutineHandle(IEnumerator<object> coroutine, string name = "", bool useSeparateThread = false)
        {
            Coroutine = coroutine;
            Name = string.IsNullOrWhiteSpace(name) ? coroutine.ToString() : name;
            Exception = null;
        }

    }

    // Keeps track of all running coroutines, and runs them till the end.
	public static class CoroutineManager
    {
        static readonly List<CoroutineHandle> Coroutines = new List<CoroutineHandle>();

        public static float UnscaledDeltaTime, DeltaTime;

        public static CoroutineHandle StartCoroutine(IEnumerable<object> func, string name = "", bool useSeparateThread = false)
        {
            var handle = new CoroutineHandle(func.GetEnumerator(), name);
            lock (Coroutines)
            {
                Coroutines.Add(handle);
            }

            handle.Thread = null;
            if (useSeparateThread)
            {
                handle.Thread = new Thread(() => { ExecuteCoroutineThread(handle); })
                {
                    Name = "Coroutine Thread (" + handle.Name + ")",
                    IsBackground = true
                };
                handle.Thread.Start();
            }

            return handle;
        }

        public static CoroutineHandle Invoke(Action action)
        {
            return StartCoroutine(DoInvokeAfter(action, 0.0f));
        }

        public static CoroutineHandle InvokeAfter(Action action, float delay)
        {
            return StartCoroutine(DoInvokeAfter(action, delay));
        }

        private static IEnumerable<object> DoInvokeAfter(Action action, float delay)
        {
            if (action == null)
            {
                yield return CoroutineStatus.Failure;
            }

            if (delay > 0.0f)
            {
                yield return new WaitForSeconds(delay);
            }

            action();

            yield return CoroutineStatus.Success;
        }


        public static bool IsCoroutineRunning(string name)
        {
            lock (Coroutines)
            {
                return Coroutines.Any(c => c.Name == name);
            }
        }

        public static bool IsCoroutineRunning(CoroutineHandle handle)
        {
            lock (Coroutines)
            {
                return Coroutines.Contains(handle);
            }
        }

        public static void StopCoroutines(string name)
        {
            lock (Coroutines)
            {
                HandleCoroutineStopping(c => c.Name == name);
                Coroutines.RemoveAll(c => c.Name == name);
            }
        }

        public static void StopCoroutines(CoroutineHandle handle)
        {
            lock (Coroutines)
            {
                HandleCoroutineStopping(c => c == handle);
                Coroutines.RemoveAll(c => c == handle);
            }
        }

        private static void HandleCoroutineStopping(Func<CoroutineHandle, bool> filter)
        {
            foreach (CoroutineHandle coroutine in Coroutines)
            {
                if (filter(coroutine))
                {
                    coroutine.AbortRequested = true;
                    if (coroutine.Thread != null)
                    {
                        bool joined = false;
                        while (!joined)
                        {
#if CLIENT
                            CrossThread.ProcessTasks();
#endif
                            joined = coroutine.Thread.Join(TimeSpan.FromMilliseconds(500));
                        }
                    }
                }
            }
        }

        public static void ExecuteCoroutineThread(CoroutineHandle handle)
        {
            try
            {
                while (!handle.AbortRequested)
                {
                    if (handle.Coroutine.Current != null)
                    {
                        WaitForSeconds wfs = handle.Coroutine.Current as WaitForSeconds;
                        if (wfs != null)
                        {
                            Thread.Sleep((int)(wfs.TotalTime * 1000));
                        }
                        else
                        {
                            switch ((CoroutineStatus)handle.Coroutine.Current)
                            {
                                case CoroutineStatus.Success:
                                    return;

                                case CoroutineStatus.Failure:
                                    DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
                                    return;
                            }
                        }
                    }

                    Thread.Yield();
                    if (!handle.Coroutine.MoveNext()) return;
                }
            }
            catch (ThreadAbortException)
            {
                //not an error, don't worry about it
            }
            catch (Exception e)
            {
                handle.Exception = e;
                DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has thrown an exception", e);
            }
        }

        private static bool IsDone(CoroutineHandle handle)
        {
#if !DEBUG
            try
            {
#endif
                if (handle.Thread == null)
                {
                    if (handle.AbortRequested) { return true; }
                    if (handle.Coroutine.Current != null)
                    {
                        WaitForSeconds wfs = handle.Coroutine.Current as WaitForSeconds;
                        if (wfs != null)
                        {
                            if (!wfs.CheckFinished(UnscaledDeltaTime)) return false;
                        }
                        else
                        {
                            switch ((CoroutineStatus)handle.Coroutine.Current)
                            {
                                case CoroutineStatus.Success:
                                    return true;

                                case CoroutineStatus.Failure:
                                    DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
                                    return true;
                            }
                        }
                    }

                    handle.Coroutine.MoveNext();
                    return false;
                }
                else
                {
                    if (handle.Thread.ThreadState.HasFlag(ThreadState.Stopped))
                    {
                        if (handle.Exception!=null || (CoroutineStatus)handle.Coroutine.Current == CoroutineStatus.Failure)
                        {
                            DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
                        }
                        return true;
                    }
                    return false;
                }
#if !DEBUG
            }
            catch (Exception e)
            {
#if CLIENT && WINDOWS
                if (e is SharpDX.SharpDXException) { throw; }
#endif
                DebugConsole.ThrowError("Coroutine " + handle.Name + " threw an exception: " + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
                handle.Exception = e;
                return true;
            }
#endif
        }
        // Updating just means stepping through all the coroutines
        public static void Update(float unscaledDeltaTime, float deltaTime)
        {
            UnscaledDeltaTime = unscaledDeltaTime;
            DeltaTime = deltaTime;

            List<CoroutineHandle> coroutineList;
            lock (Coroutines)
            {
                coroutineList = Coroutines.ToList();
            }

            foreach (var coroutine in coroutineList)
            {
                if (IsDone(coroutine))
                {
                    lock (Coroutines)
                    {
                        Coroutines.Remove(coroutine);
                    }
                }
            }
        }
    }
  
	public class WaitForSeconds
    {
        public readonly float TotalTime;

        float timer;
        bool ignorePause;

        public WaitForSeconds(float time, bool ignorePause = true)
        {
            timer = time;
            TotalTime = time;
            this.ignorePause = ignorePause;
        }

        public bool CheckFinished(float deltaTime) 
        {
#if !SERVER
            if (ignorePause || !GUI.PauseMenuOpen)
            {
                timer -= deltaTime;
            }
#else
            timer -= deltaTime;
#endif
            return timer <= 0.0f;
        }
    }
}
