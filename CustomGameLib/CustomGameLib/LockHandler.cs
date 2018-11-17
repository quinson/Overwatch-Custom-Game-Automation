﻿/* Lock Guide
Any public method in the custom game class should use a lock.

Passive locks is for functions that scan the Overwatch window but do not interact with it. 
Any amount of passive locks will run side by side.
- Usage in CustomGame class:
using (LockHandler.Passive)
- Usage in CustomGameBase class:
using (cg.LockHandler.Passive)

Semi-Passive locks is for functions that interact with the Overwatch window but do not go into any other menues allowing scanning to continue as normal. 
Only 1 semi-passive lock will run at a time.
- Usage in CustomGame class:
using (LockHandler.SemiPassive)
- Usage in CustomGameBase class:
using (cg.LockHandler.SemiPassive)

Interactive locks is for functions that interact with the Overwatch window and go into new menus. This will block passive locks from running during the interactive lock.
Only 1 interactive lock will run at a time.
- Usage in CustomGame class:
using (LockHandler.Interactive)
- Usage in CustomGameBase class:
using (cg.LockHandler.Interactive)

A deadlock will occur if LockHandler.Passive, LockHandler.Interactive, or LockHandler.SemiPassive are accessed outside of a using() statement.

The following will also cause a deadlock:
using (LockHandler.Interactive)
{
    Parallel.For(0, 10, (i) => 
    {
        using (LockHandler.Interactive)
        {
            // ...
        }
    });
}
Because the first using statement is waiting for Parallel.For to complete, and the second using statement is waiting for the first using statement to release the lock.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Deltin.CustomGameAutomation
{
    partial class CustomGame
    {
        internal readonly LockHandler LockHandler = new LockHandler();
    }

    internal class LockHandler
    {
        public LockHandler() { }

        public Locker Passive { get { return new Locker(PassiveI, this); } }
        public Locker Interactive { get { return new Locker(InteractiveI, this); } }
        public Locker SemiPassive { get { return new Locker(SemiPassiveI, this); } }

        private const int PassiveI = 0;
        private const int InteractiveI = 1;
        private const int SemiPassiveI = 2;

        private List<PassiveData> PassiveList = new List<PassiveData>(); // List of passive methods running.
        private readonly object AccessLock = new object(); // Lock for accessing the PassiveList list.

        private readonly object InteractiveLock = new object(); // Lock for semi-passive and interactive methods.
        private int InteractiveThreadID = -1; // The ID of the interactive thread. -1 for no interactive threads.

        private void SetLock(Locker locker)
        {
            int threadID = Thread.CurrentThread.ManagedThreadId;
            switch (locker.LockType)
            {
                // Passive:
                case PassiveI:
                    // Add the thread id to the list of passive threads.
                    SpinWait.SpinUntil(() => { return InteractiveThreadID == -1 || InteractiveThreadID == threadID; });
                    lock (AccessLock)
                        PassiveList.Add(new PassiveData(threadID));
                    break;

                // Interactive:
                case InteractiveI:
                    // Ignore calling thread if the calling thread is passive.
                    lock (AccessLock)
                        for (int i = 0; i < PassiveList.Count; i++)
                            if (PassiveList[i].ThreadID == threadID)
                            {
                                PassiveList[i].Waiting = true;
                                break;
                            }
                    // Wait for all passive and interactive methods on other threads to finish.
                    Monitor.Enter(InteractiveLock);
                    SpinWait.SpinUntil(() => { lock (AccessLock) return !PassiveList.Any(p => p.ThreadID != threadID && !p.Waiting); });
                    InteractiveThreadID = threadID;
                    break;

                // Semi-Passive:
                case SemiPassiveI:
                    Monitor.Enter(InteractiveLock);
                    break;
            }
        }
        private void Unlock(Locker locker)
        {
            int threadID = Thread.CurrentThread.ManagedThreadId;
            switch (locker.LockType)
            {
                // Passive:
                case PassiveI:
                    // Remove from passive list.
                    lock (AccessLock)
                        PassiveList.RemoveAll(v => v.ThreadID == threadID);
                    break;

                // Interactive:
                case InteractiveI:
                    lock (AccessLock)
                        // Stop ignoring passive caller if it exists.
                        for (int i = 0; i < PassiveList.Count; i++)
                            if (PassiveList[i].ThreadID == threadID)
                            {
                                PassiveList[i].Waiting = false;
                                break;
                            }
                    InteractiveThreadID = -1;
                    Monitor.Exit(InteractiveLock);
                    break;

                // Semi-Passive:
                case SemiPassiveI:
                    Monitor.Exit(InteractiveLock);
                    break;
            }
        }

        public class Locker : IDisposable
        {
            public Locker(int lockType, LockHandler lockHandler)
            {
                LockType = lockType;
                LockHandler = lockHandler;
                LockHandler.SetLock(this);
            }
            public int LockType { get; private set; }
            private LockHandler LockHandler;
#if DEBUG
            private bool Disposed = false;
#endif

            public void Dispose()
            {
#if DEBUG
                Disposed = true;
#endif
                LockHandler.Unlock(this);
            }

#if DEBUG
            ~Locker()
            {
                if (!Disposed)
                {
                    CustomGameDebug.WriteLine("Error: locker was used outside of using statement\n" + Environment.StackTrace);
                    Dispose();
                }
            }
#endif
        }

        private class PassiveData
        {
            public PassiveData(int threadID)
            {
                ThreadID = threadID;
            }
            public int ThreadID { get; private set; }
            public bool Waiting { get; set; }
        }
    }
}