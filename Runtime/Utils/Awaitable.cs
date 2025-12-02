using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace UG.Utils
{
    /// <summary>
    /// Unity 2022 compatibility wrapper for Unity 2023.1+ Awaitable API
    /// Provides similar functionality using Task-based approaches
    /// </summary>
    public struct Awaitable
    {
        private readonly Task _task;

        private Awaitable(Task task)
        {
            _task = task;
        }

        public TaskAwaiter GetAwaiter() => _task.GetAwaiter();

        /// <summary>
        /// Switches execution to a background thread
        /// </summary>
        public static Awaitable BackgroundThreadAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Task.Run(() => tcs.SetResult(true));
            return new Awaitable(tcs.Task);
        }

        /// <summary>
        /// Waits for the next Unity frame
        /// </summary>
        public static Awaitable NextFrameAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            
            // Use UnityMainThreadDispatcher to wait for next frame
            if (UnityMainThreadDispatcher.Instance != null)
            {
                UnityMainThreadDispatcher.Instance.StartCoroutine(WaitForNextFrameCoroutine(() =>
                {
                    tcs.SetResult(true);
                }));
            }
            else
            {
                // Fallback to Task.Yield if dispatcher not available
                Task.Yield().GetAwaiter().OnCompleted(() => tcs.SetResult(true));
            }
            
            return new Awaitable(tcs.Task);
        }

        /// <summary>
        /// Switches execution to the main Unity thread
        /// </summary>
        public static Awaitable MainThreadAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            
            if (UnityMainThreadDispatcher.Instance != null)
            {
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    tcs.SetResult(true);
                });
            }
            else
            {
                // If dispatcher not initialized, just complete immediately
                // This shouldn't happen in normal usage, but provides a fallback
                tcs.SetResult(true);
            }
            
            return new Awaitable(tcs.Task);
        }

        private static IEnumerator WaitForNextFrameCoroutine(Action callback)
        {
            yield return null; // Wait one frame
            callback?.Invoke();
        }
    }
}

