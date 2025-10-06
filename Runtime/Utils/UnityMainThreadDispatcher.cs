using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UG.Utils
{
    /// <summary>
    /// Dispatcher for executing code on the main thread
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static UnityMainThreadDispatcher _instance;
        private readonly Queue<Action> _actionQueue = new Queue<Action>();
        private readonly object _lockObject = new object();

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException(
                        "UnityMainThreadDispatcher not found. Ensure it is initialized by calling Initialize() from the main thread during startup.");
                }
                return _instance;
            }
        }

        public static void Initialize()
        {
            if (_instance == null)
            {
                // Create a new GameObject with the dispatcher attached
                var go = new GameObject("UnityMainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        public void Enqueue(Action action)
        {
            lock (_lockObject)
            {
                _actionQueue.Enqueue(action);
            }
        }

        private void Update()
        {
            lock (_lockObject)
            {
                while (_actionQueue.Count > 0)
                {
                    _actionQueue.Dequeue()?.Invoke();
                }
            }
        }

        // Helper method to execute an async task on the main thread
        public Task EnqueueAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }
    }
} 